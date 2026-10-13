namespace SalesApp.AI;

/// <summary>The Claude API key. Kept in user-secrets (development) or an environment variable; never in appsettings.</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string? ApiKey { get; set; }
}

/// <summary>Names of the app features that call Claude, used for per-feature settings and usage logging.</summary>
public static class AiFeatures
{
    public const string OrderEntry = "OrderEntry";
}

/// <summary>How the AI features behave: which model each uses, spending limits and the price list for cost estimates.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Master switch. With it off (or no API key) the app runs normally and hides the AI screens.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Estimated spend allowed per user in any 24 hours, in US dollars.</summary>
    public decimal DailyBudgetUsdPerUser { get; set; } = 2m;

    public int RequestsPerMinutePerUser { get; set; } = 10;

    /// <summary>For showing estimated costs in rupees.</summary>
    public decimal UsdToInr { get; set; } = 88m;

    public Dictionary<string, AiFeatureOptions> Features { get; set; } = [];

    /// <summary>US dollars per million tokens, by model id.</summary>
    public Dictionary<string, ModelPricing> Pricing { get; set; } = [];

    public AiFeatureOptions For(string feature) => Features.TryGetValue(feature, out var options) ? options : new AiFeatureOptions();
}

public sealed class AiFeatureOptions
{
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>low, medium, high or max: how much the model thinks before answering.</summary>
    public string Effort { get; set; } = "medium";

    public int MaxTokens { get; set; } = 8000;
}

/// <summary>US dollars per million tokens.</summary>
public sealed class ModelPricing
{
    public decimal Input { get; set; }
    public decimal Output { get; set; }
    public decimal CacheRead { get; set; }
    public decimal CacheWrite { get; set; }
}
