namespace SalesApp.AI;

/// <summary>AI is switched off or no API key is configured. The API answers 503.</summary>
public sealed class AiUnavailableException(string message) : Exception(message);

/// <summary>The user has used up their AI budget for the last 24 hours. The API answers 429.</summary>
public sealed class AiBudgetExceededException(string message) : Exception(message);

/// <summary>Claude declined the request. The API answers 422 with Claude's reason when one is given.</summary>
public sealed class AiRefusedException(string message) : Exception(message);

/// <summary>
/// The Claude API couldn't be used: rate limited, overloaded, unreachable, key rejected, or the reply couldn't be
/// read. <see cref="StatusCode"/> is what our API answers (429, 502 or 503).
/// </summary>
public sealed class AiServiceException(string message, int statusCode, Exception? inner = null) : Exception(message, inner)
{
    public int StatusCode { get; } = statusCode;
}
