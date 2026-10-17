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

public sealed class ProductService(AppDbContext db, ICurrentUser currentUser, IBusinessClock clock, IProductImageUrls imageUrls) : IProductService
{
    private static readonly SortMap<Product> Sorts = new SortMap<Product>()
        .Add("sku", p => p.Sku)
        .Add("name", p => p.Name)
        .Add("category", p => p.Category!.Name)
        .Add("listPrice", p => p.ListPrice)
        .Add("quantityOnHand", p => p.Stock!.QuantityOnHand)
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
            query = query.Where(p => p.Stock!.QuantityOnHand <= p.ReorderLevel);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(p => p.Sku.Contains(q) || p.Name.Contains(q) || p.HsnCode!.Code.StartsWith(q));
        }

        var day = request.PriceOn ?? clock.Today;
        return await Sorts.Apply(query, request.Sort, "name")
            .ThenBy(p => p.Id)
            .Select(p => new
            {
                Product = p,
                Rate = db.ProductRates
                    .Where(r => r.ProductId == p.Id && r.EffectiveFrom <= day && (r.EffectiveTo == null || r.EffectiveTo >= day))
                    .Select(r => (decimal?)r.Rate)
                    .FirstOrDefault(),
            })
            .Select(x => new ProductListItemDto(
                x.Product.Id, x.Product.Sku, x.Product.Name, x.Product.Category!.Name, x.Product.HsnCode!.Code, x.Product.HsnCode.GstRate,
                x.Product.Unit, x.Product.ListPrice, x.Rate ?? x.Product.ListPrice, x.Rate != null,
                x.Product.Stock != null ? x.Product.Stock.QuantityOnHand : 0m, x.Product.ReorderLevel, x.Product.IsActive))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<ProductDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.HsnCode)
            .Include(p => p.Stock)
            .Include(p => p.PreferredSupplier)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Product {id} was not found.");
        var imageVersion = await db.ProductImages
            .Where(i => i.ProductId == id && i.ImageData != null)
            .Select(i => (int?)i.Version)
            .SingleOrDefaultAsync(cancellationToken);

        return ToDto(product, imageVersion);
    }

    public async Task<ProductDto> CreateAsync(ProductSaveRequest request, CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();
        await EnsureSkuIsFreeAsync(sku, excludeId: null, cancellationToken);
        await EnsureReferencesAsync(request, current: null, cancellationToken);

        // Every product gets its stock row up front; quantities then only change through the stock ledger.
        var product = new Product { Sku = sku, Name = request.Name.Trim(), Stock = new StockLevel() };
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
        await EnsureReferencesAsync(request, product, cancellationToken);

        product.Sku = sku;
        Apply(product, request);
        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Copies the form onto the product. Cost price only changes for users who may see it, so saving from a form
    /// without it (which shows no cost) never wipes it.
    /// </summary>
    private void Apply(Product product, ProductSaveRequest request)
    {
        product.Name = request.Name.Trim();
        product.Description = Text.NullIfBlank(request.Description);
        product.CategoryId = request.CategoryId;
        product.HsnCodeId = request.HsnCodeId;
        product.Unit = request.Unit;
        product.ListPrice = request.ListPrice;
        if (currentUser.Has(Permissions.CostPricesView))
        {
            product.CostPrice = request.CostPrice;
        }

        product.ReorderLevel = request.ReorderLevel;
        product.ReorderQuantity = request.ReorderQuantity;
        product.LeadTimeDays = request.LeadTimeDays;
        product.PreferredSupplierId = request.PreferredSupplierId;
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
    /// New products must point at an active category, HSN code and (if any) preferred supplier. An existing product
    /// may keep a reference that has since been deactivated, so editing its price doesn't force a reclassification.
    /// </summary>
    private async Task EnsureReferencesAsync(ProductSaveRequest request, Product? current, CancellationToken cancellationToken)
    {
        var (categoryId, hsnCodeId) = (request.CategoryId, request.HsnCodeId);
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

        if (request.PreferredSupplierId is { } supplierId)
        {
            var supplierActive = await db.Suppliers.Where(s => s.Id == supplierId).Select(s => (bool?)s.IsActive).SingleOrDefaultAsync(cancellationToken);
            if (supplierActive is null || (supplierActive == false && current?.PreferredSupplierId != supplierId))
            {
                errors["preferredSupplierId"] = ["Choose an active supplier."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Some fields need attention.", errors);
        }
    }

    private ProductDto ToDto(Product p, int? imageVersion) => new(
        p.Id, p.Sku, p.Name, p.Description,
        p.CategoryId, p.Category!.Name,
        p.HsnCodeId, p.HsnCode!.Code, p.HsnCode.GstRate,
        p.Unit, p.ListPrice,
        currentUser.Has(Permissions.CostPricesView) ? p.CostPrice : null,
        p.ReorderLevel, p.ReorderQuantity, p.LeadTimeDays, p.PreferredSupplierId, p.PreferredSupplier?.Name, p.Stock?.QuantityOnHand ?? 0m,
        p.IsActive, p.CreatedAtUtc, p.UpdatedAtUtc, RowVersions.ToToken(p.RowVersion),
        imageVersion is { } version ? imageUrls.Create(p.Id, version) : null);
}
