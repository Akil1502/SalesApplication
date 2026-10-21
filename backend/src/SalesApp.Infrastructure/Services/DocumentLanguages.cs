using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

/// <summary>The language a customer's or supplier's documents print in: their own choice, else the company's default.</summary>
internal static class DocumentLanguages
{
    public static async Task<DocumentLanguage> ForCustomerAsync(AppDbContext db, int customerId, CancellationToken cancellationToken) =>
        await db.Customers.Where(c => c.Id == customerId).Select(c => c.DocumentLanguage).SingleOrDefaultAsync(cancellationToken)
        ?? await DefaultAsync(db, cancellationToken);

    public static async Task<DocumentLanguage> ForSupplierAsync(AppDbContext db, int supplierId, CancellationToken cancellationToken) =>
        await db.Suppliers.Where(s => s.Id == supplierId).Select(s => s.DocumentLanguage).SingleOrDefaultAsync(cancellationToken)
        ?? await DefaultAsync(db, cancellationToken);

    /// <summary>For a page of a list: every customer on it, keyed by id.</summary>
    public static async Task<Dictionary<int, DocumentLanguage>> ForCustomersAsync(AppDbContext db, int[] customerIds, CancellationToken cancellationToken)
    {
        var fallback = await DefaultAsync(db, cancellationToken);
        var chosen = await db.Customers.Where(c => customerIds.Contains(c.Id)).Select(c => new { c.Id, c.DocumentLanguage }).ToListAsync(cancellationToken);
        return customerIds.Distinct().ToDictionary(id => id, id => chosen.FirstOrDefault(c => c.Id == id)?.DocumentLanguage ?? fallback);
    }

    private static Task<DocumentLanguage> DefaultAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.CompanySettings.Select(s => s.DocumentLanguage).SingleAsync(cancellationToken);
}
