using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using R3.Contracts;
using R3.Infrastructure;

namespace R3.Server;

/// <summary>
/// The single place unhandled exceptions become an HTTP response. Kept as a
/// standalone, DI-free static method (HttpContext + Exception + ILogger in,
/// Task out) precisely so it is unit-testable without spinning up the whole
/// ASP.NET Core pipeline via WebApplicationFactory.
///
/// This is the "responsible boundary" for logging server exceptions (see
/// docs/LOGGING.md) - nothing upstream of this (Infrastructure, Application)
/// should also log the same exception, to avoid duplicate log entries for one
/// failure.
/// </summary>
public static class GlobalExceptionHandling
{
    public static async Task HandleAsync(HttpContext context, Exception exception, ILogger logger)
    {
        var traceId = context.TraceIdentifier;
        context.Response.ContentType = "application/json";

        if (exception is OrganizationRuleException rule)
        {
            // Expected business-rule rejection (duplicate code, inactive parent, ...),
            // not a bug - Warning gives operators visibility without Error-level noise.
            logger.LogWarning(
                "Business rule rejected the request. RuleCode={RuleCode} TraceId={TraceId}",
                rule.Code, traceId);
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ApiError(rule.Code, rule.Message, new Dictionary<string, string[]>(), traceId));
            return;
        }

        var errorId = Guid.NewGuid();
        logger.LogError(exception, "Unhandled server exception. ErrorId={ErrorId} TraceId={TraceId}", errorId, traceId);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ApiError("server.error", "Beklenmeyen bir sunucu hatası oluştu.", new Dictionary<string, string[]>(), traceId));
    }
}
