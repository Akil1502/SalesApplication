using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Entities;
using SalesApp.Core.Validation;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Data.Configurations;

namespace SalesApp.Infrastructure.Services;

public interface ISupplierService
{
    Task<PagedResult<SupplierListItemDto>> ListAsync(SupplierListRequest request, CancellationToken cancellationToken);
    Task<SupplierDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<SupplierDto> CreateAsync(SupplierSaveRequest request, CancellationToken cancellationToken);
    Task<SupplierDto> UpdateAsync(int id, SupplierSaveRequest request, CancellationToken cancellationToken);
}

/// <summary>Suppliers, with what we owe each of them and how many orders are still open.</summary>
public sealed class SupplierService(AppDbContext db, ILookupService lookups, IBusinessClock clock) : ISupplierService
{
    // A constant (not an interpolated string) so it's obviously free of user input.
    private const string NextCodeSql = "SELECT NEXT VALUE FOR [dbo].[" + SupplierConfiguration.CodeSequence + "] AS [Value]";

    private static readonly SortMap<Supplier> Sorts = new SortMap<Supplier>()
        .Add("code", s => s.Code)
        .Add("name", s => s.Name)
        .Add("city", s => s.Address.City)
        .Add("updatedAt", s => s.UpdatedAtUtc);

    internal static readonly SupplierBillStatus[] OpenBills = [SupplierBillStatus.Unpaid, SupplierBillStatus.PartiallyPaid];
    internal static readonly PurchaseOrderStatus[] OpenOrders = [PurchaseOrderStatus.PendingApproval, PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartlyReceived];

    public async Task<PagedResult<SupplierListItemDto>> ListAsync(SupplierListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Suppliers.AsNoTracking();

        if (!request.IncludeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        if (request.StateCode is { } stateCode)
        {
            query = query.Where(s => s.Address.StateCode == stateCode);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(s =>
                s.Name.Contains(q) ||
                s.Code.Contains(q) ||
                (s.TradeName != null && s.TradeName.Contains(q)) ||
                (s.Gstin != null && s.Gstin.Contains(q)) ||
                (s.Phone != null && s.Phone.Contains(q)) ||
                s.Address.City.Contains(q));
        }

        var today = clock.Today;
        var page = await Sorts.Apply(query, request.Sort, "name")
            .ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id, s.Code, s.Name, s.Gstin, s.Address.City, s.Address.StateCode, s.Phone, s.IsActive,
                Payable = db.SupplierBills.Where(b => b.SupplierId == s.Id && OpenBills.Contains(b.Status)).Sum(b => (decimal?)(b.GrandTotal - b.AmountPaid)) ?? 0m,
                Overdue = db.SupplierBills.Where(b => b.SupplierId == s.Id && OpenBills.Contains(b.Status) && b.DueDate < today).Sum(b => (decimal?)(b.GrandTotal - b.AmountPaid)) ?? 0m,
                Orders = db.PurchaseOrders.Count(o => o.SupplierId == s.Id && OpenOrders.Contains(o.Status)),
            })
            .ToPagedResultAsync(request, cancellationToken);

        var states = await lookups.GetStatesAsync(cancellationToken);
        var items = page.Items
            .Select(s => new SupplierListItemDto(s.Id, s.Code, s.Name, s.Gstin, s.City, states.GetValueOrDefault(s.StateCode)?.Name ?? s.StateCode,
                s.Phone, s.Payable, s.Overdue, s.Orders, s.IsActive))
            .ToList();
        return new PagedResult<SupplierListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<SupplierDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Supplier {id} was not found.");

        var today = clock.Today;
        var open = db.SupplierBills.AsNoTracking().Where(b => b.SupplierId == id && OpenBills.Contains(b.Status));
        var payable = await open.SumAsync(b => (decimal?)(b.GrandTotal - b.AmountPaid), cancellationToken) ?? 0m;
        var overdue = await open.Where(b => b.DueDate < today).SumAsync(b => (decimal?)(b.GrandTotal - b.AmountPaid), cancellationToken) ?? 0m;
        var orders = await db.PurchaseOrders.CountAsync(o => o.SupplierId == id && OpenOrders.Contains(o.Status), cancellationToken);
        var companyState = await db.CompanySettings.AsNoTracking().Select(s => s.StateCode).SingleAsync(cancellationToken);
        var states = await lookups.GetStatesAsync(cancellationToken);

        var a = supplier.Address;
        return new SupplierDto(
            supplier.Id, supplier.Code, supplier.Name, supplier.TradeName, supplier.Gstin, supplier.Gstin is null ? null : Gstin.Pan(supplier.Gstin),
            supplier.ContactPerson, supplier.Phone, supplier.Email,
            new AddressDto(a.Line1, a.Line2, a.City, a.StateCode, states.GetValueOrDefault(a.StateCode)?.Name ?? a.StateCode, a.Pincode),
            supplier.PaymentTermsDays, supplier.BankName, supplier.BankAccountNumber, supplier.BankIfsc, supplier.Notes, supplier.IsActive,
            a.StateCode != companyState, payable, overdue, orders,
            supplier.CreatedAtUtc, supplier.UpdatedAtUtc, RowVersions.ToToken(supplier.RowVersion));
    }

    public async Task<SupplierDto> CreateAsync(SupplierSaveRequest request, CancellationToken cancellationToken)
    {
        var (address, gstin) = await ValidateAsync(request, cancellationToken);

        var code = Text.NullIfBlank(request.Code)?.ToUpperInvariant() ?? await NextCodeAsync(cancellationToken);
        if (await db.Suppliers.AnyAsync(s => s.Code == code, cancellationToken))
        {
            throw new ValidationException("code", $"Supplier code {code} is already used.");
        }

        var supplier = new Supplier { Code = code, Name = request.Name.Trim(), Address = address };
        Apply(supplier, request, address, gstin);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(supplier.Id, cancellationToken);
    }

    public async Task<SupplierDto> UpdateAsync(int id, SupplierSaveRequest request, CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Supplier {id} was not found.");
        db.Entry(supplier).Property(s => s.RowVersion).OriginalValue = RowVersions.Parse(request.RowVersion);

        var (address, gstin) = await ValidateAsync(request, cancellationToken);
        Apply(supplier, request, address, gstin);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private static void Apply(Supplier supplier, SupplierSaveRequest request, Address address, string? gstin)
    {
        supplier.Name = request.Name.Trim();
        supplier.TradeName = Text.NullIfBlank(request.TradeName);
        supplier.Gstin = gstin;
        supplier.ContactPerson = Text.NullIfBlank(request.ContactPerson);
        supplier.Phone = Text.NormalizePhone(request.Phone);
        supplier.Email = Text.NullIfBlank(request.Email);
        supplier.Address = address;
        supplier.PaymentTermsDays = request.PaymentTermsDays;
        supplier.BankName = Text.NullIfBlank(request.BankName);
        supplier.BankAccountNumber = Text.NullIfBlank(request.BankAccountNumber);
        supplier.BankIfsc = Text.NullIfBlank(request.BankIfsc);
        supplier.Notes = Text.NullIfBlank(request.Notes);
        supplier.IsActive = request.IsActive;
    }

    /// <summary>A real state, and a GSTIN registered in the same state as the address.</summary>
    private async Task<(Address Address, string? Gstin)> ValidateAsync(SupplierSaveRequest request, CancellationToken cancellationToken)
    {
        var states = await lookups.GetStatesAsync(cancellationToken);
        var errors = new Dictionary<string, string[]>();

        if (!states.TryGetValue(request.Address.StateCode, out var addressState))
        {
            errors["address.stateCode"] = ["Choose a valid state."];
        }

        string? gstin = null;
        if (Text.NullIfBlank(request.Gstin) is { } rawGstin)
        {
            gstin = Gstin.Normalize(rawGstin);
            var gstinState = Gstin.StateCode(gstin);
            if (!states.TryGetValue(gstinState, out var registeredIn))
            {
                errors["gstin"] = [$"GSTIN starts with {gstinState}, which isn't a valid GST state code."];
            }
            else if (addressState is not null && gstinState != addressState.Code)
            {
                errors["gstin"] = [$"This GSTIN is registered in {registeredIn.Name}, but the address is in {addressState.Name}. Use the GST-registered address."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some fields need attention.", errors);
        }

        var input = request.Address;
        return (new Address
        {
            Line1 = input.Line1.Trim(),
            Line2 = Text.NullIfBlank(input.Line2),
            City = input.City.Trim(),
            StateCode = input.StateCode,
            Pincode = input.Pincode,
        }, gstin);
    }

    /// <summary>Next generated code, e.g. S00042. Sequence values are never reused, so codes may have gaps.</summary>
    private async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        // ToListAsync, not SingleAsync: composing would wrap NEXT VALUE FOR in a subquery, which SQL Server forbids.
        var next = (await db.Database.SqlQueryRaw<int>(NextCodeSql).ToListAsync(cancellationToken)).Single();
        return $"S{next:D5}";
    }
}
