using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Purchasing;
using SalesApp.Core.Returns;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data.Configurations;
using SalesApp.Infrastructure.Services;
using static SalesApp.Infrastructure.DemoData.DemoDataGenerator;

namespace SalesApp.Infrastructure.DemoData;

/// <summary>
/// Day-by-day trading simulation behind <see cref="DemoDataGenerator"/>. Each day: weekly purchase orders and their
/// deliveries (with the supplier's bill and, on their terms, our payment), returns due that day (credit notes from
/// customers, goods sent back to suppliers), invoices due that day (goods leave stock), then new orders (stock is
/// reserved so it's never oversold). Everything is built in memory and saved in a few bulk steps at the end.
/// </summary>
internal sealed class Simulation(
    DemoDataGenerator generator,
    List<DemoProduct> products,
    List<DemoCustomer> customers,
    Dictionary<string, Supplier> suppliers,
    Team team,
    CompanySettings settings)
{
    internal sealed class DemoOrder(SalesOrder entity, DemoCustomer customer)
    {
        public SalesOrder Entity { get; } = entity;
        public DemoCustomer Customer { get; } = customer;
        public List<(DemoProduct Product, SalesOrderLine Line)> Lines { get; } = [];
        public DateOnly? QuotedOn { get; set; }
        public int Sequence { get; set; }
    }

    internal sealed record DemoInvoice(Invoice Entity, DemoOrder Order, int Sequence);

    internal sealed record DemoMovement(StockMovement Entity, DemoProduct Product, DemoInvoice? Invoice, StockReceipt? Receipt,
        DemoCreditNote? CreditNote = null, DemoSupplierReturn? SupplierReturn = null);

    internal sealed class DemoPurchaseOrder(PurchaseOrder entity, Supplier supplier, int sequence)
    {
        public PurchaseOrder Entity { get; } = entity;
        public Supplier Supplier { get; } = supplier;
        public int Sequence { get; } = sequence;
        public List<(DemoProduct Product, PurchaseOrderLine Line)> Lines { get; } = [];
    }

    internal sealed record DemoReceipt(StockReceipt Entity, DemoPurchaseOrder Order, int Sequence, List<(PurchaseOrderLine OrderLine, StockReceiptLine Line)> Lines);

    internal sealed record DemoBill(SupplierBill Entity, DemoPurchaseOrder Order, int Sequence, List<(PurchaseOrderLine OrderLine, SupplierBillLine Line)> Lines);

    internal sealed record DemoCreditNote(CreditNote Entity, DemoInvoice Invoice, InvoiceLine Line, int Sequence);

    internal sealed record DemoSupplierReturn(SupplierReturn Entity, DemoBill Bill, SupplierBillLine Line, int Sequence);

    private readonly Random _random = generator.Random;
    private readonly Dictionary<DateOnly, List<DemoOrder>> _toInvoice = [];
    private readonly Dictionary<(int CustomerId, DateOnly Day), decimal> _cashByDay = [];
    private readonly Dictionary<DateOnly, List<(DemoPurchaseOrder Order, Dictionary<PurchaseOrderLine, decimal> Quantities, string Notes)>> _deliveries = [];
    private readonly Dictionary<string, int> _supplierInvoiceNumbers = [];
    private readonly Dictionary<DateOnly, List<DemoInvoice>> _returns = [];
    private readonly Dictionary<DateOnly, List<DemoBill>> _supplierReturns = [];
    private readonly Dictionary<int, List<DemoCreditNote>> _customerCredit = [];
    private readonly Dictionary<DemoCreditNote, DateOnly> _refundOn = [];
    private readonly HashSet<DemoCreditNote> _kept = [];
    private readonly Dictionary<int, List<DemoSupplierReturn>> _supplierCredit = [];
    private readonly Dictionary<string, int> _supplierCreditNoteNumbers = [];
    private Dictionary<string, (string Name, bool UsesUtgst)> _states = [];
    private int _sequence;

    public List<DemoOrder> Orders { get; } = [];
    public List<DemoInvoice> Invoices { get; } = [];
    public List<DemoPurchaseOrder> PurchaseOrders { get; } = [];
    public List<DemoReceipt> Receipts { get; } = [];
    public List<DemoBill> Bills { get; } = [];
    public List<DemoMovement> Movements { get; } = [];
    public List<DemoCreditNote> CreditNotes { get; } = [];
    public List<DemoSupplierReturn> SupplierReturns { get; } = [];
    public int PaymentCount { get; private set; }
    public int SupplierPaymentCount { get; private set; }
    public int RefundCount { get; private set; }
    public int RejectionCount { get; private set; }

    // Anomalies, placed relative to today so they always fall inside a long enough history.
    private DateOnly BulkOrderDay => Weekday(generator.Today.AddDays(-150));
    private bool InStockout(DemoProduct p, DateOnly day) => NotRestocked(p, day) || _runningDown.Contains(p);

    private bool NotRestocked(DemoProduct p, DateOnly day) =>
        (p.Spec.Sku == "CY-60C" && day > generator.Today.AddDays(-130) && day < generator.Today.AddDays(-88)) ||
        (p.Spec.Sku == "FB-DNM" && day > generator.Today.AddDays(-320) && day < generator.Today.AddDays(-275)) ||
        // Not reordered for a few weeks, so they've dropped below their reorder level with no order placed:
        // what the reorder suggestions are for.
        (NotReorderedYet.Contains(p.Spec.Sku) && day > generator.Today.AddDays(-24));

    private static readonly HashSet<string> NotReorderedYet = ["FB-CAM", "PK-CON"];

    /// <summary>
    /// Products whose supplier is late as of today, with how much of its reorder level a part delivery brought at the
    /// end of the day (zero: nothing arrived). Their shelves are run down first (see <see cref="RunDownCap"/>): the one
    /// that got nothing has been out of stock for three weeks; the other two ran out in the last week or two, so
    /// they're below their reorder level with some left. That's the demo's opening scene: products to reorder and orders to
    /// chase.
    /// </summary>
    private static readonly Dictionary<string, decimal> DelayedNow = new() { ["PC-40"] = 0.6m, ["HT-TWL"] = 0.7m, ["GT-VST"] = 0 };

    /// <summary>Delayed products whose shelves are being run down.</summary>
    private readonly HashSet<DemoProduct> _runningDown = [];

    /// <summary>When a delayed product's shelf is empty: three weeks back for the one that got nothing, four days for the others.</summary>
    private DateOnly EmptyBy(decimal cover) => generator.Today.AddDays(cover == 0 ? -21 : -4);

    /// <summary>
    /// The most a delayed product can be topped up by now: what it will probably sell, even 40% slower than it has
    /// sold so far, before its shelf has to be empty. A top-up is sized from a rough estimate of demand and could last
    /// it weeks past that; topped up a little each week instead, there's not much left to buy out on the day (see
    /// <see cref="BuyOut"/>), and it isn't empty long before.
    /// </summary>
    private decimal RunDownCap(DemoProduct product, DateOnly day)
    {
        if (!DelayedNow.TryGetValue(product.Spec.Sku, out var cover))
        {
            return decimal.MaxValue;
        }

        var room = (decimal)(0.6 * SellingRate(product, day) * Math.Max(0, EmptyBy(cover).DayNumber - day.DayNumber)) - product.Available;
        return Math.Max(0, FloorTo(room, product.Spec.Step));
    }

    /// <summary>
    /// A regular buyer of a delayed product takes what's left of it, invoiced the same day, so its shelf is empty when
    /// the story needs it to be. These sell in a few big orders, so the last of the stock could otherwise sit for weeks.
    /// </summary>
    private void BuyOut(DemoProduct product, DateOnly day, Dictionary<DemoProduct, decimal> orderedToday)
    {
        _runningDown.Add(product); // no goods coming back to the shelf from now on either
        var customer = customers.Where(c => c.IsActiveOn(day) && product.Spec.BoughtBy.Contains(c.Segment))
            .OrderByDescending(c => c.Favourites.Contains(product.Spec)).ThenByDescending(c => c.Size).FirstOrDefault();
        if (customer is not null && product.Available >= product.Spec.Step)
        {
            CreateOrder(day, customer, [(product, product.Available)], orderedToday, forceInvoice: true);
        }
    }

    /// <summary>What a product has really sold a day so far (the smoothed demand estimate drops while it's out of stock).</summary>
    private double SellingRate(DemoProduct product, DateOnly day) =>
        product.Sold > 0
            ? (double)product.Sold / Math.Max(1, day.DayNumber - generator.Start.DayNumber)
            : Math.Max(product.AverageDailyDemand, 0.1);

    private double Surge(DemoProduct p, DateOnly day) =>
        p.Spec.Sku == "GT-TSP" && day > generator.Today.AddDays(-280) && day < generator.Today.AddDays(-250) ? 3.0 : 1.0;

    /// <summary>Chance an order placed <paramref name="daysAgo"/> days ago is still open: 35% today, tapering to none after 10 days.</summary>
    private static double OpenChance(int daysAgo) => daysAgo < 10 ? 0.35 * (1 - daysAgo / 10.0) : 0;

    public void Run()
    {
        _states = generator.Db.States.AsNoTracking().ToDictionary(s => s.Code, s => (s.Name, s.UsesUtgst));

        foreach (var product in products)
        {
            var opening = RoundTo((decimal)(product.AverageDailyDemand * 45), product.Spec.Step);
            Move(product, StockMovementType.Opening, opening, generator.Start, null, null, reason: AdjustmentReason.OpeningBalance, notes: "Opening stock");
        }

        for (var day = generator.Start; day <= generator.Today; day = day.AddDays(1))
        {
            if (_deliveries.Remove(day, out var balances))
            {
                foreach (var (order, quantities, notes) in balances)
                {
                    Deliver(order, day, quantities, notes);
                }
            }

            if (day.DayOfWeek == DayOfWeek.Monday)
            {
                ReceiveWeeklyStock(day);
            }

            if (day == BulkOrderDay.AddDays(-3))
            {
                ReceiveForBulkOrder(day);
            }

            ProcessReturns(day);
            if (day == Weekday(generator.Today.AddDays(-5)))
            {
                ComplainLate(day);
            }

            InvoiceDueOrders(day);

            var orderedToday = products.ToDictionary(p => p, _ => 0m);
            if (day == BulkOrderDay)
            {
                PlaceBulkOrder(day, orderedToday);
            }

            foreach (var product in products.Where(p => DelayedNow.TryGetValue(p.Spec.Sku, out var cover) && day == EmptyBy(cover)))
            {
                BuyOut(product, day, orderedToday);
            }

            var count = generator.Poisson(generator.ExpectedOrders(day));
            for (var i = 0; i < count; i++)
            {
                PlaceOrder(day, orderedToday);
            }

            // Orders invoiced the same day they were placed.
            InvoiceDueOrders(day);

            if (day == generator.Today)
            {
                PlaceLateOrders(day);
            }

            // Smoothed demand (about a 4-week average) drives how much stock the weekly receipts top up.
            foreach (var (product, quantity) in orderedToday)
            {
                product.AverageDailyDemand = product.AverageDailyDemand * (27.0 / 28) + (double)quantity / 28;
            }
        }

        PlaceOpenOrders();
    }

    // ---- Stock in -------------------------------------------------------------------------------

    /// <summary>
    /// Each week, anything under three weeks' stock is topped up to five, as the season ahead will sell (the festive
    /// months a good deal more), so the busy months aren't cut short by empty shelves.
    /// </summary>
    private void ReceiveWeeklyStock(DateOnly day)
    {
        var bySupplier = new Dictionary<string, List<(DemoProduct Product, decimal Quantity)>>();
        var season = DemoCatalog.Seasonality[day.AddDays(14).Month - 1];
        foreach (var product in products)
        {
            if (NotRestocked(product, day))
            {
                continue;
            }

            var demand = Math.Max(product.AverageDailyDemand, 0.1) * season;
            if ((double)product.Available >= demand * 21)
            {
                continue;
            }

            var target = Math.Max((decimal)(demand * 35), product.Entity.ReorderQuantity);
            var quantity = RoundTo(target - product.Available, product.Spec.Step);
            var cap = RunDownCap(product, day);
            if (quantity > cap)
            {
                // Being run down from now on: no returns, split deliveries or replacements to refill the shelf later.
                _runningDown.Add(product);
                quantity = cap;
            }

            if (quantity <= 0)
            {
                continue;
            }

            var supplier = SupplierFor(product);
            (bySupplier.TryGetValue(supplier, out var list) ? list : bySupplier[supplier] = []).Add((product, quantity));
        }

        foreach (var (supplier, lines) in bySupplier)
        {
            Receive(day, supplier, lines);
        }
    }

    /// <summary>
    /// The orders behind today's supplier delays: placed three weeks ago, due ten days ago, and either part-delivered
    /// at the end of today or not delivered at all. They're late, which is what the assistant can chase.
    /// </summary>
    private void PlaceLateOrders(DateOnly day)
    {
        foreach (var product in products.Where(p => DelayedNow.ContainsKey(p.Spec.Sku)))
        {
            var demand = Math.Max(product.AverageDailyDemand, 0.1);
            var ordered = Math.Max(RoundTo((decimal)(demand * 30), product.Spec.Step), product.Entity.ReorderQuantity);
            var orderDate = generator.Today.AddDays(-20);
            var order = PlaceOrder(SupplierFor(product), orderDate, generator.Today.AddDays(-10), null, [(product, ordered, CostOn(product, orderDate))]);

            if (DelayedNow[product.Spec.Sku] > 0)
            {
                // Sized from its reorder level, so it stays below that however fast the product sells.
                var quantity = Math.Min(ordered, RoundTo(product.Entity.ReorderLevel * DelayedNow[product.Spec.Sku], product.Spec.Step));
                Deliver(order, day, new() { [order.Lines[0].Line] = quantity }, "Part delivery; supplier has delayed the balance");
            }
        }
    }

    /// <summary>Orders still open today: two on their way, a big one waiting for approval and a draft.</summary>
    private void PlaceOpenOrders()
    {
        var today = generator.Today;
        DemoProduct Product(string sku) => products.Single(p => p.Spec.Sku == sku);
        decimal MonthOf(DemoProduct p) => Math.Max(RoundTo((decimal)(Math.Max(p.AverageDailyDemand, 0.1) * 30), p.Spec.Step), p.Entity.ReorderQuantity);

        foreach (var (sku, daysAgo) in new[] { ("CY-20K", 2), ("PSF-30", 1) })
        {
            var product = Product(sku);
            PlaceOrder(SupplierFor(product), today.AddDays(-daysAgo), today.AddDays(6 - daysAgo), null, [(product, MonthOf(product), CostOn(product, today))]);
        }

        var cotton = Product("CY-30C");
        PlaceOrder(SupplierFor(cotton), today.AddDays(-1), today.AddDays(10), "Advance buying before the expected cotton price rise.",
            [(cotton, 3000m, CostOn(cotton, today))], PurchaseOrderStatus.PendingApproval);

        var tees = Product("GT-TSR");
        PlaceOrder(SupplierFor(tees), today, today.AddDays(14), "Festive season stock-up.", [(tees, MonthOf(tees), CostOn(tees, today))], PurchaseOrderStatus.Draft);
    }

    private void ReceiveForBulkOrder(DateOnly day)
    {
        var lines = products.Where(p => p.Spec.Sku is "CY-40C" or "CY-30C").Select(p => (p, p.Spec.Sku == "CY-40C" ? 9000m : 6000m)).ToList();
        Receive(day, DemoCatalog.Suppliers[0].Name, lines, "Special lot for a bulk order");
    }

    /// <summary>
    /// A purchase order placed a few days before the goods arrive, delivered today. Most deliveries come complete;
    /// some arrive in two parts a few days apart.
    /// </summary>
    private void Receive(DateOnly day, string supplier, List<(DemoProduct Product, decimal Quantity)> lines, string? notes = null)
    {
        var lead = _random.Next(3, 9);
        var order = PlaceOrder(supplier, day.AddDays(-lead), day.AddDays(_random.Next(-2, 2)), notes,
            [.. lines.Select(l => (l.Product, l.Quantity, CostOn(l.Product, day)))]);

        // Not for a product being run down: a balance turning up after it's been bought out would refill the shelf.
        var split = notes is null && !lines.Any(l => _runningDown.Contains(l.Product)) && _random.NextDouble() < 0.12;
        var first = order.Lines.ToDictionary(
            l => l.Line,
            l => split ? Math.Min(l.Line.Quantity, RoundTo(l.Line.Quantity * 0.6m, l.Product.Spec.Step)) : l.Line.Quantity);
        Deliver(order, day, first, notes);

        var rest = order.Lines.Where(l => l.Line.Quantity > first[l.Line]).ToDictionary(l => l.Line, l => l.Line.Quantity - first[l.Line]);
        if (rest.Count > 0)
        {
            var later = day.AddDays(_random.Next(3, 7));
            (_deliveries.TryGetValue(later, out var due) ? due : _deliveries[later] = []).Add((order, rest, "Balance of the order"));
        }
    }

    /// <summary>What a product costs from its supplier on a day: its cost share of the list price, give or take 3%.</summary>
    private decimal CostOn(DemoProduct product, DateOnly day) =>
        Math.Round(generator.PriceAt(product.Spec, day) * product.Spec.CostRatio * (decimal)(0.97 + _random.NextDouble() * 0.06), 2);

    private DemoPurchaseOrder PlaceOrder(string supplierName, DateOnly orderDate, DateOnly expectedDate, string? notes,
        List<(DemoProduct Product, decimal Quantity, decimal Price)> lines, PurchaseOrderStatus status = PurchaseOrderStatus.Ordered)
    {
        var supplier = suppliers[supplierName];
        var placedAt = ToUtc(orderDate);
        var entity = new PurchaseOrder
        {
            Number = string.Empty, // numbered in date order when saving
            Status = status,
            OrderDate = orderDate,
            ExpectedDate = expectedDate < orderDate ? orderDate : expectedDate,
            SupplierId = supplier.Id,
            IsInterState = supplier.Address.StateCode != settings.StateCode,
            Notes = notes,
            SubmittedAtUtc = status == PurchaseOrderStatus.Draft ? null : placedAt,
            OrderedAtUtc = status == PurchaseOrderStatus.Ordered ? placedAt : null,
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = placedAt,
            UpdatedAtUtc = placedAt,
        };
        var order = new DemoPurchaseOrder(entity, supplier, ++_sequence);
        foreach (var (product, quantity, price) in lines)
        {
            var line = new PurchaseOrderLine
            {
                LineNumber = entity.Lines.Count + 1,
                ProductId = product.Entity.Id,
                Quantity = quantity,
                UnitPrice = price,
                GstRate = product.GstRate,
            };
            entity.Lines.Add(line);
            order.Lines.Add((product, line));
        }

        var totals = GstCalculator.Calculate(entity.Lines.Select(PurchaseOrderService.ToGstInput), entity.IsInterState);
        entity.TaxableValue = totals.TaxableValue;
        entity.TaxAmount = totals.TaxAmount;
        entity.GrandTotal = totals.GrandTotal;

        // Large orders that went out were approved by the administrator first.
        if (status == PurchaseOrderStatus.Ordered && settings.PurchaseApprovalLimit is { } limit && entity.GrandTotal > limit)
        {
            entity.ApprovedByUserId = team.AdminId;
            entity.ApprovedAtUtc = placedAt.AddHours(2);
            entity.OrderedAtUtc = entity.ApprovedAtUtc;
        }

        PurchaseOrders.Add(order);
        return order;
    }

    /// <summary>Goods arriving against an order: stock in at the order's price, re-averaging cost, then the supplier's bill.</summary>
    private void Deliver(DemoPurchaseOrder order, DateOnly day, Dictionary<PurchaseOrderLine, decimal> quantities, string? notes)
    {
        var receipt = new StockReceipt
        {
            Number = string.Empty, // numbered in date order when saving
            ReceiptDate = day,
            SupplierName = order.Supplier.Name,
            SupplierReference = $"DC-{_random.Next(1000, 99999)}",
            Notes = notes,
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = ToUtc(day),
        };
        var demo = new DemoReceipt(receipt, order, ++_sequence, []);
        var replacements = new Dictionary<PurchaseOrderLine, decimal>();

        foreach (var (product, line) in order.Lines.Where(l => quantities.ContainsKey(l.Line)))
        {
            var quantity = quantities[line];

            // Now and then part of a delivery is refused at the door: it never enters stock and the mill replaces it
            // (not a product being run down, as the replacement would refill its shelf).
            var rejected = notes is null && quantity >= 10 * product.Spec.Step && !_runningDown.Contains(product) && _random.NextDouble() < 0.03
                ? RoundTo(quantity * (decimal)(0.05 + _random.NextDouble() * 0.1), product.Spec.Step)
                : 0m;
            var accepted = quantity - rejected;
            product.AverageCost = PurchaseCosting.AfterReceipt(product.AverageCost, product.OnHand, accepted, line.UnitPrice);
            var receiptLine = new StockReceiptLine
            {
                ProductId = product.Entity.Id,
                Quantity = accepted,
                UnitCost = line.UnitPrice,
                QuantityRejected = rejected,
                RejectionReason = rejected > 0 ? DemoCatalog.RejectionReasons[_random.Next(DemoCatalog.RejectionReasons.Length)] : null,
            };
            receipt.Lines.Add(receiptLine);
            demo.Lines.Add((line, receiptLine));
            Move(product, StockMovementType.Receipt, accepted, day, null, receipt, unitCost: line.UnitPrice);
            line.QuantityReceived += accepted;
            if (rejected > 0)
            {
                replacements[line] = rejected;
                RejectionCount++;
            }
        }

        order.Entity.Status = order.Entity.Lines.All(l => l.QuantityReceived >= l.Quantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartlyReceived;
        Receipts.Add(demo);

        if (replacements.Count > 0)
        {
            var later = day.AddDays(_random.Next(5, 11));
            (_deliveries.TryGetValue(later, out var due) ? due : _deliveries[later] = []).Add((order, replacements, "Replacement for goods rejected on delivery"));
        }

        // The supplier's invoice travels with the goods, but this week's haven't been entered yet.
        if (day <= generator.Today.AddDays(-5))
        {
            Bill(demo, day);
        }
    }

    /// <summary>
    /// The supplier's bill for a delivery. Now and then the mill charges more than the order said (a flagged
    /// difference, approved by the administrator once it's a month old) or adds freight. Then our payment, on their terms.
    /// </summary>
    private void Bill(DemoReceipt receipt, DateOnly day)
    {
        var order = receipt.Order;
        var supplier = order.Supplier;
        var billDate = day.AddDays(-_random.Next(0, 2));
        if (billDate < order.Entity.OrderDate)
        {
            billDate = order.Entity.OrderDate;
        }

        var priceDifference = _random.NextDouble() < 0.07;
        var charges = _random.NextDouble() < 0.3 ? Math.Round(600m + (decimal)_random.NextDouble() * 3900m, 0) : 0m;
        var lines = receipt.Lines
            .Select((l, i) => (l.OrderLine, l.Line.Quantity, Price: priceDifference && i == 0
                ? Math.Round(l.OrderLine.UnitPrice * (1.03m + (decimal)_random.NextDouble() * 0.03m), 2)
                : l.OrderLine.UnitPrice))
            .ToList();
        var gst = lines.Select(l => new GstLineInput(l.Quantity, l.Price, 0m, l.OrderLine.GstRate)).ToList();
        if (charges > 0)
        {
            gst.Add(new GstLineInput(1m, charges, 0m, 18m));
        }

        var totals = GstCalculator.Calculate(gst, order.Entity.IsInterState);
        var prefix = supplier.Name.Split(' ')[0].ToUpperInvariant()[..3];
        var invoiceNumber = _supplierInvoiceNumbers[supplier.Name] = _supplierInvoiceNumbers.GetValueOrDefault(supplier.Name) + 1;
        var bill = new SupplierBill
        {
            Number = string.Empty, // numbered in date order when saving
            FinancialYear = FinancialYear.StartYear(billDate),
            SupplierInvoiceNumber = $"{prefix}/{FinancialYear.Label(billDate)}/{invoiceNumber:D4}",
            BillDate = billDate,
            DueDate = billDate.AddDays(supplier.PaymentTermsDays),
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            SupplierGstin = supplier.Gstin,
            SupplierStateCode = supplier.Address.StateCode,
            IsInterState = order.Entity.IsInterState,
            TaxableValue = lines.Select((_, i) => totals.Lines[i].TaxableValue).Sum(),
            ChargesAmount = charges,
            ChargesGstRate = charges > 0 ? 18m : 0m,
            ChargesDescription = charges > 0 ? "Freight" : null,
            CgstAmount = totals.CgstAmount,
            SgstAmount = totals.SgstAmount,
            IgstAmount = totals.IgstAmount,
            RoundOff = totals.RoundOff,
            GrandTotal = totals.GrandTotal,
            SupplierTotal = totals.GrandTotal,
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = ToUtc(day).AddHours(5),
        };
        var demo = new DemoBill(bill, order, ++_sequence, []);

        var shares = PurchaseCosting.Allocate(charges, [.. lines.Select((_, i) => totals.Lines[i].TaxableValue)]);
        for (var i = 0; i < lines.Count; i++)
        {
            var (orderLine, quantity, price) = lines[i];
            var product = order.Lines.Single(l => l.Line == orderLine).Product;
            var tax = totals.Lines[i];
            var issue = BillMatching.LineIssue(new BillLineCheck(quantity, price, orderLine.GstRate, orderLine.UnitPrice, product.GstRate,
                orderLine.QuantityReceived, orderLine.QuantityBilled, product.Entity.Unit.ToString()), settings.BillPriceTolerancePercent);
            var line = new SupplierBillLine
            {
                LineNumber = i + 1,
                ProductId = product.Entity.Id,
                ProductSku = product.Entity.Sku,
                ProductName = product.Entity.Name,
                HsnCode = product.HsnCode,
                Unit = product.Entity.Unit,
                Quantity = quantity,
                UnitPrice = price,
                OrderedPrice = orderLine.UnitPrice,
                TaxableValue = tax.TaxableValue,
                GstRate = orderLine.GstRate,
                CgstRate = tax.CgstRate,
                CgstAmount = tax.CgstAmount,
                SgstRate = tax.SgstRate,
                SgstAmount = tax.SgstAmount,
                IgstRate = tax.IgstRate,
                IgstAmount = tax.IgstAmount,
                LineTotal = tax.LineTotal,
                Issue = issue,
            };
            bill.Lines.Add(line);
            demo.Lines.Add((orderLine, line));
            orderLine.QuantityBilled += quantity;
            product.AverageCost = PurchaseCosting.AfterBillAdjustment(product.AverageCost, product.OnHand, quantity, (price - orderLine.UnitPrice) * quantity + shares[i]);
        }

        if (bill.Lines.Any(l => l.Issue is not null))
        {
            bill.MatchStatus = BillMatchStatus.NeedsApproval;
            if (billDate < generator.Today.AddDays(-30))
            {
                bill.MatchStatus = BillMatchStatus.Approved;
                bill.ApprovedByUserId = team.AdminId;
                bill.ApprovedAtUtc = ToUtc(day.AddDays(1));
                bill.ApprovalNote = DemoCatalog.PriceApprovalNotes[_random.Next(DemoCatalog.PriceApprovalNotes.Length)];
            }
        }

        Bills.Add(demo);
        UseSupplierCredit(bill, billDate);
        PaySupplier(bill);

        // A few deliveries turn out faulty once opened and part goes back to the mill (until one has, each one does, so
        // there's a return to show: one can fall through when there's no stock to send back).
        if (_random.NextDouble() < 0.03 || (SupplierReturns.Count == 0 && _supplierReturns.Count == 0))
        {
            var returnDay = Weekday(billDate.AddDays(_random.Next(3, 15)));
            if (returnDay > billDate && returnDay <= generator.Today)
            {
                (_supplierReturns.TryGetValue(returnDay, out var due) ? due : _supplierReturns[returnDay] = []).Add(demo);
            }
        }
    }

    /// <summary>
    /// We pay most bills around their due date, some a few weeks late, and lately not Karur Home Textile Makers at
    /// all (a dispute), so payables have something overdue. A bill waiting for approval isn't paid.
    /// </summary>
    private void PaySupplier(SupplierBill bill)
    {
        SetStatus(bill);
        if (bill.MatchStatus == BillMatchStatus.NeedsApproval || bill.AmountCredited >= bill.GrandTotal)
        {
            return;
        }

        var held = bill.SupplierName == "Karur Home Textile Makers" && bill.BillDate > generator.Today.AddDays(-80);
        var payOn = _random.NextDouble() < 0.12 ? bill.DueDate.AddDays(_random.Next(8, 35)) : bill.DueDate.AddDays(_random.Next(-6, 4));
        if (payOn < bill.BillDate)
        {
            payOn = bill.BillDate;
        }

        if (held || payOn > generator.Today)
        {
            return;
        }

        var mode = bill.GrandTotal >= 200_000m
            ? generator.Pick([(PaymentMode.Rtgs, 0.7), (PaymentMode.Neft, 0.3)], m => m.Item2).Item1
            : generator.Pick([(PaymentMode.Neft, 0.7), (PaymentMode.Cheque, 0.25), (PaymentMode.Upi, 0.05)], m => m.Item2).Item1;
        bill.Payments.Add(new SupplierPayment
        {
            PaymentDate = payOn,
            Amount = bill.GrandTotal - bill.AmountCredited,
            Mode = mode,
            Reference = Reference(mode, payOn),
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = ToUtc(payOn).AddHours(6),
        });
        bill.AmountPaid = bill.GrandTotal - bill.AmountCredited;
        SetStatus(bill);
        SupplierPaymentCount++;
    }

    /// <summary>Credit with the supplier from earlier returns comes off their next bill.</summary>
    private void UseSupplierCredit(SupplierBill bill, DateOnly date)
    {
        if (!_supplierCredit.TryGetValue(bill.SupplierId, out var credits))
        {
            return;
        }

        foreach (var credit in credits.Where(c => c.Entity.ReturnDate <= date).ToList())
        {
            var use = Math.Min(Available(credit.Entity), bill.GrandTotal - bill.AmountCredited);
            if (use <= 0)
            {
                break;
            }

            credit.Entity.Adjustments.Add(new SupplierCreditAdjustment
            {
                SupplierBill = bill, AdjustmentDate = date, Amount = use, CreatedByUserId = team.ManagerId, CreatedAtUtc = ToUtc(date).AddHours(5),
            });
            credit.Entity.AmountAdjusted += use;
            bill.AmountCredited += use;
            if (Available(credit.Entity) == 0)
            {
                credits.Remove(credit);
            }
        }
    }

    // Deterministic on purpose (string.GetHashCode is randomised per process): combed yarn from one mill, carded from
    // another; polyester and viscose from Surat, poly-cotton from Erode.
    internal static string SupplierFor(DemoProduct product) => (product.Spec.Category switch
    {
        DemoCatalog.CottonYarn => DemoCatalog.Suppliers[product.Spec.Sku.EndsWith('C') ? 0 : 2],
        DemoCatalog.BlendedYarn => DemoCatalog.Suppliers[product.Spec.Sku == "PC-40" ? 1 : 7],
        DemoCatalog.Fabrics => DemoCatalog.Suppliers[3],
        DemoCatalog.HomeTextiles => DemoCatalog.Suppliers[4],
        DemoCatalog.Garments => DemoCatalog.Suppliers[5],
        _ => DemoCatalog.Suppliers[6],
    }).Name;

    // ---- Orders ---------------------------------------------------------------------------------

    private void PlaceOrder(DateOnly day, Dictionary<DemoProduct, decimal> orderedToday)
    {
        var active = customers.Where(c => c.IsActiveOn(day)).ToList();
        if (active.Count == 0)
        {
            return;
        }

        var customer = generator.Pick(active, c => c.Size);
        var lineCount = generator.Pick([(1, 0.45), (2, 0.30), (3, 0.17), (4, 0.08)], l => l.Item2).Item1;
        var candidates = products.Where(p => p.Spec.BoughtBy.Contains(customer.Segment)).ToList();

        var chosen = new List<DemoProduct>();
        for (var i = 0; i < lineCount && chosen.Count < candidates.Count; i++)
        {
            // Mostly the customer's regular products, sometimes something new.
            var pool = _random.NextDouble() < 0.8
                ? candidates.Where(p => customer.Favourites.Contains(p.Spec) && !chosen.Contains(p)).ToList()
                : candidates.Where(p => !chosen.Contains(p)).ToList();
            if (pool.Count == 0)
            {
                pool = candidates.Where(p => !chosen.Contains(p)).ToList();
            }

            chosen.Add(generator.Pick(pool, p => p.Spec.Weight * Surge(p, day)));
        }

        var lines = chosen
            .Select(p => (Product: p, Quantity: p.Spec.QtyMin + (decimal)_random.NextDouble() * (p.Spec.QtyMax - p.Spec.QtyMin) * (decimal)Math.Sqrt(customer.Size)))
            .ToList();
        CreateOrder(day, customer, lines, orderedToday, forceInvoice: false);
    }

    private void PlaceBulkOrder(DateOnly day, Dictionary<DemoProduct, decimal> orderedToday)
    {
        var customer = customers.Where(c => c.Segment == Segment.Knitter && c.IsActiveOn(day)).OrderByDescending(c => c.Size).FirstOrDefault()
            ?? customers.Where(c => c.IsActiveOn(day)).OrderByDescending(c => c.Size).First();
        var lines = products.Where(p => p.Spec.Sku is "CY-40C" or "CY-30C").Select(p => (p, p.Spec.Sku == "CY-40C" ? 9000m : 6000m)).ToList();
        CreateOrder(day, customer, lines, orderedToday, forceInvoice: true, note: "Bulk order for export programme");
    }

    private void CreateOrder(DateOnly day, DemoCustomer customer, List<(DemoProduct Product, decimal Quantity)> wanted,
        Dictionary<DemoProduct, decimal> orderedToday, bool forceInvoice, string? note = null)
    {
        // Decide what happens to the order first: only orders that will be fulfilled reserve stock.
        var daysAgo = generator.Today.DayNumber - day.DayNumber;
        var roll = _random.NextDouble();
        SalesOrderStatus status;
        DateOnly? invoiceDate = null;
        if (!forceInvoice && roll < 0.04)
        {
            status = SalesOrderStatus.Cancelled;
        }
        else if (!forceInvoice && roll < 0.04 + OpenChance(daysAgo))
        {
            // Still in the pipeline: most open orders are only days old, a few have waited over a week.
            var stage = _random.NextDouble();
            status = stage < 0.35 ? SalesOrderStatus.Draft : stage < 0.7 ? SalesOrderStatus.Quoted : SalesOrderStatus.Confirmed;
        }
        else
        {
            var candidate = day.AddDays(forceInvoice ? 0 : _random.Next(0, 4));
            status = candidate > generator.Today ? SalesOrderStatus.Confirmed : SalesOrderStatus.Invoiced;
            invoiceDate = status == SalesOrderStatus.Invoiced ? candidate : null;
        }

        var reserves = status is SalesOrderStatus.Confirmed or SalesOrderStatus.Invoiced;
        var order = new DemoOrder(new SalesOrder
        {
            Number = string.Empty,
            Status = status,
            OrderDate = day,
            CustomerId = customer.Entity.Id,
            PlaceOfSupplyStateCode = customer.Entity.BillingAddress.StateCode,
            CustomerReference = _random.NextDouble() < 0.4 ? $"PO-{_random.Next(1000, 99999)}" : null,
            Notes = note,
            CreatedByUserId = customer.Entity.AssignedRepId ?? team.ManagerId,
            CreatedAtUtc = ToUtc(day),
            UpdatedAtUtc = ToUtc(day),
        }, customer) { Sequence = ++_sequence };

        foreach (var (product, desired) in wanted)
        {
            var quantity = RoundTo(desired, product.Spec.Step);
            if (reserves)
            {
                // Can't promise more than is free; a short line becomes a smaller one (or is dropped).
                quantity = Math.Min(quantity, FloorTo(product.Available, product.Spec.Step));
            }

            if (quantity < product.Spec.Step)
            {
                continue;
            }

            var line = new SalesOrderLine
            {
                LineNumber = order.Lines.Count + 1,
                ProductId = product.Entity.Id,
                Quantity = quantity,
                UnitPrice = generator.PriceAt(product.Spec, day),
                DiscountPercent = customer.Discount,
                GstRate = product.GstRate,
            };
            order.Lines.Add((product, line));
            order.Entity.Lines.Add(line);
            orderedToday[product] += quantity;
            if (reserves)
            {
                product.Reserved += quantity;
            }
        }

        if (order.Lines.Count == 0)
        {
            return; // nothing in stock to sell: a lost sale, which is what a stockout looks like in the data
        }

        var (taxable, tax, total) = OrderTotals(order.Entity.Lines, customer.IsInterState);
        order.Entity.TaxableValue = taxable;
        order.Entity.TaxAmount = tax;
        order.Entity.GrandTotal = total;

        if (status != SalesOrderStatus.Draft && (status == SalesOrderStatus.Quoted || _random.NextDouble() < 0.3))
        {
            order.QuotedOn = day;
            order.Entity.QuotedAtUtc = ToUtc(day);
            order.Entity.ValidUntil = day.AddDays(15);
        }

        if (status is SalesOrderStatus.Confirmed or SalesOrderStatus.Invoiced)
        {
            order.Entity.ConfirmedAtUtc = ToUtc(day);
        }

        if (status == SalesOrderStatus.Cancelled)
        {
            var cancelled = day.AddDays(_random.Next(1, 6));
            order.Entity.CancelledAtUtc = ToUtc(cancelled > generator.Today ? generator.Today : cancelled);
            order.Entity.CancellationReason = DemoCatalog.CancelReasons[_random.Next(DemoCatalog.CancelReasons.Length)];
        }

        Orders.Add(order);
        if (invoiceDate is { } date)
        {
            (_toInvoice.TryGetValue(date, out var due) ? due : _toInvoice[date] = []).Add(order);
        }
    }

    // ---- Invoices and payments -------------------------------------------------------------------

    private void InvoiceDueOrders(DateOnly day)
    {
        if (!_toInvoice.Remove(day, out var orders))
        {
            return;
        }

        foreach (var order in orders)
        {
            var customer = order.Customer;
            var pos = _states[order.Entity.PlaceOfSupplyStateCode];
            var totals = GstCalculator.Calculate(order.Lines.Select(l => OrderLineInput(l.Line)), customer.IsInterState);
            var address = customer.Entity.BillingAddress;

            var invoice = new Invoice
            {
                Number = string.Empty,
                FinancialYear = FinancialYear.StartYear(day),
                InvoiceDate = day,
                DueDate = day.AddDays(customer.Entity.PaymentTermsDays),
                SalesOrder = order.Entity,
                CustomerId = customer.Entity.Id,
                SellerName = settings.LegalName,
                SellerGstin = settings.Gstin,
                SellerStateCode = settings.StateCode,
                SellerAddress = SellerAddress(settings, _states[settings.StateCode].Name),
                CustomerName = customer.Entity.Name,
                CustomerGstin = customer.Entity.Gstin,
                BillingAddress = new InvoiceAddress
                {
                    Line1 = address.Line1, Line2 = address.Line2, City = address.City,
                    StateCode = address.StateCode, StateName = _states[address.StateCode].Name, Pincode = address.Pincode,
                },
                PlaceOfSupplyStateCode = order.Entity.PlaceOfSupplyStateCode,
                PlaceOfSupplyStateName = pos.Name,
                IsInterState = customer.IsInterState,
                UsesUtgst = !customer.IsInterState && pos.UsesUtgst,
                CustomerReference = order.Entity.CustomerReference,
                TaxableValue = totals.TaxableValue,
                CgstAmount = totals.CgstAmount,
                SgstAmount = totals.SgstAmount,
                IgstAmount = totals.IgstAmount,
                RoundOff = totals.RoundOff,
                GrandTotal = totals.GrandTotal,
                CreatedByUserId = team.ManagerId,
                CreatedAtUtc = ToUtc(day).AddHours(3),
            };

            for (var i = 0; i < order.Lines.Count; i++)
            {
                var (product, line) = order.Lines[i];
                var tax = totals.Lines[i];
                invoice.Lines.Add(new InvoiceLine
                {
                    LineNumber = i + 1,
                    ProductId = product.Entity.Id,
                    ProductSku = product.Entity.Sku,
                    ProductName = product.Entity.Name,
                    HsnCode = product.HsnCode,
                    Unit = product.Entity.Unit,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountPercent = line.DiscountPercent,
                    GrossAmount = tax.GrossAmount,
                    DiscountAmount = tax.DiscountAmount,
                    TaxableValue = tax.TaxableValue,
                    GstRate = line.GstRate,
                    CgstRate = tax.CgstRate,
                    CgstAmount = tax.CgstAmount,
                    SgstRate = tax.SgstRate,
                    SgstAmount = tax.SgstAmount,
                    IgstRate = tax.IgstRate,
                    IgstAmount = tax.IgstAmount,
                    LineTotal = tax.LineTotal,
                });
            }

            var demoInvoice = new DemoInvoice(invoice, order, ++_sequence);
            Invoices.Add(demoInvoice);

            foreach (var (product, line) in order.Lines)
            {
                product.Reserved -= line.Quantity;
                Move(product, StockMovementType.Sale, -line.Quantity, day, demoInvoice, null);
                product.Sold += line.Quantity;
            }

            UseCustomerCredit(invoice, day);
            AddPayments(invoice, customer);

            // About one invoice in 45 sees a return or a price correction, usually within three weeks but sometimes only
            // after it was paid (that credit is then used on the next invoice or refunded); the satin far more often.
            var returnProne = order.Lines.Any(l => l.Product.Spec.Sku == DemoCatalog.ReturnProneSku);
            if (_random.NextDouble() < (returnProne ? 0.25 : 0.022))
            {
                var returnDay = Weekday(day.AddDays(_random.NextDouble() < 0.3 ? _random.Next(30, 61) : _random.Next(3, 21)));
                if (returnDay > day && returnDay <= generator.Today)
                {
                    (_returns.TryGetValue(returnDay, out var due) ? due : _returns[returnDay] = []).Add(demoInvoice);
                }
            }
        }
    }

    /// <summary>Credit the customer has from earlier credit notes comes off their next invoice.</summary>
    private void UseCustomerCredit(Invoice invoice, DateOnly date)
    {
        if (!_customerCredit.TryGetValue(invoice.CustomerId, out var credits))
        {
            return;
        }

        foreach (var credit in credits.Where(c => c.Entity.CreditNoteDate <= date && !_refundOn.ContainsKey(c) && !_kept.Contains(c)).ToList())
        {
            var use = Math.Min(Available(credit.Entity), invoice.GrandTotal - invoice.AmountCredited);
            if (use <= 0)
            {
                break;
            }

            credit.Entity.Adjustments.Add(new CreditAdjustment
            {
                Invoice = invoice, AdjustmentDate = date, Amount = use, CreatedByUserId = team.ManagerId, CreatedAtUtc = ToUtc(date).AddHours(3),
            });
            credit.Entity.AmountAdjusted += use;
            invoice.AmountCredited += use;
            if (Available(credit.Entity) == 0)
            {
                credits.Remove(credit);
            }
        }
    }

    private static GstLineInput OrderLineInput(SalesOrderLine line) => new(line.Quantity, line.UnitPrice, line.DiscountPercent, line.GstRate);

    private void AddPayments(Invoice invoice, DemoCustomer customer)
    {
        var due = invoice.DueDate;
        var terms = customer.Entity.PaymentTermsDays;
        List<(DateOnly Date, decimal Share)> schedule = customer.Habit switch
        {
            PaymentHabit.Prompt => [(invoice.InvoiceDate.AddDays((int)(terms * (0.4 + _random.NextDouble() * 0.6))), 1m)],
            PaymentHabit.Normal => [(due.AddDays(_random.Next(-5, 16)), 1m)],
            PaymentHabit.Slow => [(due.AddDays(_random.Next(15, 61)), 1m)],
            PaymentHabit.Partial => [(due.AddDays(_random.Next(0, 11)), 0.5m), (due.AddDays(_random.Next(25, 51)), 0.5m)],
            // Defaulters stopped paying about five months ago; older invoices were paid, very late.
            _ => invoice.InvoiceDate < generator.Today.AddDays(-150) ? [(due.AddDays(_random.Next(40, 91)), 1m)] : [],
        };

        var payable = invoice.GrandTotal - invoice.AmountCredited;
        var remaining = payable;
        for (var i = 0; i < schedule.Count; i++)
        {
            var (date, share) = schedule[i];
            if (date < invoice.InvoiceDate)
            {
                date = invoice.InvoiceDate;
            }

            if (date > generator.Today)
            {
                break; // not paid yet
            }

            var amount = i == schedule.Count - 1 ? remaining : Math.Round(payable * share, 0);
            if (amount <= 0)
            {
                break;
            }

            var mode = ChooseMode(amount, customer.Entity.Id, date);
            invoice.Payments.Add(new Payment
            {
                PaymentDate = date,
                Amount = amount,
                Mode = mode,
                Reference = Reference(mode, date),
                CreatedByUserId = team.ManagerId,
                CreatedAtUtc = ToUtc(date).AddHours(4),
            });
            remaining -= amount;
            PaymentCount++;
        }

        invoice.AmountPaid = payable - remaining;
        SetStatus(invoice);
    }

    private static void SetStatus(Invoice invoice) =>
        invoice.Status = invoice.AmountPaid + invoice.AmountCredited >= invoice.GrandTotal ? InvoiceStatus.Paid
            : invoice.AmountPaid + invoice.AmountCredited > 0 ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Unpaid;

    private static void SetStatus(SupplierBill bill) =>
        bill.Status = bill.AmountPaid + bill.AmountCredited >= bill.GrandTotal ? SupplierBillStatus.Paid
            : bill.AmountPaid + bill.AmountCredited > 0 ? SupplierBillStatus.PartiallyPaid
            : SupplierBillStatus.Unpaid;

    // ---- Returns ------------------------------------------------------------------------------------

    private void ProcessReturns(DateOnly day)
    {
        if (_returns.Remove(day, out var invoices))
        {
            foreach (var invoice in invoices)
            {
                IssueCreditNote(invoice, day);
            }
        }

        if (_supplierReturns.Remove(day, out var bills))
        {
            foreach (var bill in bills)
            {
                ReturnToSupplier(bill, day);
            }
        }

        RefundUnusedCredit(day);
    }

    /// <summary>
    /// A credit note against one line of an invoice: three in four are goods coming back (damaged ones written off at
    /// once), the rest price corrections. Its value comes off what's still due, and payments still to come shrink to
    /// match; whatever is left is the customer's credit, for their next invoice or a refund.
    /// </summary>
    private DemoCreditNote? IssueCreditNote(DemoInvoice demoInvoice, DateOnly day)
    {
        var invoice = demoInvoice.Entity;
        var lines = demoInvoice.Order.Lines;
        var prone = lines.FindIndex(l => l.Product.Spec.Sku == DemoCatalog.ReturnProneSku);
        var index = prone >= 0 && _random.NextDouble() < 0.9 ? prone : _random.Next(lines.Count);
        var product = lines[index].Product;
        var line = invoice.Lines[index];
        var creditable = new CreditableLine(line.Quantity, line.TaxableValue, line.CgstAmount, line.SgstAmount, line.IgstAmount, line.GstRate);
        // Goods coming back would interrupt a stock-out the insights are meant to find, so those are price corrections.
        var goods = !InStockout(product, day) && (index == prone || _random.NextDouble() < 0.75);

        ReturnReason reason;
        decimal? quantity = null;
        ReturnDisposition? disposition = null;
        CreditAmounts amounts;
        if (goods)
        {
            var share = index == prone ? 0.3 + _random.NextDouble() * 0.3 : 0.1 + _random.NextDouble() * 0.3;
            var back = Math.Min(line.Quantity, RoundTo(line.Quantity * (decimal)share, product.Spec.Step));
            reason = index == prone
                ? (_random.NextDouble() < 0.6 ? ReturnReason.QualityIssue : ReturnReason.Damaged)
                : generator.Pick([(ReturnReason.Damaged, 0.35), (ReturnReason.WrongItem, 0.25), (ReturnReason.QualityIssue, 0.40)], r => r.Item2).Item1;
            disposition = reason == ReturnReason.Damaged || (reason == ReturnReason.QualityIssue && _random.NextDouble() < 0.3)
                ? ReturnDisposition.Damaged
                : ReturnDisposition.Restock;
            quantity = back;
            amounts = ReturnRules.ForGoods(creditable, CreditedSoFar.None, back, invoice.IsInterState, withGst: true);
        }
        else
        {
            reason = generator.Pick([(ReturnReason.RateDifference, 0.5), (ReturnReason.Discount, 0.3), (ReturnReason.ShortSupply, 0.2)], r => r.Item2).Item1;
            var amount = Math.Max(1m, Math.Round(line.TaxableValue * (decimal)(0.03 + _random.NextDouble() * 0.05), 0));
            amounts = ReturnRules.ForValue(creditable, CreditedSoFar.None, Math.Min(amount, line.TaxableValue), invoice.IsInterState, withGst: true);
        }

        var (roundOff, total) = ReturnRules.Total([amounts]);
        if (total <= 0)
        {
            return null;
        }

        var notes = DemoCatalog.ReturnNotes[reason];
        var address = invoice.BillingAddress;
        var note = new CreditNote
        {
            Number = string.Empty, // numbered in date order when saving
            FinancialYear = FinancialYear.StartYear(day),
            CreditNoteDate = day,
            Kind = goods ? CreditNoteKind.GoodsReturn : CreditNoteKind.PriceCorrection,
            Reason = reason,
            ReasonNote = notes[_random.Next(notes.Length)],
            WithGst = true,
            Invoice = invoice,
            CustomerId = invoice.CustomerId,
            SellerName = invoice.SellerName,
            SellerGstin = invoice.SellerGstin,
            SellerStateCode = invoice.SellerStateCode,
            SellerAddress = invoice.SellerAddress,
            CustomerName = invoice.CustomerName,
            CustomerGstin = invoice.CustomerGstin,
            BillingAddress = new InvoiceAddress
            {
                Line1 = address.Line1, Line2 = address.Line2, City = address.City,
                StateCode = address.StateCode, StateName = address.StateName, Pincode = address.Pincode,
            },
            PlaceOfSupplyStateCode = invoice.PlaceOfSupplyStateCode,
            PlaceOfSupplyStateName = invoice.PlaceOfSupplyStateName,
            IsInterState = invoice.IsInterState,
            UsesUtgst = invoice.UsesUtgst,
            TaxableValue = amounts.TaxableValue,
            CgstAmount = amounts.CgstAmount,
            SgstAmount = amounts.SgstAmount,
            IgstAmount = amounts.IgstAmount,
            RoundOff = roundOff,
            GrandTotal = total,
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = ToUtc(day).AddHours(2),
        };
        note.Lines.Add(new CreditNoteLine
        {
            LineNumber = 1,
            ProductId = line.ProductId,
            ProductSku = line.ProductSku,
            ProductName = line.ProductName,
            HsnCode = line.HsnCode,
            Unit = line.Unit,
            Quantity = quantity,
            Disposition = disposition,
            TaxableValue = amounts.TaxableValue,
            GstRate = line.GstRate,
            CgstAmount = amounts.CgstAmount,
            SgstAmount = amounts.SgstAmount,
            IgstAmount = amounts.IgstAmount,
            LineTotal = amounts.LineTotal,
        });
        var demo = new DemoCreditNote(note, demoInvoice, line, ++_sequence);
        CreditNotes.Add(demo);

        if (quantity is { } returned)
        {
            Move(product, StockMovementType.SaleReturn, returned, day, null, null, unitCost: product.AverageCost, creditNote: demo);
            if (disposition == ReturnDisposition.Damaged)
            {
                Move(product, StockMovementType.Adjustment, -returned, day, null, null, reason: AdjustmentReason.Damaged,
                    notes: "Came back damaged: written off", creditNote: demo);
            }
        }

        var off = SettleAgainst(invoice.Payments, p => p.PaymentDate, p => p.Amount, (p, amount) => p.Amount = amount,
            p => { invoice.Payments.Remove(p); PaymentCount--; }, invoice.GrandTotal - invoice.AmountCredited, total, day);
        if (off > 0)
        {
            note.Adjustments.Add(new CreditAdjustment
            {
                Invoice = invoice, AdjustmentDate = day, Amount = off, CreatedByUserId = team.ManagerId, CreatedAtUtc = ToUtc(day).AddHours(2),
            });
            note.AmountAdjusted = off;
            invoice.AmountCredited += off;
            invoice.AmountPaid = invoice.Payments.Sum(p => p.Amount);
            SetStatus(invoice);
        }

        if (total > off)
        {
            // About a third of customers ask for the money back (the first one left with credit always does, so the
            // demo has a refund to show); the rest have it set against their next invoice.
            if (_random.NextDouble() < 0.35 || _refundOn.Count == 0)
            {
                _refundOn[demo] = day.AddDays(_random.Next(5, 15));
            }

            (_customerCredit.TryGetValue(invoice.CustomerId, out var credits) ? credits : _customerCredit[invoice.CustomerId] = []).Add(demo);
        }

        return demo;
    }

    /// <summary>
    /// A quality complaint five days ago about a fully paid invoice from last month. Its credit is still on the
    /// customer's account today, waiting to be used on one of their invoices or refunded: something to try.
    /// </summary>
    private void ComplainLate(DateOnly day)
    {
        // A customer who still owes on a newer invoice, so the credit can be set against it.
        var owing = Invoices.Where(i => i.Entity.Status != InvoiceStatus.Paid).Select(i => i.Entity.CustomerId).ToHashSet();
        var invoice = Invoices.LastOrDefault(i => i.Entity.Status == InvoiceStatus.Paid && i.Entity.AmountCredited == 0 && owing.Contains(i.Entity.CustomerId)
            && i.Entity.InvoiceDate > day.AddDays(-60) && i.Entity.InvoiceDate < day.AddDays(-30)
            && i.Entity.Payments.All(p => p.PaymentDate <= day) && !CreditNotes.Any(c => c.Invoice == i));
        if (invoice is not null && IssueCreditNote(invoice, day) is { } note)
        {
            _refundOn.Remove(note);
            _kept.Add(note);
        }
    }

    /// <summary>
    /// Part of one bill line goes back to the mill (no more than is on the shelf): stock out at the bill's price, and
    /// the value off what we still owe on that bill, or credit for their next one.
    /// </summary>
    private void ReturnToSupplier(DemoBill demoBill, DateOnly day)
    {
        var bill = demoBill.Entity;
        var (orderLine, line) = demoBill.Lines[_random.Next(demoBill.Lines.Count)];
        var product = demoBill.Order.Lines.Single(l => l.Line == orderLine).Product;
        var wanted = RoundTo(line.Quantity * (decimal)(0.05 + _random.NextDouble() * 0.1), product.Spec.Step);
        // Only stock nobody has ordered yet: what's reserved for confirmed orders is about to be invoiced.
        var quantity = Math.Min(Math.Min(wanted, line.Quantity), FloorTo(product.OnHand - product.Reserved, product.Spec.Step));
        if (quantity <= 0 || InStockout(product, day))
        {
            return;
        }

        var creditable = new CreditableLine(line.Quantity, line.TaxableValue, line.CgstAmount, line.SgstAmount, line.IgstAmount, line.GstRate);
        var amounts = ReturnRules.ForGoods(creditable, CreditedSoFar.None, quantity, bill.IsInterState, withGst: true);
        var (roundOff, total) = ReturnRules.Total([amounts]);
        if (total <= 0)
        {
            return;
        }

        // Their credit note has usually arrived for returns more than ten days old.
        var noted = day < generator.Today.AddDays(-10);
        var prefix = bill.SupplierName.Split(' ')[0].ToUpperInvariant()[..3];
        var ret = new SupplierReturn
        {
            Number = string.Empty, // numbered in date order when saving
            FinancialYear = FinancialYear.StartYear(day),
            ReturnDate = day,
            Reason = generator.Pick([(ReturnReason.QualityIssue, 0.5), (ReturnReason.Damaged, 0.3), (ReturnReason.WrongItem, 0.2)], r => r.Item2).Item1,
            ReasonNote = DemoCatalog.SupplierReturnNotes[_random.Next(DemoCatalog.SupplierReturnNotes.Length)],
            WithGst = true,
            SupplierBill = bill,
            SupplierId = bill.SupplierId,
            SupplierName = bill.SupplierName,
            SupplierGstin = bill.SupplierGstin,
            SupplierStateCode = bill.SupplierStateCode,
            IsInterState = bill.IsInterState,
            SupplierCreditNoteNumber = noted
                ? $"{prefix}/CN/{_supplierCreditNoteNumbers[bill.SupplierName] = _supplierCreditNoteNumbers.GetValueOrDefault(bill.SupplierName) + 1:D3}"
                : null,
            SupplierCreditNoteDate = noted ? day.AddDays(_random.Next(2, 9)) : null,
            TaxableValue = amounts.TaxableValue,
            CgstAmount = amounts.CgstAmount,
            SgstAmount = amounts.SgstAmount,
            IgstAmount = amounts.IgstAmount,
            RoundOff = roundOff,
            GrandTotal = total,
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = ToUtc(day).AddHours(4),
        };
        ret.Lines.Add(new SupplierReturnLine
        {
            LineNumber = 1,
            ProductId = line.ProductId,
            ProductSku = line.ProductSku,
            ProductName = line.ProductName,
            HsnCode = line.HsnCode,
            Unit = line.Unit,
            Quantity = quantity,
            UnitPrice = line.UnitPrice,
            TaxableValue = amounts.TaxableValue,
            GstRate = line.GstRate,
            CgstAmount = amounts.CgstAmount,
            SgstAmount = amounts.SgstAmount,
            IgstAmount = amounts.IgstAmount,
            LineTotal = amounts.LineTotal,
        });
        var demo = new DemoSupplierReturn(ret, demoBill, line, ++_sequence);
        SupplierReturns.Add(demo);

        product.AverageCost = PurchaseCosting.AfterReturn(product.AverageCost, product.OnHand, quantity, line.UnitPrice);
        Move(product, StockMovementType.PurchaseReturn, -quantity, day, null, null, unitCost: line.UnitPrice, supplierReturn: demo);

        var off = SettleAgainst(bill.Payments, p => p.PaymentDate, p => p.Amount, (p, amount) => p.Amount = amount,
            p => { bill.Payments.Remove(p); SupplierPaymentCount--; }, bill.GrandTotal - bill.AmountCredited, total, day);
        if (off > 0)
        {
            ret.Adjustments.Add(new SupplierCreditAdjustment
            {
                SupplierBill = bill, AdjustmentDate = day, Amount = off, CreatedByUserId = team.ManagerId, CreatedAtUtc = ToUtc(day).AddHours(4),
            });
            ret.AmountAdjusted = off;
            bill.AmountCredited += off;
            bill.AmountPaid = bill.Payments.Sum(p => p.Amount);
            SetStatus(bill);
        }

        if (total > off)
        {
            (_supplierCredit.TryGetValue(bill.SupplierId, out var credits) ? credits : _supplierCredit[bill.SupplierId] = []).Add(demo);
        }
    }

    /// <summary>
    /// How much of a credit worth <paramref name="total"/> comes off a document on <paramref name="day"/>: what's still
    /// due after the payments made by then. Payments planned for later shrink (or go) by as much as they would have
    /// covered, since the customer or we now pay less.
    /// </summary>
    private static decimal SettleAgainst<TPayment>(List<TPayment> payments, Func<TPayment, DateOnly> date, Func<TPayment, decimal> amount,
        Action<TPayment, decimal> setAmount, Action<TPayment> remove, decimal outstanding, decimal total, DateOnly day)
    {
        var due = outstanding - payments.Where(p => date(p) <= day).Sum(amount);
        var off = Math.Clamp(due, 0m, total);
        var later = payments.Where(p => date(p) > day).OrderByDescending(date).ToList();
        var neverPaid = due - later.Sum(amount);
        var excess = off - Math.Max(0m, neverPaid);
        foreach (var payment in later)
        {
            if (excess <= 0)
            {
                break;
            }

            var value = amount(payment);
            if (value <= excess)
            {
                excess -= value;
                remove(payment);
            }
            else
            {
                setAmount(payment, value - excess);
                excess = 0;
            }
        }

        return off;
    }

    /// <summary>Credit is paid back when the customer asked for it, or once it has gone unused for 45 days.</summary>
    private void RefundUnusedCredit(DateOnly day)
    {
        foreach (var credits in _customerCredit.Values)
        {
            foreach (var credit in credits.Where(c => !_kept.Contains(c) && (_refundOn.TryGetValue(c, out var on) ? on : c.Entity.CreditNoteDate.AddDays(45)) <= day).ToList())
            {
                var amount = Available(credit.Entity);
                var mode = amount >= 50_000m ? PaymentMode.Neft : generator.Pick([(PaymentMode.Neft, 0.6), (PaymentMode.Upi, 0.3), (PaymentMode.Cheque, 0.1)], m => m.Item2).Item1;
                credit.Entity.Refunds.Add(new CreditRefund
                {
                    RefundDate = day,
                    Amount = amount,
                    Mode = mode,
                    Reference = Reference(mode, day),
                    Notes = "Unused credit paid back",
                    CreatedByUserId = team.ManagerId,
                    CreatedAtUtc = ToUtc(day).AddHours(4),
                });
                credit.Entity.AmountRefunded += amount;
                credits.Remove(credit);
                RefundCount++;
            }
        }
    }

    private static decimal Available(CreditNote note) => note.GrandTotal - note.AmountAdjusted - note.AmountRefunded;

    private static decimal Available(SupplierReturn ret) => ret.GrandTotal - ret.AmountAdjusted - ret.AmountRefunded;

    private PaymentMode ChooseMode(decimal amount, int customerId, DateOnly date)
    {
        var mode = amount switch
        {
            < 50_000m => generator.Pick([(PaymentMode.Upi, 0.45), (PaymentMode.Neft, 0.35), (PaymentMode.Cheque, 0.12), (PaymentMode.Cash, 0.08)], m => m.Item2).Item1,
            < 200_000m => generator.Pick([(PaymentMode.Neft, 0.6), (PaymentMode.Upi, 0.15), (PaymentMode.Cheque, 0.25)], m => m.Item2).Item1,
            _ => generator.Pick([(PaymentMode.Rtgs, 0.55), (PaymentMode.Neft, 0.35), (PaymentMode.Cheque, 0.10)], m => m.Item2).Item1,
        };

        if (mode == PaymentMode.Cash)
        {
            // Respect the ₹2 lakh-per-person-per-day cash limit the app itself enforces.
            var key = (customerId, date);
            var already = _cashByDay.GetValueOrDefault(key);
            if (already + amount >= Services.PaymentService.CashLimitPerDay)
            {
                return PaymentMode.Neft;
            }

            _cashByDay[key] = already + amount;
        }

        return mode;
    }

    private string Reference(PaymentMode mode, DateOnly date) => mode switch
    {
        PaymentMode.Upi => $"UPI/{_random.NextInt64(100_000_000_000, 999_999_999_999)}",
        PaymentMode.Neft or PaymentMode.Rtgs => $"UTIB{date:yyMMdd}{_random.Next(100_000, 999_999)}",
        PaymentMode.Cheque => _random.Next(100_000, 999_999).ToString(CultureInfo.InvariantCulture),
        PaymentMode.Cash => $"CR-{_random.Next(1000, 9999)}",
        _ => null!,
    };

    // ---- Stock ledger ------------------------------------------------------------------------------

    private void Move(DemoProduct product, StockMovementType type, decimal quantity, DateOnly day, DemoInvoice? invoice, StockReceipt? receipt,
        decimal? unitCost = null, AdjustmentReason? reason = null, string? notes = null, DemoCreditNote? creditNote = null, DemoSupplierReturn? supplierReturn = null)
    {
        product.OnHand += quantity;
        Movements.Add(new DemoMovement(new StockMovement
        {
            Type = type,
            MovementDate = day,
            Quantity = quantity,
            BalanceAfter = product.OnHand,
            UnitCost = unitCost,
            Reason = reason,
            ReferenceType = invoice is not null ? StockReferenceTypes.Invoice
                : receipt is not null ? StockReferenceTypes.Receipt
                : creditNote is not null ? StockReferenceTypes.CreditNote
                : supplierReturn is not null ? StockReferenceTypes.SupplierReturn
                : null,
            Notes = notes,
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = ToUtc(day),
        }, product, invoice, receipt, creditNote, supplierReturn));
    }

    private static DateOnly Weekday(DateOnly day) => day.DayOfWeek switch
    {
        DayOfWeek.Saturday => day.AddDays(-1),
        DayOfWeek.Sunday => day.AddDays(-2),
        _ => day,
    };

    // ---- Saving ----------------------------------------------------------------------------------------

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var db = generator.Db;

        // Numbers in date order within each financial year, exactly as the live app would have issued them.
        foreach (var fy in Orders.GroupBy(o => FinancialYear.StartYear(o.Entity.OrderDate)))
        {
            var n = 0;
            foreach (var order in fy.OrderBy(o => o.Entity.OrderDate).ThenBy(o => o.Sequence))
            {
                order.Entity.Number = $"SO/{FinancialYear.Label(order.Entity.OrderDate)}/{++n:D5}";
            }
        }

        foreach (var fy in Orders.Where(o => o.QuotedOn is not null).GroupBy(o => FinancialYear.StartYear(o.QuotedOn!.Value)))
        {
            var n = 0;
            foreach (var order in fy.OrderBy(o => o.QuotedOn).ThenBy(o => o.Sequence))
            {
                order.Entity.QuotationNumber = $"{settings.QuotationPrefix}/{FinancialYear.Label(order.QuotedOn!.Value)}/{++n:D5}";
            }
        }

        foreach (var fy in Invoices.GroupBy(i => i.Entity.FinancialYear))
        {
            var n = 0;
            foreach (var invoice in fy.OrderBy(i => i.Entity.InvoiceDate).ThenBy(i => i.Sequence))
            {
                invoice.Entity.Number = $"{settings.InvoicePrefix}/{FinancialYear.Label(invoice.Entity.InvoiceDate)}/{++n:D5}";
            }
        }

        foreach (var fy in PurchaseOrders.GroupBy(o => FinancialYear.StartYear(o.Entity.OrderDate)))
        {
            var n = 0;
            foreach (var order in fy.OrderBy(o => o.Entity.OrderDate).ThenBy(o => o.Sequence))
            {
                order.Entity.Number = $"PO/{FinancialYear.Label(order.Entity.OrderDate)}/{++n:D5}";
            }
        }

        foreach (var fy in Bills.GroupBy(b => b.Entity.FinancialYear))
        {
            var n = 0;
            foreach (var bill in fy.OrderBy(b => b.Entity.BillDate).ThenBy(b => b.Sequence))
            {
                bill.Entity.Number = $"PB/{FinancialYear.Label(bill.Entity.BillDate)}/{++n:D5}";
            }
        }

        foreach (var fy in CreditNotes.GroupBy(c => c.Entity.FinancialYear))
        {
            var n = 0;
            foreach (var note in fy.OrderBy(c => c.Entity.CreditNoteDate).ThenBy(c => c.Sequence))
            {
                note.Entity.Number = $"CN/{FinancialYear.Label(note.Entity.CreditNoteDate)}/{++n:D5}";
            }
        }

        foreach (var fy in SupplierReturns.GroupBy(r => r.Entity.FinancialYear))
        {
            var n = 0;
            foreach (var ret in fy.OrderBy(r => r.Entity.ReturnDate).ThenBy(r => r.Sequence))
            {
                ret.Entity.Number = $"DN/{FinancialYear.Label(ret.Entity.ReturnDate)}/{++n:D5}";
            }
        }

        var receiptNumber = 0;
        foreach (var receipt in Receipts.OrderBy(r => r.Entity.ReceiptDate).ThenBy(r => r.Sequence))
        {
            receipt.Entity.Number = $"GRN-{++receiptNumber:D5}";
        }

        db.SalesOrders.AddRange(Orders.Select(o => o.Entity));
        db.Invoices.AddRange(Invoices.Select(i => i.Entity));
        db.PurchaseOrders.AddRange(PurchaseOrders.Select(o => o.Entity));
        await db.SaveChangesAsync(cancellationToken);

        // Receipts and bills point at the order lines, which have their ids now.
        foreach (var receipt in Receipts)
        {
            receipt.Entity.PurchaseOrderId = receipt.Order.Entity.Id;
            receipt.Entity.SupplierId = receipt.Order.Supplier.Id;
            foreach (var (orderLine, line) in receipt.Lines)
            {
                line.PurchaseOrderLineId = orderLine.Id;
            }
        }

        foreach (var bill in Bills)
        {
            bill.Entity.PurchaseOrderId = bill.Order.Entity.Id;
            foreach (var (orderLine, line) in bill.Lines)
            {
                line.PurchaseOrderLineId = orderLine.Id;
            }
        }

        db.StockReceipts.AddRange(Receipts.Select(r => r.Entity));
        db.SupplierBills.AddRange(Bills.Select(b => b.Entity));
        await db.SaveChangesAsync(cancellationToken);

        // Credit notes and supplier returns point at invoice and bill lines, which have their ids now.
        foreach (var note in CreditNotes)
        {
            note.Entity.Lines[0].InvoiceLineId = note.Line.Id;
        }

        foreach (var ret in SupplierReturns)
        {
            ret.Entity.Lines[0].SupplierBillLineId = ret.Line.Id;
        }

        db.CreditNotes.AddRange(CreditNotes.Select(c => c.Entity));
        db.SupplierReturns.AddRange(SupplierReturns.Select(r => r.Entity));
        await db.SaveChangesAsync(cancellationToken);

        foreach (var movement in Movements)
        {
            movement.Entity.ProductId = movement.Product.Entity.Id;
            movement.Entity.ReferenceId = movement.Invoice?.Entity.Id ?? movement.Receipt?.Id ?? movement.CreditNote?.Entity.Id ?? movement.SupplierReturn?.Entity.Id;
            movement.Entity.ReferenceNumber = movement.Invoice?.Entity.Number ?? movement.Receipt?.Number
                ?? movement.CreditNote?.Entity.Number ?? movement.SupplierReturn?.Entity.Number;
            if (movement.CreditNote is { } note && movement.Entity.Type == StockMovementType.SaleReturn)
            {
                movement.Entity.Notes = $"Returned against {note.Invoice.Entity.Number}";
            }
            else if (movement.SupplierReturn is { } ret)
            {
                movement.Entity.Notes = $"Returned to {ret.Entity.SupplierName} against {ret.Bill.Entity.Number}";
            }
        }

        db.StockMovements.AddRange(Movements.Select(m => m.Entity));
        await db.SaveChangesAsync(cancellationToken);

        foreach (var product in products)
        {
            var last = Movements.Where(m => m.Product == product).Max(m => (DateOnly?)m.Entity.MovementDate);
            DateTime? lastUtc = last is { } d ? ToUtc(d) : null;
            await db.StockLevels.Where(s => s.ProductId == product.Entity.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.QuantityOnHand, product.OnHand).SetProperty(x => x.LastMovementAtUtc, lastUtc), cancellationToken);
            await db.Products.Where(p => p.Id == product.Entity.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CostPrice, product.AverageCost), cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE o SET o.[InvoiceId] = i.[Id] FROM [dbo].[SalesOrders] o JOIN [dbo].[Invoices] i ON i.[SalesOrderId] = o.[Id] WHERE i.[Status] <> 'Cancelled'",
            cancellationToken);

        // Carry the counters on, so the next document created in the app continues each series.
        void AddSequences(string type, IEnumerable<int> financialYears) =>
            db.DocumentSequences.AddRange(financialYears.GroupBy(y => y).Select(g => new DocumentSequence { DocumentType = type, FinancialYear = g.Key, NextNumber = g.Count() + 1 }));

        AddSequences(DocumentTypes.SalesOrder, Orders.Select(o => FinancialYear.StartYear(o.Entity.OrderDate)));
        AddSequences(DocumentTypes.Quotation, Orders.Where(o => o.QuotedOn is not null).Select(o => FinancialYear.StartYear(o.QuotedOn!.Value)));
        AddSequences(DocumentTypes.Invoice, Invoices.Select(i => i.Entity.FinancialYear));
        AddSequences(DocumentTypes.PurchaseOrder, PurchaseOrders.Select(o => FinancialYear.StartYear(o.Entity.OrderDate)));
        AddSequences(DocumentTypes.SupplierBill, Bills.Select(b => b.Entity.FinancialYear));
        AddSequences(DocumentTypes.CreditNote, CreditNotes.Select(c => c.Entity.FinancialYear));
        AddSequences(DocumentTypes.DebitNote, SupplierReturns.Select(r => r.Entity.FinancialYear));
        await db.SaveChangesAsync(cancellationToken);

#pragma warning disable EF1002 // Integers formatted by us, not user input.
        await db.Database.ExecuteSqlRawAsync(string.Create(CultureInfo.InvariantCulture,
            $"ALTER SEQUENCE [dbo].[{CustomerConfiguration.CodeSequence}] RESTART WITH {customers.Count + 1}"), cancellationToken);
        await db.Database.ExecuteSqlRawAsync(string.Create(CultureInfo.InvariantCulture,
            $"ALTER SEQUENCE [dbo].[{StockReceiptConfiguration.NumberSequence}] RESTART WITH {Receipts.Count + 1}"), cancellationToken);
        await db.Database.ExecuteSqlRawAsync(string.Create(CultureInfo.InvariantCulture,
            $"ALTER SEQUENCE [dbo].[{SupplierConfiguration.CodeSequence}] RESTART WITH {suppliers.Count + 1}"), cancellationToken);
#pragma warning restore EF1002
    }
}
