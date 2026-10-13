using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface IProductService
{
    Task<PagedResult<ProductListItemDto>> ListAsync(ProductListRequest request, CancellationToken cancellationToken);
    Task<ProductDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<ProductDto> CreateAsync(ProductSaveRequest request, CancellationToken cancellationToken);
    Task<ProductDto> UpdateAsync(int id, ProductSaveRequest request, CancellationToken cancellationToken);
}

public sealed class ProductService(AppDbContext db, ICurrentUser currentUser) : IProductService
{
    private static readonly SortMap<Product> Sorts = new SortMap<Product>()
        .Add("sku", p => p.Sku)
        .Add("name", p => p.Name)
        .Add("category", p => p.Category!.Name)
        .Add("listPrice", p => p.ListPrice)
        .Add("quantityOnHand", p => p.QuantityOnHand)
        .Add("updatedAt", p => p.UpdatedAtUtc);

    public async Task<PagedResult<ProductListItemDto>> ListAsync(ProductListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking();

        if (!request.IncludeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (request.LowStockOnly)
        {
            query = query.Where(p => p.QuantityOnHand <= p.ReorderLevel);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(p => p.Sku.Contains(q) || p.Name.Contains(q) || p.HsnCode!.Code.StartsWith(q));
        }

        return await Sorts.Apply(query, request.Sort, "name")
            .ThenBy(p => p.Id)
            .Select(p => new ProductListItemDto(
                p.Id, p.Sku, p.Name, p.Category!.Name, p.HsnCode!.Code, p.HsnCode.GstRate,
                p.Unit, p.ListPrice, p.QuantityOnHand, p.ReorderLevel, p.IsActive))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<ProductDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.HsnCode)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Product {id} was not found.");

        return ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(ProductSaveRequest request, CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();
        await EnsureSkuIsFreeAsync(sku, excludeId: null, cancellationToken);
        await EnsureReferencesAsync(request.CategoryId, request.HsnCodeId, current: null, cancellationToken);

        var product = new Product { Sku = sku, Name = request.Name.Trim() };
        Apply(product, request);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductDto> UpdateAsync(int id, ProductSaveRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Product {id} was not found.");
        db.Entry(product).Property(p => p.RowVersion).OriginalValue = RowVersions.Parse(request.RowVersion);

        var sku = request.Sku.Trim().ToUpperInvariant();
        await EnsureSkuIsFreeAsync(sku, id, cancellationToken);
        await EnsureReferencesAsync(request.CategoryId, request.HsnCodeId, product, cancellationToken);

        product.Sku = sku;
        Apply(product, request);
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    private static void Apply(Product product, ProductSaveRequest request)
    {
        product.Name = request.Name.Trim();
        product.Description = Text.NullIfBlank(request.Description);
        product.CategoryId = request.CategoryId;
        product.HsnCodeId = request.HsnCodeId;
        product.Unit = request.Unit;
        product.ListPrice = request.ListPrice;
        product.CostPrice = request.CostPrice;
        product.ReorderLevel = request.ReorderLevel;
        product.ReorderQuantity = request.ReorderQuantity;
        product.LeadTimeDays = request.LeadTimeDays;
        product.IsActive = request.IsActive;
    }

    private async Task EnsureSkuIsFreeAsync(string sku, int? excludeId, CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(p => p.Sku == sku && p.Id != excludeId, cancellationToken))
        {
            throw new ValidationException("sku", $"SKU {sku} is already used by another product.");
        }
    }

    /// <summary>
    /// New products must point at an active category and HSN code. An existing product may keep a
    /// reference that has since been deactivated, so editing its price doesn't force a reclassification.
    /// </summary>
    private async Task EnsureReferencesAsync(int categoryId, int hsnCodeId, Product? current, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var categoryActive = await db.Categories.Where(c => c.Id == categoryId).Select(c => (bool?)c.IsActive).SingleOrDefaultAsync(cancellationToken);
        if (categoryActive is null || (categoryActive == false && current?.CategoryId != categoryId))
        {
            errors["categoryId"] = ["Choose an active category."];
        }

        var hsnActive = await db.HsnCodes.Where(h => h.Id == hsnCodeId).Select(h => (bool?)h.IsActive).SingleOrDefaultAsync(cancellationToken);
        if (hsnActive is null || (hsnActive == false && current?.HsnCodeId != hsnCodeId))
        {
            errors["hsnCodeId"] = ["Choose an active HSN code."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some fields need attention.", errors);
        }
    }

    private ProductDto ToDto(Product p) => new(
        p.Id, p.Sku, p.Name, p.Description,
        p.CategoryId, p.Category!.Name,
        p.HsnCodeId, p.HsnCode!.Code, p.HsnCode.GstRate,
        p.Unit, p.ListPrice,
        currentUser.IsManagerOrAdmin ? p.CostPrice : null,
        p.ReorderLevel, p.ReorderQuantity, p.LeadTimeDays, p.QuantityOnHand,
        p.IsActive, p.CreatedAtUtc, p.UpdatedAtUtc, RowVersions.ToToken(p.RowVersion));
}
