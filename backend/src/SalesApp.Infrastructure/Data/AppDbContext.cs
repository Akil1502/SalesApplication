using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data.Configurations;

namespace SalesApp.Infrastructure.Data;

/// <summary>
/// The single EF Core context for the sales database. Identity tables keep their standard
/// AspNet* names so any Identity tooling or documentation applies unchanged.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<State> States => Set<State>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<HsnCode> HsnCodes => Set<HsnCode>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockReceipt> StockReceipts => Set<StockReceipt>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<ProductAlias> ProductAliases => Set<ProductAlias>();
    public DbSet<AiQueryLog> AiQueryLogs => Set<AiQueryLog>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiDraft> AiDrafts => Set<AiDraft>();
    public DbSet<InsightRun> InsightRuns => Set<InsightRun>();
    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();
    public DbSet<UserPermissionChange> UserPermissionChanges => Set<UserPermissionChange>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.FullName).HasMaxLength(120).IsRequired();
        });

        builder.HasSequence<int>(CustomerConfiguration.CodeSequence).StartsAt(1).IncrementsBy(1);
        builder.HasSequence<int>(StockReceiptConfiguration.NumberSequence).StartsAt(1).IncrementsBy(1);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Keyless read models over the rpt.* views (not part of migrations; the views come from Sql/*.sql).
        Reporting.ReportingViewMappings.Map(builder);
    }

    public IQueryable<Reporting.RptSalesLine> RptSalesLines => Set<Reporting.RptSalesLine>().AsNoTracking();
    public IQueryable<Reporting.RptInvoice> RptInvoices => Set<Reporting.RptInvoice>().AsNoTracking();
    public IQueryable<Reporting.RptProduct> RptProducts => Set<Reporting.RptProduct>().AsNoTracking();
    public IQueryable<Reporting.RptPayment> RptPayments => Set<Reporting.RptPayment>().AsNoTracking();
    public IQueryable<Reporting.RptMargin> RptMargins => Set<Reporting.RptMargin>().AsNoTracking();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is decimal(18,2) unless a property says otherwise (quantities use decimal(18,3)).
        // Setting it globally means no decimal column silently falls back to EF's default precision.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Timestamps are UTC to the second; sub-second precision adds nothing for audit columns.
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2(0)");
    }
}
