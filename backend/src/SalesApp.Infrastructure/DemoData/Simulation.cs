using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Purchasing;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data.Configurations;
using SalesApp.Infrastructure.Services;
using static SalesApp.Infrastructure.DemoData.DemoDataGenerator;

namespace SalesApp.Infrastructure.DemoData;

/// <summary>
/// Day-by-day trading simulation behind <see cref="DemoDataGenerator"/>. Each day: weekly purchase orders and their
/// deliveries (with the supplier's bill and, on their terms, our payment), invoices due that day (goods leave stock),
/// then new orders (stock is reserved so it's never oversold). Everything is built in memory and saved in a few bulk
/// steps at the end.
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

    internal sealed record DemoMovement(StockMovement Entity, DemoProduct Product, DemoInvoice? Invoice, StockReceipt? Receipt);

    internal sealed class DemoPurchaseOrder(PurchaseOrder entity, Supplier supplier, int sequence)
    {
        public PurchaseOrder Entity { get; } = entity;
        public Supplier Supplier { get; } = supplier;
        public int Sequence { get; } = sequence;
        public List<(DemoProduct Product, PurchaseOrderLine Line)> Lines { get; } = [];
    }

    internal sealed record DemoReceipt(StockReceipt Entity, DemoPurchaseOrder Order, int Sequence, List<(PurchaseOrderLine OrderLine, StockReceiptLine Line)> Lines);

    internal sealed record DemoBill(SupplierBill Entity, DemoPurchaseOrder Order, int Sequence, List<(PurchaseOrderLine OrderLine, SupplierBillLine Line)> Lines);

    private readonly Random _random = generator.Random;
    private readonly Dictionary<DateOnly, List<DemoOrder>> _toInvoice = [];
    private readonly Dictionary<(int CustomerId, DateOnly Day), decimal> _cashByDay = [];
    private readonly Dictionary<DateOnly, List<(DemoPurchaseOrder Order, Dictionary<PurchaseOrderLine, decimal> Quantities)>> _deliveries = [];
    private readonly Dictionary<string, int> _supplierInvoiceNumbers = [];
    private Dictionary<string, (string Name, bool UsesUtgst)> _states = [];
    private int _sequence;

    public List<DemoOrder> Orders { get; } = [];
    public List<DemoInvoice> Invoices { get; } = [];
    public List<DemoPurchaseOrder> PurchaseOrders { get; } = [];
    public List<DemoReceipt> Receipts { get; } = [];
    public List<DemoBill> Bills { get; } = [];
    public List<DemoMovement> Movements { get; } = [];
    public int PaymentCount { get; private set; }
    public int SupplierPaymentCount { get; private set; }

    // Anomalies, placed relative to today so they always fall inside a long enough history.
    private DateOnly BulkOrderDay => Weekday(generator.Today.AddDays(-150));
    private bool InStockout(DemoProduct p, DateOnly day) =>
        (p.Spec.Sku == "CY-60C" && day > generator.Today.AddDays(-130) && day < generator.Today.AddDays(-88)) ||
        (p.Spec.Sku == "FB-DNM" && day > generator.Today.AddDays(-320) && day < generator.Today.AddDays(-275)) ||
        // Supplier delays still running today, long enough to empty the shelf whatever stock they started from.
        (DelayedNow.ContainsKey(p.Spec.Sku) && day > generator.Today.AddDays(-45)) ||
        // Not reordered for a few weeks, so they've dropped below their reorder level with no order placed:
        // what the reorder suggestions are for.
        (NotReorderedYet.Contains(p.Spec.Sku) && day > generator.Today.AddDays(-24));

    private static readonly HashSet<string> NotReorderedYet = ["FB-CAM", "PK-CON"];

    /// <summary>
    /// Products whose supplier is late as of today, with the days of demand a part delivery covered three days ago
    /// (zero: nothing arrived). The demo therefore opens with two products below reorder level and one out of stock.
    /// </summary>
    private static readonly Dictionary<string, double> DelayedNow = new() { ["PC-40"] = 5, ["HT-TWL"] = 6, ["GT-VST"] = 0 };
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
                foreach (var (order, quantities) in balances)
                {
                    Deliver(order, day, quantities, "Balance of the order");
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

            if (day == generator.Today.AddDays(-3))
            {
                PlaceLateOrders(day);
            }

            InvoiceDueOrders(day);

            var orderedToday = products.ToDictionary(p => p, _ => 0m);
            if (day == BulkOrderDay)
            {
                PlaceBulkOrder(day, orderedToday);
            }

            var count = generator.Poisson(generator.ExpectedOrders(day));
            for (var i = 0; i < count; i++)
            {
                PlaceOrder(day, orderedToday);
            }

            // Orders invoiced the same day they were placed.
            InvoiceDueOrders(day);

            // Smoothed demand (about a 4-week average) drives how much stock the weekly receipts top up.
            foreach (var (product, quantity) in orderedToday)
            {
                product.AverageDailyDemand = product.AverageDailyDemand * (27.0 / 28) + (double)quantity / 28;
            }
        }

        PlaceOpenOrders();
    }

    // ---- Stock in -------------------------------------------------------------------------------

    private void ReceiveWeeklyStock(DateOnly day)
    {
        var bySupplier = new Dictionary<string, List<(DemoProduct Product, decimal Quantity)>>();
        foreach (var product in products)
        {
            if (InStockout(product, day))
            {
                continue;
            }

            var demand = Math.Max(product.AverageDailyDemand, 0.1);
            if ((double)product.Available >= demand * 21)
            {
                continue;
            }

            var target = Math.Max((decimal)(demand * 35), product.Entity.ReorderQuantity);
            var quantity = RoundTo(target - product.Available, product.Spec.Step);
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
    /// three days ago or not delivered at all. They're late, which is what the assistant can chase.
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
                var quantity = Math.Min(ordered, RoundTo((decimal)(demand * DelayedNow[product.Spec.Sku]), product.Spec.Step));
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

        var split = notes is null && _random.NextDouble() < 0.12;
        var first = order.Lines.ToDictionary(
            l => l.Line,
            l => split ? Math.Min(l.Line.Quantity, RoundTo(l.Line.Quantity * 0.6m, l.Product.Spec.Step)) : l.Line.Quantity);
        Deliver(order, day, first, notes);

        var rest = order.Lines.Where(l => l.Line.Quantity > first[l.Line]).ToDictionary(l => l.Line, l => l.Line.Quantity - first[l.Line]);
        if (rest.Count > 0)
        {
            var later = day.AddDays(_random.Next(3, 7));
            (_deliveries.TryGetValue(later, out var due) ? due : _deliveries[later] = []).Add((order, rest));
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

        foreach (var (product, line) in order.Lines.Where(l => quantities.ContainsKey(l.Line)))
        {
            var quantity = quantities[line];
            product.AverageCost = PurchaseCosting.AfterReceipt(product.AverageCost, product.OnHand, quantity, line.UnitPrice);
            var receiptLine = new StockReceiptLine { ProductId = product.Entity.Id, Quantity = quantity, UnitCost = line.UnitPrice };
            receipt.Lines.Add(receiptLine);
            demo.Lines.Add((line, receiptLine));
            Move(product, StockMovementType.Receipt, quantity, day, null, receipt, unitCost: line.UnitPrice);
            line.QuantityReceived += quantity;
        }

        order.Entity.Status = order.Entity.Lines.All(l => l.QuantityReceived >= l.Quantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartlyReceived;
        Receipts.Add(demo);

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
        PaySupplier(bill);
    }

    /// <summary>
    /// We pay most bills around their due date, some a few weeks late, and lately not Karur Home Textile Makers at
    /// all (a dispute), so payables have something overdue. A bill waiting for approval isn't paid.
    /// </summary>
    private void PaySupplier(SupplierBill bill)
    {
        if (bill.MatchStatus == BillMatchStatus.NeedsApproval)
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
            Amount = bill.GrandTotal,
            Mode = mode,
            Reference = Reference(mode, payOn),
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = ToUtc(payOn).AddHours(6),
        });
        bill.AmountPaid = bill.GrandTotal;
        bill.Status = SupplierBillStatus.Paid;
        SupplierPaymentCount++;
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
            }

            AddPayments(invoice, customer);
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

        var remaining = invoice.GrandTotal;
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

            var amount = i == schedule.Count - 1 ? remaining : Math.Round(invoice.GrandTotal * share, 0);
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

        invoice.AmountPaid = invoice.GrandTotal - remaining;
        invoice.Status = remaining == 0 ? InvoiceStatus.Paid : invoice.AmountPaid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Unpaid;
    }

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
        decimal? unitCost = null, AdjustmentReason? reason = null, string? notes = null)
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
            ReferenceType = invoice is not null ? StockReferenceTypes.Invoice : receipt is not null ? StockReferenceTypes.Receipt : null,
            Notes = notes,
            CreatedByUserId = team.ManagerId,
            CreatedAtUtc = ToUtc(day),
        }, product, invoice, receipt));
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

        foreach (var movement in Movements)
        {
            movement.Entity.ProductId = movement.Product.Entity.Id;
            movement.Entity.ReferenceId = movement.Invoice?.Entity.Id ?? movement.Receipt?.Id;
            movement.Entity.ReferenceNumber = movement.Invoice?.Entity.Number ?? movement.Receipt?.Number;
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
