using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.AI.AskData;
using SalesApp.AI.Assistant;
using SalesApp.AI.Claude;
using SalesApp.AI.OrderEntry;

namespace SalesApp.AI;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Claude gateway and the AI features. Safe to call without an API key: the gateway then reports
    /// itself unavailable and the rest of the app is unaffected.
    /// </summary>
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        services.AddSingleton<ClaudeClientProvider>();
        services.AddSingleton<IAiUsageRecorder, AiUsageRecorder>();
        services.AddScoped<IClaudeGateway, ClaudeGateway>();

        services.AddScoped<CustomerMatcher>();
        services.AddScoped<IOrderExtractionService, OrderExtractionService>();
        services.AddScoped<ISmartOrderService, SmartOrderService>();

        services.Configure<AskDataOptions>(configuration.GetSection(AskDataOptions.SectionName));
        services.AddSingleton<IReadOnlySqlExecutor, ReadOnlySqlExecutor>();
        services.AddScoped<IGuardedQueryRunner, GuardedQueryRunner>();
        services.AddScoped<IAskDataService, AskDataService>();

        services.Configure<AssistantOptions>(configuration.GetSection(AssistantOptions.SectionName));
        services.AddScoped<AssistantTools>();
        services.AddScoped<AssistantDrafts>();
        services.AddScoped<IAssistantService, AssistantService>();

        return services;
    }
}
