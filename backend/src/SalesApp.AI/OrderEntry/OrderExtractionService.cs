using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesApp.AI.Claude;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.OrderEntry;

/// <summary>What the user gave us: pasted text, a file, or both, and the customer the order is from.</summary>
public sealed record OrderExtractionInput(string? Text, byte[]? File, int? CustomerId);

public interface IOrderExtractionService
{
    Task<OrderExtractionDto> ExtractAsync(OrderExtractionInput input, CancellationToken cancellationToken);
}

/// <summary>
/// Reads an order from a WhatsApp message, email, PDF or photo with one Claude call (no tools, structured output),
/// then checks everything the model said against the database: product ids must exist, prices come from the price
/// list, and the customer is matched by rules. Nothing is saved apart from the usage log.
/// </summary>
public sealed class OrderExtractionService(
    AppDbContext db,
    IClaudeGateway claude,
    CustomerMatcher customers,
    IBusinessClock clock,
    IOptionsMonitor<AiOptions> options,
    ILogger<OrderExtractionService> logger) : IOrderExtractionService
{
    public const int MaxTextLength = 20_000;
    private const int MaxAliasesPerProduct = 10;

    private static readonly JsonSerializerOptions ReplyJson = new(JsonSerializerDefaults.Web);

    public async Task<OrderExtractionDto> ExtractAsync(OrderExtractionInput input, CancellationToken cancellationToken)
    {
        var text = input.Text?.Trim();
        if (string.IsNullOrEmpty(text) && input.File is not { Length: > 0 })
        {
            throw new ValidationException("text", "Paste the customer's message or attach their order.");
        }

        if (text?.Length > MaxTextLength)
        {
            throw new ValidationException("text", $"The message is too long. Paste at most {MaxTextLength:N0} characters.");
        }

        // The sales person says who the order is from; what the order itself says about the buyer is only a cross-check.
        if (input.CustomerId is not { } customerId || !await db.Customers.AnyAsync(c => c.Id == customerId && c.IsActive, cancellationToken))
        {
            throw new ValidationException("customerId", "Choose the customer the order is from.");
        }

        var (fileContent, source) = ReadFile(input.File);
        var catalog = await LoadCatalogAsync(cancellationToken);

        var content = new List<ClaudeContent>();
        if (fileContent is not null)
        {
            content.Add(fileContent);
        }

        content.Add(new ClaudeText(OrderExtractionPrompt.UserText(clock.Today, text, fileContent is not null)));

        var response = await claude.SendAsync(new ClaudeRequest(
            AiFeatures.OrderEntry,
            [new ClaudeSystemBlock(OrderExtractionPrompt.Instructions), new ClaudeSystemBlock(OrderExtractionPrompt.Catalog(catalog), CacheAfter: true)],
            content,
            OrderExtractionPrompt.Schema), cancellationToken);

        var reply = Parse(response.Text);
        var aliases = catalog.SelectMany(p => p.Aliases.Select(a => (Alias: AliasText.Normalize(a), Product: p)))
            .GroupBy(a => a.Alias).ToDictionary(g => g.Key, g => g.First().Product);
        // Today's price: the rate for today's period if one is set, else the list price. The order is priced the same way.
        var today = clock.Today;
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new
            {
                p.Id, p.Sku, p.Name, p.Unit, OnHand = p.Stock == null ? 0m : p.Stock.QuantityOnHand, p.HsnCode!.GstRate,
                Price = db.ProductRates
                    .Where(r => r.ProductId == p.Id && r.EffectiveFrom <= today && (r.EffectiveTo == null || r.EffectiveTo >= today))
                    .Select(r => (decimal?)r.Rate)
                    .FirstOrDefault() ?? p.ListPrice,
            })
            .ToDictionaryAsync(p => p.Id, p => new ExtractedProductDto(p.Id, p.Sku, p.Name, p.Unit, p.Price, p.OnHand, p.GstRate), cancellationToken);

        var lines = reply.IsOrder
            ? reply.Lines.Take(200).Select(line => ToLine(line, products, aliases)).ToList()
            : [];

        var clues = new CustomerCluesDto(Blank(reply.Customer.Name), Blank(reply.Customer.Gstin), Blank(reply.Customer.Phone),
            Blank(reply.Customer.City), Blank(reply.Customer.ContactPerson));
        var customer = await customers.MatchAsync(clues, input.CustomerId, cancellationToken);

        var usage = response.Usage;
        return new OrderExtractionDto(
            source,
            reply.IsOrder,
            customer,
            Clip(Blank(reply.CustomerReference), 50),
            DateOnly.TryParseExact(reply.RequestedDeliveryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            Clip(Blank(reply.Notes), 2000),
            lines,
            reply.Unmatched.Where(u => !string.IsNullOrWhiteSpace(u)).Take(50).ToList(),
            new AiUsageDto(usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheReadTokens, usage.CacheWriteTokens,
                usage.CostUsd, Math.Round(usage.CostUsd * options.CurrentValue.UsdToInr, 2), usage.DurationMs));
    }

    private static (ClaudeContent? Content, OrderSource Source) ReadFile(byte[]? file)
    {
        if (file is not { Length: > 0 })
        {
            return (null, OrderSource.AiText);
        }

        var type = OrderFile.Detect(file)
            ?? throw new ValidationException("file", "Attach a PDF or an image (JPEG, PNG, GIF or WebP).");

        if (type.Kind == OrderFileKind.Pdf)
        {
            return file.Length > OrderFile.MaxPdfBytes
                ? throw new ValidationException("file", "PDFs can be at most 10 MB.")
                : (new ClaudePdf(file), OrderSource.AiPdf);
        }

        return file.Length > OrderFile.MaxImageBytes
            ? throw new ValidationException("file", "Images can be at most 5 MB.")
            : (new ClaudeImage(file, type.MediaType), OrderSource.AiImage);
    }

    private async Task<List<CatalogEntry>> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Sku, p.Name, p.Unit, Category = p.Category!.Name, p.Description })
            .ToListAsync(cancellationToken);
        var aliases = await db.ProductAliases.AsNoTracking()
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new { a.ProductId, a.Alias })
            .ToListAsync(cancellationToken);
        var aliasesByProduct = aliases.GroupBy(a => a.ProductId)
            .ToDictionary(g => g.Key, g => g.Take(MaxAliasesPerProduct).Select(a => a.Alias).Order(StringComparer.Ordinal).ToList());

        return products
            .Select(p => new CatalogEntry(p.Id, p.Sku, p.Name, p.Unit.ToString(), p.Category, p.Description,
                aliasesByProduct.GetValueOrDefault(p.Id) ?? []))
            .ToList();
    }

    private Reply Parse(string json)
    {
        try
        {
            var reply = JsonSerializer.Deserialize<ModelReply>(json, ReplyJson) ?? throw new JsonException("Empty reply");
            return new Reply(reply.IsOrder, reply.Customer ?? new ModelCustomer(null, null, null, null, null), reply.CustomerReference,
                reply.RequestedDeliveryDate, reply.Notes, reply.Lines ?? [], reply.Unmatched ?? []);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Order extraction reply wasn't valid JSON");
            throw new AiServiceException("Claude's reply couldn't be read. Try again, or enter the order manually.", 502, exception);
        }
    }

    private static ExtractedLineDto ToLine(
        ModelLine line,
        Dictionary<int, ExtractedProductDto> products,
        Dictionary<string, CatalogEntry> aliases)
    {
        var confidence = line.Confidence?.ToUpperInvariant() switch
        {
            "HIGH" => ExtractionConfidence.High,
            "MEDIUM" => ExtractionConfidence.Medium,
            _ => ExtractionConfidence.Low,
        };
        var note = Blank(line.Note);

        int? productId = null;
        var matchedBy = ProductMatchSource.None;
        if (line.ProductId > 0 && products.ContainsKey(line.ProductId))
        {
            productId = line.ProductId;
            matchedBy = ProductMatchSource.Ai;
        }
        else
        {
            if (line.ProductId > 0)
            {
                // A product id that isn't in the catalog is never trusted, whatever the model's confidence.
                note = L.Show("The suggested product isn't in the catalog; choose one.");
            }

            if (aliases.TryGetValue(AliasText.Normalize(line.SourceText), out var aliased) && products.ContainsKey(aliased.Id))
            {
                productId = aliased.Id;
                matchedBy = ProductMatchSource.Alias;
                confidence = ExtractionConfidence.Medium;
            }
            else
            {
                confidence = ExtractionConfidence.Low;
            }
        }

        var product = productId is { } id ? products[id] : null;
        decimal? quantity = line.Quantity > 0 ? Math.Round(line.Quantity, 3) : null;
        decimal? stated = line.UnitPrice > 0 ? Math.Round(line.UnitPrice, 2) : null;
        decimal? variance = product is not null && stated is { } price && product.Price > 0
            ? Math.Round((price - product.Price) / product.Price, 4)
            : null;

        return new ExtractedLineDto(
            Clip(line.SourceText?.Trim() ?? "", 200) ?? "",
            product,
            quantity,
            Blank(line.SourceUnit),
            stated,
            variance,
            confidence,
            matchedBy,
            note);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Clip(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private sealed record ModelReply(
        bool IsOrder,
        ModelCustomer? Customer,
        string? CustomerReference,
        string? RequestedDeliveryDate,
        string? Notes,
        List<ModelLine>? Lines,
        List<string>? Unmatched);

    /// <summary>The reply with every list and object present, whatever the model left out.</summary>
    private sealed record Reply(
        bool IsOrder,
        ModelCustomer Customer,
        string? CustomerReference,
        string? RequestedDeliveryDate,
        string? Notes,
        List<ModelLine> Lines,
        List<string> Unmatched);

    private sealed record ModelCustomer(string? Name, string? Gstin, string? Phone, string? City, string? ContactPerson);

    private sealed record ModelLine(string? SourceText, int ProductId, decimal Quantity, string? SourceUnit, decimal UnitPrice, string? Confidence, string? Note);
}
