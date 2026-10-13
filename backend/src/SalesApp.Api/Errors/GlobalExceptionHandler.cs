using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;

namespace SalesApp.Api.Errors;

/// <summary>
/// Turns exceptions into <see cref="ApiError"/> responses. Business exceptions map to 4xx with
/// their own message; anything unexpected becomes a 500 with a generic message, so internals
/// (SQL text, stack traces) never reach the browser.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client went away (closed the tab, pressed Stop); there is nobody to answer.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        var (status, error) = exception switch
        {
            ValidationException e => (StatusCodes.Status400BadRequest, new ApiError(e.Message, traceId, e.Errors)),
            NotFoundException e => (StatusCodes.Status404NotFound, new ApiError(e.Message, traceId)),
            ForbiddenException e => (StatusCodes.Status403Forbidden, new ApiError(e.Message, traceId)),
            ConflictException e => (StatusCodes.Status409Conflict, new ApiError(e.Message, traceId)),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, new ApiError(
                "This record was changed by someone else. Reload it and try again.", traceId)),
            _ => (StatusCodes.Status500InternalServerError, new ApiError(
                "Something went wrong on our side. Please try again, and quote the trace id if it keeps happening.", traceId)),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path} (trace {TraceId})",
                httpContext.Request.Method, httpContext.Request.Path, traceId);
        }
        else
        {
            logger.LogInformation("Request failed with {Status}: {Message} (trace {TraceId})", status, exception.Message, traceId);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(error, cancellationToken);
        return true;
    }
}
