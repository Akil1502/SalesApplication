using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Validation;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Data.Configurations;

namespace SalesApp.Infrastructure.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> ListAsync(CustomerListRequest request, CancellationToken cancellationToken);
    Task<CustomerDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<CustomerDto> CreateAsync(CustomerSaveRequest request, CancellationToken cancellationToken);
    Task<CustomerDto> UpdateAsync(int id, CustomerSaveRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Customers. Credit limit, payment terms and the assigned rep are commercial decisions, so only
/// users who may set credit terms can set them; anyone else's new customers are assigned to them.
/// </summary>
public sealed class CustomerService(
    AppDbContext db,
    ILookupService lookups,
    ICurrentUser currentUser) : ICustomerService
{
    private const int DefaultPaymentTermsDays = 30;

    private static readonly SortMap<Customer> Sorts = new SortMap<Customer>()
        .Add("code", c => c.Code)
        .Add("name", c => c.Name)
        .Add("city", c => c.BillingAddress.City)
        .Add("updatedAt", c => c.UpdatedAtUtc);

    public async Task<PagedResult<CustomerListItemDto>> ListAsync(CustomerListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking();

        if (!request.IncludeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (request.AssignedRepId is { } repId)
        {
            query = query.Where(c => c.AssignedRepId == repId);
        }

        if (request.StateCode is { } stateCode)
        {
            query = query.Where(c => c.BillingAddress.StateCode == stateCode);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(c =>
                c.Name.Contains(q) ||
                c.Code.Contains(q) ||
                (c.TradeName != null && c.TradeName.Contains(q)) ||
                (c.Gstin != null && c.Gstin.Contains(q)) ||
                (c.Phone != null && c.Phone.Contains(q)) ||
                c.BillingAddress.City.Contains(q));
        }

        var page = await Sorts.Apply(query, request.Sort, "name")
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id, c.Code, c.Name, c.Gstin, c.BillingAddress.City, c.BillingAddress.StateCode, c.Phone,
                RepName = c.AssignedRep != null ? c.AssignedRep.FullName : null,
                c.IsActive,
            })
            .ToPagedResultAsync(request, cancellationToken);

        var states = await lookups.GetStatesAsync(cancellationToken);
        var items = page.Items
            .Select(c => new CustomerListItemDto(
                c.Id, c.Code, c.Name, c.Gstin, c.City, states.GetValueOrDefault(c.StateCode)?.Name ?? c.StateCode,
                c.Phone, c.RepName, c.IsActive))
            .ToList();
        return new PagedResult<CustomerListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<CustomerDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .Include(c => c.AssignedRep)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Customer {id} was not found.");

        return await ToDtoAsync(customer, cancellationToken);
    }

    public async Task<CustomerDto> CreateAsync(CustomerSaveRequest request, CancellationToken cancellationToken)
    {
        var (billing, shipping, gstin) = await ValidateAsync(request, cancellationToken);

        var code = Text.NullIfBlank(request.Code)?.ToUpperInvariant() ?? await NextCodeAsync(cancellationToken);
        if (await db.Customers.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new ValidationException("code", $"Customer code {code} is already used.");
        }

        var customer = new Customer { Code = code, Name = request.Name.Trim(), BillingAddress = billing };
        ApplyDetails(customer, request, shipping, gstin);

        if (currentUser.Has(Permissions.CreditTermsEdit))
        {
            await ApplyCommercialTermsAsync(customer, request);
        }
        else
        {
            customer.CreditLimit = null;
            customer.PaymentTermsDays = DefaultPaymentTermsDays;
            customer.AssignedRepId = currentUser.UserId;
        }

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(customer.Id, cancellationToken);
    }

    public async Task<CustomerDto> UpdateAsync(int id, CustomerSaveRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Customer {id} was not found.");
        db.Entry(customer).Property(c => c.RowVersion).OriginalValue = RowVersions.Parse(request.RowVersion);

        var (billing, shipping, gstin) = await ValidateAsync(request, cancellationToken);

        customer.BillingAddress = billing;
        ApplyDetails(customer, request, shipping, gstin);

        // Sales reps may edit contact details and addresses; commercial terms stay as they were.
        if (currentUser.Has(Permissions.CreditTermsEdit))
        {
            await ApplyCommercialTermsAsync(customer, request);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private static void ApplyDetails(Customer customer, CustomerSaveRequest request, Address? shipping, string? gstin)
    {
        customer.Name = request.Name.Trim();
        customer.TradeName = Text.NullIfBlank(request.TradeName);
        customer.Gstin = gstin;
        customer.ContactPerson = Text.NullIfBlank(request.ContactPerson);
        customer.Phone = Text.NormalizePhone(request.Phone);
        customer.Email = Text.NullIfBlank(request.Email);
        customer.ShippingAddress = shipping;
        customer.Notes = Text.NullIfBlank(request.Notes);
        customer.IsActive = request.IsActive;
        customer.DocumentLanguage = request.DocumentLanguage;
    }

    private async Task ApplyCommercialTermsAsync(Customer customer, CustomerSaveRequest request)
    {
        if (request.AssignedRepId is { } repId && !await LookupService.IsAssignableRepAsync(db, repId))
        {
            throw new ValidationException("assignedRepId", "Choose an active sales rep or sales manager.");
        }

        customer.CreditLimit = request.CreditLimit;
        customer.PaymentTermsDays = request.PaymentTermsDays;
        customer.AssignedRepId = request.AssignedRepId;
    }

    /// <summary>Cross-field rules DataAnnotations can't express: real states, and GSTIN matching the billing state.</summary>
    private async Task<(Address Billing, Address? Shipping, string? Gstin)> ValidateAsync(
        CustomerSaveRequest request, CancellationToken cancellationToken)
    {
        var states = await lookups.GetStatesAsync(cancellationToken);
        var errors = new Dictionary<string, string[]>();

        if (!states.ContainsKey(request.BillingAddress.StateCode))
        {
            errors["billingAddress.stateCode"] = ["Choose a valid state."];
        }

        Address? shipping = null;
        if (!request.ShippingSameAsBilling)
        {
            if (request.ShippingAddress is null)
            {
                errors["shippingAddress"] = ["Enter the shipping address, or tick 'same as billing'."];
            }
            else
            {
                if (!states.ContainsKey(request.ShippingAddress.StateCode))
                {
                    errors["shippingAddress.stateCode"] = ["Choose a valid state."];
                }

                shipping = ToAddress(request.ShippingAddress);
            }
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
            else if (gstinState != request.BillingAddress.StateCode && states.TryGetValue(request.BillingAddress.StateCode, out var billingState))
            {
                errors["gstin"] = [$"This GSTIN is registered in {registeredIn.Name}, but the billing address is in {billingState.Name}. The billing address must be the GST-registered address."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some fields need attention.", errors);
        }

        return (ToAddress(request.BillingAddress), shipping, gstin);
    }

    private static Address ToAddress(AddressInput input) => new()
    {
        Line1 = input.Line1.Trim(),
        Line2 = Text.NullIfBlank(input.Line2),
        City = input.City.Trim(),
        StateCode = input.StateCode,
        Pincode = input.Pincode,
    };

    // A constant (not an interpolated string) so it's obviously free of user input.
    private const string NextCodeSql = "SELECT NEXT VALUE FOR [dbo].[" + CustomerConfiguration.CodeSequence + "] AS [Value]";

    /// <summary>Next generated code, e.g. C00042. Sequence values are never reused, so codes may have gaps.</summary>
    private async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        // ToListAsync, not SingleAsync: composing (TOP 2) would wrap the SQL in a subquery,
        // and SQL Server doesn't allow NEXT VALUE FOR inside derived tables.
        var next = (await db.Database.SqlQueryRaw<int>(NextCodeSql).ToListAsync(cancellationToken)).Single();
        return $"C{next:D5}";
    }

    private async Task<CustomerDto> ToDtoAsync(Customer c, CancellationToken cancellationToken)
    {
        var states = await lookups.GetStatesAsync(cancellationToken);
        AddressDto ToDto(Address a) => new(a.Line1, a.Line2, a.City, a.StateCode, states.GetValueOrDefault(a.StateCode)?.Name ?? a.StateCode, a.Pincode);

        return new CustomerDto(
            c.Id, c.Code, c.Name, c.TradeName, c.Gstin, c.Gstin is null ? null : Gstin.Pan(c.Gstin),
            c.ContactPerson, c.Phone, c.Email,
            ToDto(c.BillingAddress), c.ShippingAddress is null ? null : ToDto(c.ShippingAddress),
            c.CreditLimit, c.PaymentTermsDays, c.AssignedRepId, c.AssignedRep?.FullName,
            c.Notes, c.IsActive, c.DocumentLanguage, c.CreatedAtUtc, c.UpdatedAtUtc, RowVersions.ToToken(c.RowVersion));
    }
}
