using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SalesApp.Core.Contracts.Audit;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Auditing;

/// <summary>
/// Names for the ids a record points at (a customer's sales rep, an order's customer), looked up once per save so
/// the trail says "Divya Rajan" rather than a GUID.
/// </summary>
internal sealed class AuditLookups(Dictionary<(string Property, string Id), string> names)
{
    /// <summary>The properties whose values are ids of other records, and so get a name.</summary>
    internal static readonly HashSet<string> References =
    [
        "AssignedRepId", "ApprovedByUserId", "CategoryId", "HsnCodeId", "CustomerId", "ProductId", "InvoiceId", "SalesOrderId", "StateCode",
        "PlaceOfSupplyStateCode", "SupplierStateCode", "SupplierId", "PreferredSupplierId", "PurchaseOrderId", "SupplierBillId",
        "CreditNoteId", "SupplierReturnId",
    ];

    public string? Name(string property, object? id) =>
        id is null ? null : names.TryGetValue((Kind(property), Key(id)), out var name) ? name : Key(id);

    internal static string Kind(string property) => property switch
    {
        _ when property.EndsWith("StateCode", StringComparison.Ordinal) => "StateCode",
        "ApprovedByUserId" => "AssignedRepId",
        "PreferredSupplierId" => "SupplierId",
        _ => property,
    };

    internal static string Key(object id) => Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public static async Task<AuditLookups> LoadAsync(AppDbContext db, IEnumerable<(string Property, object Id)> wanted, CancellationToken cancellationToken)
    {
        var ids = wanted.GroupBy(w => Kind(w.Property)).ToDictionary(g => g.Key, g => g.Select(w => w.Id).Distinct().ToList());
        var names = new Dictionary<(string, string), string>();

        void Add<TKey>(string kind, IEnumerable<(TKey Id, string Name)> rows)
        {
            foreach (var (id, name) in rows)
            {
                names[(kind, Key(id!))] = name;
            }
        }

        List<T> Of<T>(string kind) => ids.TryGetValue(kind, out var list) ? [.. list.OfType<T>()] : [];

        if (Of<Guid>("AssignedRepId") is { Count: > 0 } reps)
        {
            Add("AssignedRepId", (await db.Users.AsNoTracking().Where(u => reps.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToListAsync(cancellationToken)).Select(u => (u.Id, u.FullName)));
        }

        if (Of<int>("CategoryId") is { Count: > 0 } categories)
        {
            Add("CategoryId", (await db.Categories.AsNoTracking().Where(c => categories.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken)).Select(c => (c.Id, c.Name)));
        }

        if (Of<int>("HsnCodeId") is { Count: > 0 } hsn)
        {
            Add("HsnCodeId", (await db.HsnCodes.AsNoTracking().Where(h => hsn.Contains(h.Id)).Select(h => new { h.Id, h.Code }).ToListAsync(cancellationToken)).Select(h => (h.Id, h.Code)));
        }

        if (Of<int>("CustomerId") is { Count: > 0 } customers)
        {
            Add("CustomerId", (await db.Customers.AsNoTracking().Where(c => customers.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken)).Select(c => (c.Id, c.Name)));
        }

        if (Of<int>("ProductId") is { Count: > 0 } products)
        {
            Add("ProductId", (await db.Products.AsNoTracking().Where(p => products.Contains(p.Id)).Select(p => new { p.Id, p.Sku }).ToListAsync(cancellationToken)).Select(p => (p.Id, p.Sku)));
        }

        if (Of<int>("InvoiceId") is { Count: > 0 } invoices)
        {
            Add("InvoiceId", (await db.Invoices.AsNoTracking().Where(i => invoices.Contains(i.Id)).Select(i => new { i.Id, i.Number }).ToListAsync(cancellationToken)).Select(i => (i.Id, i.Number)));
        }

        if (Of<int>("SalesOrderId") is { Count: > 0 } orders)
        {
            Add("SalesOrderId", (await db.SalesOrders.AsNoTracking().Where(o => orders.Contains(o.Id)).Select(o => new { o.Id, o.Number }).ToListAsync(cancellationToken)).Select(o => (o.Id, o.Number)));
        }

        if (Of<int>("SupplierId") is { Count: > 0 } suppliers)
        {
            Add("SupplierId", (await db.Suppliers.AsNoTracking().Where(s => suppliers.Contains(s.Id)).Select(s => new { s.Id, s.Name }).ToListAsync(cancellationToken)).Select(s => (s.Id, s.Name)));
        }

        if (Of<int>("PurchaseOrderId") is { Count: > 0 } purchaseOrders)
        {
            Add("PurchaseOrderId", (await db.PurchaseOrders.AsNoTracking().Where(o => purchaseOrders.Contains(o.Id)).Select(o => new { o.Id, o.Number }).ToListAsync(cancellationToken)).Select(o => (o.Id, o.Number)));
        }

        if (Of<int>("SupplierBillId") is { Count: > 0 } bills)
        {
            Add("SupplierBillId", (await db.SupplierBills.AsNoTracking().Where(b => bills.Contains(b.Id)).Select(b => new { b.Id, b.Number }).ToListAsync(cancellationToken)).Select(b => (b.Id, b.Number)));
        }

        if (Of<int>("CreditNoteId") is { Count: > 0 } creditNotes)
        {
            Add("CreditNoteId", (await db.CreditNotes.AsNoTracking().Where(c => creditNotes.Contains(c.Id)).Select(c => new { c.Id, c.Number }).ToListAsync(cancellationToken)).Select(c => (c.Id, c.Number)));
        }

        if (Of<int>("SupplierReturnId") is { Count: > 0 } returns)
        {
            Add("SupplierReturnId", (await db.SupplierReturns.AsNoTracking().Where(r => returns.Contains(r.Id)).Select(r => new { r.Id, r.Number }).ToListAsync(cancellationToken)).Select(r => (r.Id, r.Number)));
        }

        if (Of<string>("StateCode") is { Count: > 0 } states)
        {
            Add("StateCode", (await db.States.AsNoTracking().Where(s => states.Contains(s.Code)).Select(s => new { s.Code, s.Name }).ToListAsync(cancellationToken)).Select(s => (s.Code, s.Name)));
        }

        return new AuditLookups(names);
    }
}

/// <summary>Works out, before a save, which audited records it changes and how: the entries are written after it.</summary>
internal static class AuditCollector
{
    public static async Task<List<PendingAudit>> CollectAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        db.ChangeTracker.DetectChanges();
        var changed = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && e.Entity is not AuditEntry)
            .ToList();
        if (changed.Count == 0)
        {
            return [];
        }

        // The audited records this save touches: changed ones, owners of changed parts (a customer whose address
        // changed), and orders (sales or purchase) whose lines changed.
        var records = new List<EntityEntry>();
        void Include(EntityEntry? entry)
        {
            // EF hands out a new EntityEntry wrapper per lookup, so entries are compared by the record they wrap.
            if (entry is not null && !records.Any(r => ReferenceEquals(r.Entity, entry.Entity))
                && AuditedTypes.Specs.ContainsKey(entry.Metadata.ClrType) && AuditedTypes.Audited(entry))
            {
                records.Add(entry);
            }
        }

        var owned = changed.Where(e => e.Metadata.IsOwned()).ToList();
        var lines = changed.Where(e => LineParents.ContainsKey(e.Metadata.ClrType)).ToList();
        foreach (var entry in changed.Where(e => !e.Metadata.IsOwned()))
        {
            Include(entry);
        }

        foreach (var part in owned)
        {
            var (owner, _) = OwnerOf(db, part);
            if (owner is not null && AuditedTypes.Specs.TryGetValue(owner.Metadata.ClrType, out var spec) && spec.IncludeOwned)
            {
                Include(owner);
            }
        }

        foreach (var line in lines)
        {
            Include(ParentOf(db, line));
        }

        if (records.Count == 0)
        {
            return [];
        }

        var lookups = await AuditLookups.LoadAsync(db, References(records.Concat(owned).Concat(lines)), cancellationToken);
        var pending = new List<PendingAudit>();
        foreach (var record in records)
        {
            var spec = AuditedTypes.Specs[record.Metadata.ClrType];
            var action = record.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Deleted => AuditAction.Deleted,
                _ => AuditAction.Updated,
            };

            var changes = FieldChanges(record, action, spec, lookups);
            if (spec.IncludeOwned)
            {
                changes.AddRange(OwnedChanges(db, record, owned, lookups));
            }

            if (record.Entity is SalesOrder or PurchaseOrder && LineChange(db, record, lines, lookups) is { } lineChange)
            {
                changes.Add(lineChange);
            }

            if (action == AuditAction.Updated && changes.Count == 0)
            {
                continue;
            }

            var keyNow = action == AuditAction.Created ? null : KeyOf(record);
            pending.Add(new PendingAudit(action, spec.Name, spec.Label(record, lookups), () => keyNow ?? KeyOf(record), changes));
        }

        return pending;
    }

    private static List<AuditChange> FieldChanges(EntityEntry entry, AuditAction action, AuditedTypes.Spec spec, AuditLookups lookups)
    {
        var changes = new List<AuditChange>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (AuditedTypes.Ignored.Contains(name) || property.Metadata.IsShadowProperty() || property.Metadata.ClrType == typeof(byte[]))
            {
                continue;
            }

            if (action == AuditAction.Created && spec.CreatedFields is { } fields && !fields.Contains(name))
            {
                continue;
            }

            var old = action == AuditAction.Created ? null : Display(name, property.OriginalValue, lookups);
            var current = action == AuditAction.Deleted ? null : Display(name, property.CurrentValue, lookups);
            if (action == AuditAction.Updated && !property.IsModified)
            {
                continue;
            }

            if (old != current)
            {
                changes.Add(new AuditChange(AuditFormat.Label(name), old, current));
            }
        }

        return changes;
    }

    /// <summary>Changes to owned parts, such as a customer's billing address, compared value by value.</summary>
    private static IEnumerable<AuditChange> OwnedChanges(AppDbContext db, EntityEntry owner, List<EntityEntry> owned, AuditLookups lookups)
    {
        foreach (var group in owned.Select(part => (Part: part, Owner: OwnerOf(db, part)))
                     .Where(x => x.Owner.Entry is { } o && ReferenceEquals(o.Entity, owner.Entity))
                     .GroupBy(x => x.Owner.Navigation))
        {
            var before = new Dictionary<string, string?>();
            var after = new Dictionary<string, string?>();
            foreach (var (part, _) in group)
            {
                foreach (var property in part.Properties.Where(p => !p.Metadata.IsShadowProperty() && !p.Metadata.IsPrimaryKey()))
                {
                    var name = property.Metadata.Name;
                    if (part.State is EntityState.Deleted or EntityState.Modified)
                    {
                        before[name] = Display(name, property.OriginalValue, lookups);
                    }

                    if (part.State is EntityState.Added or EntityState.Modified)
                    {
                        after[name] = Display(name, property.CurrentValue, lookups);
                    }
                }
            }

            foreach (var name in before.Keys.Union(after.Keys))
            {
                var old = before.GetValueOrDefault(name);
                var current = after.GetValueOrDefault(name);
                if (old != current)
                {
                    yield return new AuditChange(AuditFormat.Label(name, group.Key), old, current);
                }
            }
        }
    }

    /// <summary>
    /// An order's lines before and after, as one change ("CY-30C × 500 @ ₹300; …"). Only product, quantity, price and
    /// discount count, so a purchase order line that just records goods received or billed isn't a change to the order.
    /// </summary>
    private static AuditChange? LineChange(AppDbContext db, EntityEntry order, List<EntityEntry> lines, AuditLookups lookups)
    {
        var mine = lines.Where(l => ParentOf(db, l) is { } o && ReferenceEquals(o.Entity, order.Entity)).ToList();
        if (mine.Count == 0)
        {
            return null;
        }

        string? Summary(IEnumerable<(int Number, int Product, decimal Quantity, decimal Price, decimal Discount)> rows)
        {
            var text = string.Join("; ", rows.OrderBy(r => r.Number).Select(r =>
                $"{lookups.Name("ProductId", r.Product)} × {AuditFormat.Value(r.Quantity, "Quantity")} @ {AuditFormat.Money(r.Price)}" +
                (r.Discount > 0 ? $", {AuditFormat.Value(r.Discount, "DiscountPercent")} off" : string.Empty)));
            return text.Length == 0 ? null : text;
        }

        (int, int, decimal, decimal, decimal) Row(EntityEntry line, bool original)
        {
            object? Read(string name) => original ? line.Property(name).OriginalValue : line.Property(name).CurrentValue;
            var discount = line.Metadata.FindProperty(nameof(SalesOrderLine.DiscountPercent)) is null ? 0m : (decimal)Read(nameof(SalesOrderLine.DiscountPercent))!;
            return ((int)Read(nameof(SalesOrderLine.LineNumber))!, (int)Read(nameof(SalesOrderLine.ProductId))!,
                (decimal)Read(nameof(SalesOrderLine.Quantity))!, (decimal)Read(nameof(SalesOrderLine.UnitPrice))!, discount);
        }

        var old = Summary(mine.Where(l => l.State is EntityState.Deleted or EntityState.Modified).Select(l => Row(l, original: true)));
        var current = Summary(mine.Where(l => l.State is EntityState.Added or EntityState.Modified).Select(l => Row(l, original: false)));
        return old == current ? null : new AuditChange("Lines", old, current);
    }

    private static string? Display(string property, object? value, AuditLookups lookups) =>
        AuditLookups.References.Contains(property) ? lookups.Name(property, value) : AuditFormat.Value(value, property);

    private static IEnumerable<(string Property, object Id)> References(IEnumerable<EntityEntry> entries)
    {
        foreach (var entry in entries)
        {
            foreach (var property in entry.Properties.Where(p => AuditLookups.References.Contains(p.Metadata.Name)))
            {
                if (property.CurrentValue is { } current)
                {
                    yield return (property.Metadata.Name, current);
                }

                if (entry.State != EntityState.Added && property.OriginalValue is { } original)
                {
                    yield return (property.Metadata.Name, original);
                }
            }
        }
    }

    /// <summary>The record an owned part belongs to (matched by key, which an owned part shares with its owner), and the navigation it hangs off.</summary>
    private static (EntityEntry? Entry, string? Navigation) OwnerOf(AppDbContext db, EntityEntry part)
    {
        var ownership = part.Metadata.FindOwnership();
        if (ownership is null)
        {
            return (null, null);
        }

        var partKey = ownership.Properties.Select(p => part.State == EntityState.Deleted ? part.Property(p.Name).OriginalValue : part.Property(p.Name).CurrentValue).ToList();
        var ownerKey = ownership.PrincipalKey.Properties;
        var owner = db.ChangeTracker.Entries().FirstOrDefault(e => e.Metadata == ownership.PrincipalEntityType
            && ownerKey.Select(k => e.Property(k.Name).CurrentValue).SequenceEqual(partKey));
        return (owner, ownership.PrincipalToDependent?.Name);
    }

    /// <summary>Line types whose changes are recorded on their document, with the document type and the line's key to it.</summary>
    private static readonly Dictionary<Type, (Type Parent, string ForeignKey)> LineParents = new()
    {
        [typeof(SalesOrderLine)] = (typeof(SalesOrder), nameof(SalesOrderLine.SalesOrderId)),
        [typeof(PurchaseOrderLine)] = (typeof(PurchaseOrder), nameof(PurchaseOrderLine.PurchaseOrderId)),
    };

    private static EntityEntry? ParentOf(AppDbContext db, EntityEntry line)
    {
        var (parent, foreignKey) = LineParents[line.Metadata.ClrType];
        var parentId = line.State == EntityState.Deleted ? line.Property(foreignKey).OriginalValue : line.Property(foreignKey).CurrentValue;
        return db.ChangeTracker.Entries().FirstOrDefault(e => e.Metadata.ClrType == parent && Equals(e.Property("Id").CurrentValue, parentId));
    }

    private static string? KeyOf(EntityEntry entry) =>
        entry.Metadata.FindPrimaryKey() is { } key
            ? string.Join(",", key.Properties.Select(p => AuditLookups.Key(entry.Property(p.Name).CurrentValue ?? string.Empty)))
            : null;
}
