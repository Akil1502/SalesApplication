using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Auditing;

/// <summary>
/// Records every change to an audited record (see <see cref="AuditedTypes"/>) with the old and new value of each
/// field. The entries are written by the same save, inside the same transaction (one is started if the caller
/// didn't), so a change and its audit entry are committed or rolled back together.
/// </summary>
public sealed class AuditInterceptor(IAuditActor actor, TimeProvider time) : SaveChangesInterceptor
{
    private static readonly AsyncLocal<int> SuppressedDepth = new();
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private List<PendingAudit>? _pending;
    private IDbContextTransaction? _ownTransaction;
    private bool _writing;

    /// <summary>
    /// Turns auditing off for the current flow until disposed: for bulk set-up work such as seeding demo data, where
    /// thousands of entries would bury the real ones.
    /// </summary>
    public static IDisposable Suppress()
    {
        SuppressedDepth.Value++;
        return new Restore();
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (Collecting(eventData.Context) is { } db)
        {
            _pending = AuditCollector.CollectAsync(db, CancellationToken.None).GetAwaiter().GetResult();
            if (_pending.Count > 0 && db.Database.CurrentTransaction is null)
            {
                _ownTransaction = db.Database.BeginTransaction();
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Collecting(eventData.Context) is { } db)
        {
            _pending = await AuditCollector.CollectAsync(db, cancellationToken);
            if (_pending.Count > 0 && db.Database.CurrentTransaction is null)
            {
                _ownTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
            }
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is AppDbContext db && TakePending() is { } pending)
        {
            AddEntries(db, pending);
            _writing = true;
            try
            {
                db.SaveChanges();
            }
            finally
            {
                _writing = false;
            }

            _ownTransaction?.Commit();
            ReleaseTransaction();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db && TakePending() is { } pending)
        {
            // The change itself is saved; its audit entries are written and committed even if the caller gives up now.
            AddEntries(db, pending);
            _writing = true;
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            finally
            {
                _writing = false;
            }

            if (_ownTransaction is not null)
            {
                await _ownTransaction.CommitAsync(CancellationToken.None);
            }

            ReleaseTransaction();
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _pending = null;
        _ownTransaction?.Rollback();
        ReleaseTransaction();
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = null;
        if (_ownTransaction is not null)
        {
            await _ownTransaction.RollbackAsync(cancellationToken);
        }

        ReleaseTransaction();
    }

    private AppDbContext? Collecting(DbContext? context) =>
        context is AppDbContext db && !_writing && SuppressedDepth.Value == 0 ? db : null;

    private List<PendingAudit>? TakePending()
    {
        var pending = _pending;
        _pending = null;
        return pending is { Count: > 0 } ? pending : null;
    }

    /// <summary>Adds the entries, now that new records have their keys. The save that writes them isn't audited itself.</summary>
    private void AddEntries(AppDbContext db, List<PendingAudit> pending)
    {
        var now = time.GetUtcNow().UtcDateTime;
        db.AuditEntries.AddRange(pending.Select(p => new AuditEntry
        {
            OccurredAtUtc = now,
            UserId = actor.UserId,
            UserName = actor.Name,
            IpAddress = actor.IpAddress,
            Action = p.Action,
            EntityType = p.EntityType,
            EntityId = p.Key(),
            EntityLabel = p.Label,
            ChangesJson = p.Changes.Count > 0 ? JsonSerializer.Serialize(p.Changes, Json) : null,
        }));
    }

    private void ReleaseTransaction()
    {
        _ownTransaction?.Dispose();
        _ownTransaction = null;
    }

    private sealed class Restore : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                SuppressedDepth.Value--;
            }
        }
    }
}

/// <summary>An entry waiting for its save to finish: a new record's key is only known afterwards.</summary>
internal sealed record PendingAudit(AuditAction Action, string EntityType, string? Label, Func<string?> Key, List<AuditChange> Changes);

/// <summary>Which records are audited, what they're called, and how each is labelled in the trail.</summary>
internal static class AuditedTypes
{
    /// <param name="Name">The record type as the trail shows it.</param>
    /// <param name="Label">How the record is known, from its values and the looked-up names.</param>
    /// <param name="CreatedFields">For records copied from others (invoices), the fields worth listing when created.</param>
    /// <param name="IncludeOwned">Whether owned parts (a customer's addresses) count as the record's own fields.</param>
    internal sealed record Spec(string Name, Func<EntityEntry, AuditLookups, string?> Label, string[]? CreatedFields = null, bool IncludeOwned = false);

    internal static readonly Dictionary<Type, Spec> Specs = new()
    {
        [typeof(Customer)] = new("Customer", (e, _) => Value<string>(e, nameof(Customer.Name)), IncludeOwned: true),
        [typeof(Product)] = new("Product", (e, _) => $"{Value<string>(e, nameof(Product.Sku))} · {Value<string>(e, nameof(Product.Name))}"),
        [typeof(ProductRate)] = new("Rate", (e, l) =>
            $"{l.Name(nameof(ProductRate.ProductId), Value<int>(e, nameof(ProductRate.ProductId)))} from {AuditFormat.Value(Value<DateOnly>(e, nameof(ProductRate.EffectiveFrom)), nameof(ProductRate.EffectiveFrom))}"),
        [typeof(Category)] = new("Category", (e, _) => Value<string>(e, nameof(Category.Name))),
        [typeof(HsnCode)] = new("HSN code", (e, _) => Value<string>(e, nameof(HsnCode.Code))),
        [typeof(CompanySettings)] = new("Company settings", (e, _) => Value<string>(e, nameof(CompanySettings.LegalName))),
        [typeof(SalesOrder)] = new("Order", (e, _) => Value<string>(e, nameof(SalesOrder.Number))),
        [typeof(Invoice)] = new("Invoice", (e, _) => Value<string>(e, nameof(Invoice.Number)),
            CreatedFields: [nameof(Invoice.InvoiceDate), nameof(Invoice.DueDate), nameof(Invoice.CustomerId), nameof(Invoice.TaxableValue), nameof(Invoice.GrandTotal), nameof(Invoice.Status)]),
        [typeof(Payment)] = new("Payment", (e, l) =>
            $"{AuditFormat.Money(Value<decimal>(e, nameof(Payment.Amount)))} for {l.Name(nameof(Payment.InvoiceId), Value<int>(e, nameof(Payment.InvoiceId)))}"),
        [typeof(StockReceipt)] = new("Goods receipt", (e, _) => Value<string>(e, nameof(StockReceipt.Number))),
        [typeof(StockMovement)] = new("Stock adjustment", (e, l) => l.Name(nameof(StockMovement.ProductId), Value<int>(e, nameof(StockMovement.ProductId)))),
        [typeof(AppUser)] = new("User", (e, _) => $"{Value<string>(e, nameof(AppUser.FullName))} ({Value<string>(e, nameof(AppUser.Email))})"),
        [typeof(AppRole)] = new("Role", (e, _) => Value<string>(e, nameof(IdentityRole<Guid>.Name))),
    };

    /// <summary>Never listed: keys, timestamps, concurrency tokens, credentials and Identity's internal fields.</summary>
    internal static readonly HashSet<string> Ignored =
    [
        "Id", "RowVersion", "CreatedAtUtc", "UpdatedAtUtc", "CreatedByUserId", "UpdatedByUserId", "ConcurrencyStamp",
        "SecurityStamp", "PasswordHash", "NormalizedEmail", "NormalizedUserName", "NormalizedName", "UserName",
        "AccessFailedCount", "LockoutEnd", "LockoutEnabled", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
        "FinancialYear", "CgstAmount", "SgstAmount", "IgstAmount", "RoundOff", "BalanceAfter",
    ];

    /// <summary>Stock movements are audited only when someone made them by hand; sales and receipts are covered by their documents.</summary>
    internal static bool Audited(EntityEntry entry) =>
        entry.Entity is not StockMovement movement || movement.Type is StockMovementType.Opening or StockMovementType.Adjustment;

    private static T Value<T>(EntityEntry entry, string property)
    {
        var p = entry.Property(property);
        return (T)(entry.State == EntityState.Deleted ? p.OriginalValue : p.CurrentValue)!;
    }
}
