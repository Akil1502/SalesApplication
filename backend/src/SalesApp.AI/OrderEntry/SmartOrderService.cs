using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Services;

namespace SalesApp.AI.OrderEntry;

public interface ISmartOrderService
{
    Task<OrderDto> ConfirmAsync(SmartOrderConfirmRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Saves a reviewed smart-entry order as an ordinary draft (recording what it was read from), then learns from the
/// user's corrections: each customer wording they mapped to a product becomes an alias for next time.
/// </summary>
public sealed class SmartOrderService(
    AppDbContext db,
    IOrderService orders,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<SmartOrderService> logger) : ISmartOrderService
{
    public async Task<OrderDto> ConfirmAsync(SmartOrderConfirmRequest request, CancellationToken cancellationToken)
    {
        if (request.Source is not (OrderSource.AiText or OrderSource.AiPdf or OrderSource.AiImage))
        {
            throw new ValidationException("source", "Smart order entry saves orders read from text, a PDF or an image.");
        }

        // The order is the part that matters; it's saved (in its own transaction) before any alias is touched.
        var order = await orders.CreateAsync(request.Order with { Source = request.Source }, cancellationToken);

        try
        {
            await LearnAliasesAsync(request.Corrections, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two people teaching the same wording at once; the order is saved either way.
            logger.LogWarning(exception, "Couldn't save product aliases for order {OrderId}", order.Id);
        }

        return order;
    }

    private async Task LearnAliasesAsync(IReadOnlyList<AliasCorrection> corrections, CancellationToken cancellationToken)
    {
        var wanted = corrections
            .Select(c => (c.SourceText, Normalized: AliasText.Normalize(c.SourceText), c.ProductId))
            .Where(c => c.Normalized.Length >= 2)
            .GroupBy(c => c.Normalized)
            .Select(g => g.Last())
            .ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var productIds = wanted.Select(w => w.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id, p => (p.Sku, p.Name), cancellationToken);
        var normalized = wanted.Select(w => w.Normalized).ToList();
        var existing = await db.ProductAliases.Where(a => normalized.Contains(a.NormalizedAlias))
            .ToDictionaryAsync(a => a.NormalizedAlias, cancellationToken);

        foreach (var (sourceText, key, productId) in wanted)
        {
            // Wording that is just the product's own SKU or name teaches nothing new.
            if (!products.TryGetValue(productId, out var product) || key == AliasText.Normalize(product.Sku) || key == AliasText.Normalize(product.Name))
            {
                continue;
            }

            if (existing.TryGetValue(key, out var alias))
            {
                // The latest correction wins: the wording now means this product.
                alias.ProductId = productId;
                alias.Alias = Clip(sourceText.Trim());
                continue;
            }

            db.ProductAliases.Add(new ProductAlias
            {
                ProductId = productId,
                Alias = Clip(sourceText.Trim()),
                NormalizedAlias = key,
                CreatedByUserId = currentUser.UserId,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Clip(string value) => value.Length <= AliasText.MaxLength ? value : value[..AliasText.MaxLength];
}
