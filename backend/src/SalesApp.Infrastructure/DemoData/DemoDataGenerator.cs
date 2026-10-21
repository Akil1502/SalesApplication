using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Core.Validation;
using SalesApp.Infrastructure.Auditing;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Data.Configurations;
using SalesApp.Infrastructure.Services;

namespace SalesApp.Infrastructure.DemoData;

public sealed record DemoDataOptions(int Months = 24, int Seed = 20261013, bool Reset = false);

public sealed record DemoDataSummary(
    DateOnly From,
    DateOnly To,
    int Customers,
    int Products,
    int Orders,
    int Invoices,
    int Payments,
    int StockReceipts,
    int StockMovements,
    decimal TaxableSales,
    TimeSpan Elapsed,
    int Suppliers = 0,
    int PurchaseOrders = 0,
    int SupplierBills = 0,
    int SupplierPayments = 0,
    int CreditNotes = 0,
    int Refunds = 0,
    int SupplierReturns = 0,
    int Rejections = 0);

/// <summary>
/// Generates realistic, fully consistent demo history: a day-by-day simulation of a textile trading
/// business, so stock never goes negative, ledger balances add up, invoice numbers are gapless per
/// financial year, and payments follow each customer's habits. Built-in patterns for the AI phases to find:
/// festive Oct-Nov peak, year-on-year growth, customers who churn or join, late payers and defaulters, a
/// cotton price hike, a one-off bulk order, a demand surge and two stockouts. Stock comes in through purchase orders,
/// goods receipts and supplier bills (some with price differences or freight), paid on each supplier's terms, with a
/// few late deliveries still open today. Deterministic for a seed and date.
/// </summary>
public sealed class DemoDataGenerator(
    AppDbContext db,
    UserManager<AppUser> users,
    IBusinessClock clock,
    IConfiguration configuration,
    ILogger<DemoDataGenerator> logger)
{
    private const decimal MonthlyInflation = 0.004m;
    private const double AnnualGrowth = 0.18;
    private const double BaseOrdersPerDay = 5.2;

    private Random _random = new(1);
    private DateOnly _today;
    private DateOnly _start;

    public async Task<DemoDataSummary> GenerateAsync(DemoDataOptions options, CancellationToken cancellationToken)
    {
        // Two years of made-up history would bury the audit trail; the generator isn't someone changing records.
        using var quiet = AuditInterceptor.Suppress();
        if (options.Months is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Months must be between 1 and 60.");
        }

        var stopwatch = Stopwatch.StartNew();
        _random = new Random(options.Seed);
        _today = clock.Today;
        _start = new DateOnly(_today.Year, _today.Month, 1).AddMonths(-(options.Months - 1));

        var hasData = await db.Invoices.AnyAsync(cancellationToken) || await db.SalesOrders.AnyAsync(cancellationToken)
            || await db.Products.AnyAsync(cancellationToken) || await db.Customers.AnyAsync(cancellationToken);
        if (hasData && !options.Reset)
        {
            throw new InvalidOperationException("The database already has customers, products or sales. Run with --reset to replace them with demo data.");
        }

        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (hasData)
            {
                await ResetAsync(cancellationToken);
            }

            var settings = await db.CompanySettings.AsNoTracking().SingleAsync(cancellationToken);
            var team = await EnsureTeamAsync(cancellationToken);
            var products = await CreateProductsAsync(cancellationToken);
            var suppliers = await CreateSuppliersAsync(products, cancellationToken);
            var customers = await CreateCustomersAsync(team, settings.StateCode, cancellationToken);

            var simulation = new Simulation(this, products, customers, suppliers, team, settings);
            simulation.Run();
            await simulation.SaveAsync(cancellationToken);
            await DemoNotifications.CreateAsync(db, clock, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var summary = new DemoDataSummary(_start, _today, customers.Count, products.Count, simulation.Orders.Count,
                simulation.Invoices.Count, simulation.PaymentCount, simulation.Receipts.Count, simulation.Movements.Count,
                simulation.Invoices.Sum(i => i.Entity.TaxableValue) - simulation.CreditNotes.Sum(c => c.Entity.TaxableValue), stopwatch.Elapsed,
                suppliers.Count, simulation.PurchaseOrders.Count, simulation.Bills.Count, simulation.SupplierPaymentCount,
                simulation.CreditNotes.Count, simulation.RefundCount, simulation.SupplierReturns.Count, simulation.RejectionCount);
            logger.LogInformation("Demo data generated: {Summary}", summary);
            return summary;
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Removes customers, suppliers, products and all sales, purchase and stock history (and the assistant's drafts,
    /// which point at customers and suppliers). Users, settings, states, categories and HSN codes stay.
    /// </summary>
    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning("Resetting customers, suppliers, products, orders, invoices, purchases, payments and stock history");
        foreach (var sql in new[]
        {
            "UPDATE [dbo].[SalesOrders] SET [InvoiceId] = NULL",
            "DELETE FROM [dbo].[Notifications]",
            "DELETE FROM [dbo].[CreditRefunds]",
            "DELETE FROM [dbo].[CreditAdjustments]",
            "DELETE FROM [dbo].[CreditNoteLines]",
            "DELETE FROM [dbo].[CreditNotes]",
            "DELETE FROM [dbo].[SupplierRefunds]",
            "DELETE FROM [dbo].[SupplierCreditAdjustments]",
            "DELETE FROM [dbo].[SupplierReturnLines]",
            "DELETE FROM [dbo].[SupplierReturns]",
            "DELETE FROM [dbo].[Payments]",
            "DELETE FROM [dbo].[InvoiceLines]",
            "DELETE FROM [dbo].[Invoices]",
            "DELETE FROM [dbo].[SalesOrderLines]",
            "DELETE FROM [dbo].[SalesOrders]",
            "DELETE FROM [dbo].[SupplierPayments]",
            "DELETE FROM [dbo].[SupplierBillLines]",
            "DELETE FROM [dbo].[SupplierBills]",
            "DELETE FROM [dbo].[StockMovements]",
            "DELETE FROM [dbo].[StockReceiptLines]",
            "DELETE FROM [dbo].[StockReceipts]",
            "DELETE FROM [dbo].[PurchaseOrderLines]",
            "DELETE FROM [dbo].[PurchaseOrders]",
            "DELETE FROM [dbo].[StockLevels]",
            "DELETE FROM [dbo].[ProductRates]",
            "DELETE FROM [dbo].[ProductAliases]",
            "DELETE FROM [dbo].[Products]",
            "DELETE FROM [dbo].[AiDrafts]",
            "DELETE FROM [dbo].[Customers]",
            "DELETE FROM [dbo].[Suppliers]",
            "DELETE FROM [dbo].[DocumentSequences]",
        })
        {
#pragma warning disable EF1002 // Constant SQL.
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
#pragma warning restore EF1002
        }
    }

    // ---- Masters ---------------------------------------------------------------------------

    internal sealed record Team(Guid AdminId, Guid ManagerId, IReadOnlyList<Guid> RepIds);

    /// <summary>A sales manager and three reps, so dashboards and the assistant have people to talk about.</summary>
    private async Task<Team> EnsureTeamAsync(CancellationToken cancellationToken)
    {
        var admin = (await users.GetUsersInRoleAsync(Roles.Admin)).FirstOrDefault()
            ?? throw new InvalidOperationException("An admin user is needed before generating demo data.");
        var password = configuration["Seed:DemoUserPassword"];

        async Task<Guid> Ensure(string fullName, string email, string role)
        {
            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                return existing.Id;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return admin.Id;
            }

            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = fullName, CreatedAtUtc = DateTime.UtcNow };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Couldn't create demo user {email}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
            }

            await users.AddToRoleAsync(user, role);
            return user.Id;
        }

        var manager = await Ensure("Meena Krishnan", "meena.krishnan@salesapp.local", Roles.SalesManager);
        List<Guid> reps =
        [
            await Ensure("Arun Kumar", "arun.kumar@salesapp.local", Roles.SalesRep),
            await Ensure("Divya Rajan", "divya.rajan@salesapp.local", Roles.SalesRep),
            await Ensure("Farhan Sheikh", "farhan.sheikh@salesapp.local", Roles.SalesRep),
        ];
        cancellationToken.ThrowIfCancellationRequested();
        return new Team(admin.Id, manager, reps);
    }

    internal sealed class DemoProduct(Product entity, ProductSpec spec, decimal gstRate, string hsnCode)
    {
        public Product Entity { get; } = entity;
        public ProductSpec Spec { get; } = spec;
        public decimal GstRate { get; } = gstRate;
        public string HsnCode { get; } = hsnCode;
        public decimal OnHand { get; set; }
        public decimal Reserved { get; set; }
        public double AverageDailyDemand { get; set; }
        public decimal Available => OnHand - Reserved;

        /// <summary>What's been invoiced so far, for its real rate of sale (the estimate above is rough).</summary>
        public decimal Sold { get; set; }

        /// <summary>Weighted average cost, kept the way the app keeps it as goods come in and bills arrive.</summary>
        public decimal? AverageCost { get; set; }
    }

    private async Task<List<DemoProduct>> CreateProductsAsync(CancellationToken cancellationToken)
    {
        var categories = await db.Categories.ToDictionaryAsync(c => c.Name, cancellationToken);
        foreach (var name in DemoCatalog.Products.Select(p => p.Category).Distinct().Where(n => !categories.ContainsKey(n)))
        {
            categories[name] = db.Categories.Add(new Category { Name = name }).Entity;
        }

        var hsnCodes = await db.HsnCodes.ToDictionaryAsync(h => h.Code, cancellationToken);
        foreach (var (code, description, rate) in DemoCatalog.ExtraHsnCodes.Where(h => !hsnCodes.ContainsKey(h.Code)))
        {
            hsnCodes[code] = db.HsnCodes.Add(new HsnCode { Code = code, Description = description, GstRate = rate }).Entity;
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(cancellationToken);

        var totalWeight = DemoCatalog.Products.Sum(p => p.Weight);
        var created = new List<DemoProduct>();
        foreach (var spec in DemoCatalog.Products)
        {
            // Rough daily demand from the product's share of ~6 order lines a day at a mid-range quantity.
            var dailyDemand = spec.Weight / totalWeight * 6.5 * (double)((spec.QtyMin + spec.QtyMax) / 2);
            var product = new Product
            {
                Sku = spec.Sku,
                Name = spec.Name,
                CategoryId = categories[spec.Category].Id,
                HsnCodeId = hsnCodes[spec.Hsn].Id,
                Unit = spec.Unit,
                ListPrice = PriceAt(spec, _today),
                CostPrice = Math.Round(PriceAt(spec, _today) * spec.CostRatio, 2),
                ReorderLevel = RoundTo((decimal)(dailyDemand * 14), spec.Step),
                ReorderQuantity = RoundTo((decimal)(dailyDemand * 30), spec.Step),
                LeadTimeDays = spec.Category is DemoCatalog.CottonYarn or DemoCatalog.BlendedYarn ? 7 : 14,
                Stock = new StockLevel(),
                CreatedAtUtc = ToUtc(_start.AddDays(-30)),
            };
            db.Products.Add(product);
            created.Add(new DemoProduct(product, spec, hsnCodes[spec.Hsn].GstRate, spec.Hsn)
            {
                AverageDailyDemand = dailyDemand,
                AverageCost = Math.Round(PriceAt(spec, _start) * spec.CostRatio, 2), // what the opening stock cost
            });
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    /// <summary>The suppliers, and each product's preferred supplier (who the simulation buys it from).</summary>
    private async Task<Dictionary<string, Supplier>> CreateSuppliersAsync(List<DemoProduct> products, CancellationToken cancellationToken)
    {
        var suppliers = new Dictionary<string, Supplier>(StringComparer.Ordinal);
        var number = 1;
        foreach (var spec in DemoCatalog.Suppliers)
        {
            var supplier = new Supplier
            {
                Code = $"S{number++:D5}",
                Name = spec.Name,
                Gstin = FakeGstin(spec.StateCode, spec.Name),
                ContactPerson = spec.ContactPerson,
                Phone = $"+91{_random.Next(7, 10)}{_random.Next(100_000_000, 999_999_999)}",
                Email = $"sales@{new string(spec.Name.ToLowerInvariant().Where(char.IsLetter).Take(14).ToArray())}.example",
                Address = new Address
                {
                    Line1 = $"{_random.Next(1, 300)}, {Pick(["Mill Road", "SIDCO Industrial Estate", "Avinashi Road", "Bypass Road", "Ring Road"], _ => 1)}",
                    City = spec.City,
                    StateCode = spec.StateCode,
                    Pincode = spec.Pincode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                PaymentTermsDays = spec.PaymentTermsDays,
                BankName = Pick(["State Bank of India", "Indian Bank", "HDFC Bank", "Karur Vysya Bank"], _ => 1),
                BankAccountNumber = $"{_random.Next(10_000_000, 99_999_999)}{_random.Next(1000, 9999)}",
                BankIfsc = Pick(["SBIN0001234", "IDIB000C123", "HDFC0004321", "KVBL0001122"], _ => 1),
                CreatedAtUtc = ToUtc(_start.AddDays(-45)),
            };
            supplier.UpdatedAtUtc = supplier.CreatedAtUtc;
            db.Suppliers.Add(supplier);
            suppliers[spec.Name] = supplier;
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(cancellationToken);

        foreach (var product in products)
        {
            product.Entity.PreferredSupplierId = suppliers[Simulation.SupplierFor(product)].Id;
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(cancellationToken);
        return suppliers;
    }

    internal enum PaymentHabit
    {
        Prompt,
        Normal,
        Slow,
        Partial,
        Defaulter,
    }

    internal sealed class DemoCustomer(Customer entity, Segment segment, double size, PaymentHabit habit, decimal discount)
    {
        public Customer Entity { get; } = entity;
        public Segment Segment { get; } = segment;
        public double Size { get; } = size;
        public PaymentHabit Habit { get; } = habit;
        public decimal Discount { get; } = discount;
        public DateOnly ActiveFrom { get; set; }
        public DateOnly? ActiveUntil { get; set; }
        public List<ProductSpec> Favourites { get; } = [];
        public bool IsInterState { get; set; }
        public decimal CashToday { get; set; }
        public bool IsActiveOn(DateOnly day) => day >= ActiveFrom && (ActiveUntil is null || day <= ActiveUntil);
    }

    private async Task<List<DemoCustomer>> CreateCustomersAsync(Team team, string companyState, CancellationToken cancellationToken)
    {
        const int count = 56;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customers = new List<DemoCustomer>();

        for (var i = 0; i < count; i++)
        {
            var city = Pick(DemoCatalog.Cities, c => c.Weight);
            var segment = city.Segments[_random.Next(city.Segments.Length)];
            string name;
            do
            {
                var cores = DemoCatalog.NameCores[segment];
                name = $"{DemoCatalog.NamePrefixes[_random.Next(DemoCatalog.NamePrefixes.Length)]} {cores[_random.Next(cores.Length)]} {DemoCatalog.NameSuffixes[_random.Next(DemoCatalog.NameSuffixes.Length)]}".Trim();
            }
            while (!names.Add(name));

            // A few large accounts and a long tail of smaller ones.
            var size = Math.Round(Math.Exp(Normal(0, 0.6)), 2);
            var habit = Pick([(PaymentHabit.Prompt, 0.45), (PaymentHabit.Normal, 0.30), (PaymentHabit.Slow, 0.15), (PaymentHabit.Partial, 0.07), (PaymentHabit.Defaulter, 0.03)], h => h.Item2).Item1;
            var discount = Pick([(0m, 0.5), (2m, 0.25), (3m, 0.15), (5m, 0.10)], d => d.Item2).Item1;
            var terms = Pick([(30, 0.5), (45, 0.3), (60, 0.2)], t => t.Item2).Item1;
            var registered = segment != Segment.Retailer || _random.NextDouble() < 0.6;
            var isTamilNadu = city.StateCode == "33";

            var entity = new Customer
            {
                Code = string.Empty, // assigned once activity dates are known
                Name = name,
                Gstin = registered ? FakeGstin(city.StateCode, name) : null,
                ContactPerson = DemoCatalog.ContactNames[_random.Next(DemoCatalog.ContactNames.Length)],
                Phone = $"+91{_random.Next(7, 10)}{_random.Next(100_000_000, 999_999_999)}",
                Email = $"purchase@{new string(name.ToLowerInvariant().Where(char.IsLetter).Take(14).ToArray())}.example",
                BillingAddress = new Address
                {
                    Line1 = $"{_random.Next(1, 220)}, {Pick(["Market Road", "Mill Street", "Industrial Estate", "Main Road", "Bazaar Street", "Ring Road"], _ => 1)}",
                    City = city.City,
                    StateCode = city.StateCode,
                    Pincode = (city.PincodeBase + _random.Next(0, 9)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                PaymentTermsDays = terms,
                CreditLimit = _random.NextDouble() < 0.2 ? null : RoundTo((decimal)(size * 1_500_000), 100_000m),
                AssignedRepId = isTamilNadu ? team.RepIds[_random.Next(0, 2)] : team.RepIds[2],
            };

            var customer = new DemoCustomer(entity, segment, size, habit, discount)
            {
                ActiveFrom = _start,
                IsInterState = city.StateCode != companyState,
            };

            var candidates = DemoCatalog.Products.Where(p => p.BoughtBy.Contains(segment)).ToList();
            foreach (var favourite in candidates.OrderBy(_ => _random.Next()).Take(_random.Next(3, Math.Min(7, candidates.Count + 1))))
            {
                customer.Favourites.Add(favourite);
            }

            customers.Add(customer);
        }

        // Growth from new accounts, and a few customers who stop buying (churn).
        var monthsSpan = (_today.DayNumber - _start.DayNumber) / 30;
        foreach (var newcomer in customers.OrderBy(_ => _random.Next()).Take(10))
        {
            newcomer.ActiveFrom = _start.AddDays(_random.Next(Math.Min(120, monthsSpan * 15), Math.Max(121, _today.DayNumber - _start.DayNumber - 60)));
        }

        foreach (var churner in customers.Where(c => c.ActiveFrom == _start).OrderBy(_ => _random.Next()).Take(4))
        {
            var earliest = _start.AddDays(180);
            var latest = _today.AddDays(-120);
            churner.ActiveUntil = latest > earliest ? earliest.AddDays(_random.Next(0, latest.DayNumber - earliest.DayNumber)) : null;
        }

        // The manager looks after the five largest accounts.
        foreach (var key in customers.OrderByDescending(c => c.Size).Take(5))
        {
            key.Entity.AssignedRepId = team.ManagerId;
        }

        var number = 1;
        foreach (var customer in customers.OrderBy(c => c.ActiveFrom).ThenBy(c => c.Entity.Name, StringComparer.Ordinal))
        {
            customer.Entity.Code = $"C{number++:D5}";
            customer.Entity.CreatedAtUtc = ToUtc(customer.ActiveFrom.AddDays(-_random.Next(1, 20)));
            db.Customers.Add(customer.Entity);
        }

        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync(cancellationToken);
        return customers;
    }

    // ---- Helpers shared with the simulation ---------------------------------------------------

    internal Random Random => _random;
    internal DateOnly Today => _today;
    internal DateOnly Start => _start;
    internal AppDbContext Db => db;

    /// <summary>List price on a given day: gentle monthly inflation, plus a cotton price hike in the past year.</summary>
    internal decimal PriceAt(ProductSpec spec, DateOnly day)
    {
        var months = (day.Year - _start.Year) * 12 + day.Month - _start.Month;
        var price = spec.Price * (decimal)Math.Pow(1 + (double)MonthlyInflation, months - MonthsUntilToday());
        if (spec.Category == DemoCatalog.CottonYarn && day < _today.AddDays(-340))
        {
            price /= 1.08m; // before the hike, cotton yarn was 8% cheaper
        }

        return Math.Round(price * 2, MidpointRounding.AwayFromZero) / 2; // to the nearest 50 paise
    }

    private int MonthsUntilToday() => (_today.Year - _start.Year) * 12 + _today.Month - _start.Month;

    internal double ExpectedOrders(DateOnly day)
    {
        var growth = Math.Pow(1 + AnnualGrowth, (day.DayNumber - _today.DayNumber) / 365.0);
        var weekday = day.DayOfWeek switch { DayOfWeek.Sunday => 0.12, DayOfWeek.Saturday => 0.8, _ => 1.0 };
        return BaseOrdersPerDay * DemoCatalog.Seasonality[day.Month - 1] * growth * weekday;
    }

    internal int Poisson(double lambda)
    {
        var limit = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do
        {
            k++;
            p *= _random.NextDouble();
        }
        while (p > limit);
        return k - 1;
    }

    internal double Normal(double mean, double stdDev)
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    internal T Pick<T>(IReadOnlyList<T> items, Func<T, double> weight)
    {
        var total = items.Sum(weight);
        var roll = _random.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0)
            {
                return item;
            }
        }

        return items[^1];
    }

    internal static decimal RoundTo(decimal value, decimal step) =>
        step <= 0 ? value : Math.Max(step, Math.Round(value / step, MidpointRounding.AwayFromZero) * step);

    internal static decimal FloorTo(decimal value, decimal step) => step <= 0 ? value : Math.Floor(value / step) * step;

    /// <summary>Mid-morning India time on the given date, as UTC.</summary>
    internal static DateTime ToUtc(DateOnly day) => day.ToDateTime(new TimeOnly(5, 30), DateTimeKind.Utc);

    /// <summary>A made-up but checksum-valid GSTIN for the state (random PAN-shaped middle).</summary>
    private string FakeGstin(string stateCode, string name)
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var type = name.Contains("Pvt Ltd", StringComparison.Ordinal) ? 'C' : name.Contains("LLP", StringComparison.Ordinal) ? 'F' : Pick(['F', 'P', 'C'], _ => 1);
        var initial = char.ToUpperInvariant(name.Split(' ')[^1][0]);
        if (!char.IsAsciiLetterUpper(initial))
        {
            initial = letters[_random.Next(26)];
        }

        var pan = $"{letters[_random.Next(26)]}{letters[_random.Next(26)]}{letters[_random.Next(26)]}{type}{initial}{_random.Next(1000, 10000)}{letters[_random.Next(26)]}";
        var first14 = $"{stateCode}{pan}1Z";
        return first14 + Gstin.ComputeCheckCharacter(first14);
    }

    internal static string SellerAddress(CompanySettings s, string stateName) =>
        string.Join(", ", new[] { s.AddressLine1, s.AddressLine2, $"{s.City} - {s.Pincode}", stateName }.Where(p => !string.IsNullOrWhiteSpace(p)));

    internal static (decimal Taxable, decimal Tax, decimal Total) OrderTotals(IEnumerable<SalesOrderLine> lines, bool isInterState)
    {
        var totals = GstCalculator.Calculate(lines.Select(OrderService.ToGstInput), isInterState);
        return (totals.TaxableValue, totals.TaxAmount, totals.GrandTotal);
    }
}
