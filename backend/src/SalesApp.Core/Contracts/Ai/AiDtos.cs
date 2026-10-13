using System.ComponentModel.DataAnnotations;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;

namespace SalesApp.Core.Contracts.Ai;

/// <summary>Whether the AI features can be used. The UI hides AI screens when <see cref="Enabled"/> is false.</summary>
public sealed record AiStatusDto(bool Enabled, string? Reason, bool OrderEntry);

public enum ExtractionConfidence
{
    High,
    Medium,
    Low,
}

/// <summary>How a line's product was found.</summary>
public enum ProductMatchSource
{
    /// <summary>Claude matched it against the catalog.</summary>
    Ai,

    /// <summary>The customer's wording matched an alias learned from an earlier correction.</summary>
    Alias,

    /// <summary>No product found; someone has to choose one.</summary>
    None,
}

/// <summary>
/// How the customer was identified, strongest first. Not nullable on purpose: a nullable enum makes the OpenAPI
/// generator mark the enum itself as nullable everywhere it's used.
/// </summary>
public enum CustomerMatchSource
{
    None,
    Selected,
    Gstin,
    Phone,
    Name,
}

/// <summary>What the order said about who sent it. Empty strings mean "not stated".</summary>
public sealed record CustomerCluesDto(string? Name, string? Gstin, string? Phone, string? City, string? ContactPerson);

public sealed record CustomerCandidateDto(int Id, string Code, string Name, string City, decimal Score);

public sealed record CustomerSuggestionDto(
    int? CustomerId,
    string? CustomerName,
    CustomerMatchSource MatchedBy,
    CustomerCluesDto Clues,
    IReadOnlyList<CustomerCandidateDto> Candidates);

/// <summary>The catalog product a line was matched to, with what the review screen shows about it.</summary>
public sealed record ExtractedProductDto(int Id, string Sku, string Name, ProductUnit Unit, decimal ListPrice, decimal QuantityOnHand, decimal GstRate);

/// <summary>
/// One product line read from the order. Prices come from the catalog, never from the message; a price the customer
/// stated is shown alongside with its difference from list price, for the sales person to judge.
/// </summary>
public sealed record ExtractedLineDto(
    string SourceText,
    ExtractedProductDto? Product,
    decimal? Quantity,
    string? StatedUnit,
    decimal? StatedPrice,
    decimal? PriceVariance,
    ExtractionConfidence Confidence,
    ProductMatchSource MatchedBy,
    string? Note);

public sealed record AiUsageDto(
    string Model,
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheWriteTokens,
    decimal CostUsd,
    decimal CostInr,
    int DurationMs);

/// <summary>A draft read from a customer's message or file. Nothing has been saved; the user reviews it first.</summary>
public sealed record OrderExtractionDto(
    OrderSource Source,
    bool LooksLikeOrder,
    CustomerSuggestionDto Customer,
    string? CustomerReference,
    DateOnly? RequestedDeliveryDate,
    string? Notes,
    IReadOnlyList<ExtractedLineDto> Lines,
    IReadOnlyList<string> Unmatched,
    AiUsageDto Usage);

/// <summary>A product the user picked for wording the AI got wrong or couldn't match. Saved as an alias.</summary>
public sealed record AliasCorrection
{
    [Required, MaxLength(200)]
    public string SourceText { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }
}

/// <summary>The reviewed order, plus the corrections to learn from.</summary>
public sealed record SmartOrderConfirmRequest
{
    [Required]
    public OrderSaveRequest Order { get; init; } = new();

    /// <summary>AiText, AiPdf or AiImage: what the order was read from.</summary>
    public OrderSource Source { get; init; } = OrderSource.AiText;

    [MaxLength(200)]
    public List<AliasCorrection> Corrections { get; init; } = [];
}
