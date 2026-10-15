using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<CategoryDto> CreateAsync(CategorySaveRequest request, CancellationToken cancellationToken);
    Task<CategoryDto> UpdateAsync(int id, CategorySaveRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}

/// <summary>Product categories. Small lists (tens of rows), so no paging.</summary>
public sealed class CategoryService(AppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await db.Categories
            .AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive, db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);

    public async Task<CategoryDto> CreateAsync(CategorySaveRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, excludeId: null, cancellationToken);

        var category = new Category { Name = name, Description = Text.NullIfBlank(request.Description), IsActive = request.IsActive };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return new CategoryDto(category.Id, category.Name, category.Description, category.IsActive, 0);
    }

    public async Task<CategoryDto> UpdateAsync(int id, CategorySaveRequest request, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException($"Category {id} was not found.");

        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, id, cancellationToken);

        category.Name = name;
        category.Description = Text.NullIfBlank(request.Description);
        category.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        var productCount = await db.Products.CountAsync(p => p.CategoryId == id, cancellationToken);
        return new CategoryDto(category.Id, category.Name, category.Description, category.IsActive, productCount);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var category = await db.Categories.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException($"Category {id} was not found.");

        if (await db.Products.AnyAsync(p => p.CategoryId == id, cancellationToken))
        {
            throw new ConflictException($"'{category.Name}' is used by products, so it can't be deleted. Mark it inactive instead.");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureNameIsFreeAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        if (await db.Categories.AnyAsync(c => c.Name == name && c.Id != excludeId, cancellationToken))
        {
            throw new ValidationException("name", $"A category called '{name}' already exists.");
        }
    }
}

public interface IHsnCodeService
{
    Task<IReadOnlyList<HsnCodeDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<HsnCodeDto> CreateAsync(HsnCodeSaveRequest request, CancellationToken cancellationToken);
    Task<HsnCodeDto> UpdateAsync(int id, HsnCodeSaveRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}

/// <summary>
/// HSN codes and their GST rates. Changing a rate affects future invoices only: issued invoices
/// keep the rate copied onto their lines.
/// </summary>
public sealed class HsnCodeService(AppDbContext db) : IHsnCodeService
{
    public async Task<IReadOnlyList<HsnCodeDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await db.HsnCodes
            .AsNoTracking()
            .Where(h => includeInactive || h.IsActive)
            .OrderBy(h => h.Code)
            .Select(h => new HsnCodeDto(h.Id, h.Code, h.Description, h.GstRate, h.IsActive, db.Products.Count(p => p.HsnCodeId == h.Id)))
            .ToListAsync(cancellationToken);

    public async Task<HsnCodeDto> CreateAsync(HsnCodeSaveRequest request, CancellationToken cancellationToken)
    {
        await EnsureCodeIsFreeAsync(request.Code, excludeId: null, cancellationToken);

        var hsn = new HsnCode
        {
            Code = request.Code,
            Description = request.Description.Trim(),
            GstRate = request.GstRate,
            IsActive = request.IsActive,
        };
        db.HsnCodes.Add(hsn);
        await db.SaveChangesAsync(cancellationToken);
        return new HsnCodeDto(hsn.Id, hsn.Code, hsn.Description, hsn.GstRate, hsn.IsActive, 0);
    }

    public async Task<HsnCodeDto> UpdateAsync(int id, HsnCodeSaveRequest request, CancellationToken cancellationToken)
    {
        var hsn = await db.HsnCodes.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException($"HSN code {id} was not found.");

        await EnsureCodeIsFreeAsync(request.Code, id, cancellationToken);

        hsn.Code = request.Code;
        hsn.Description = request.Description.Trim();
        hsn.GstRate = request.GstRate;
        hsn.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        var productCount = await db.Products.CountAsync(p => p.HsnCodeId == id, cancellationToken);
        return new HsnCodeDto(hsn.Id, hsn.Code, hsn.Description, hsn.GstRate, hsn.IsActive, productCount);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var hsn = await db.HsnCodes.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException($"HSN code {id} was not found.");

        if (await db.Products.AnyAsync(p => p.HsnCodeId == id, cancellationToken))
        {
            throw new ConflictException($"HSN {hsn.Code} is used by products, so it can't be deleted. Mark it inactive instead.");
        }

        db.HsnCodes.Remove(hsn);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCodeIsFreeAsync(string code, int? excludeId, CancellationToken cancellationToken)
    {
        if (await db.HsnCodes.AnyAsync(h => h.Code == code && h.Id != excludeId, cancellationToken))
        {
            throw new ValidationException("code", $"HSN code {code} already exists.");
        }
    }
}
