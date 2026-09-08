using Serilog.Context;

namespace Explore.API.Middleware;

/// <summary>
/// Ensures every request has a correlation ID for distributed tracing.
/// Reads from incoming X-Correlation-ID / X-Request-ID headers, generates one if absent,
/// pushes it into Serilog LogContext, and echoes it on the response.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private const string RequestIdHeader = "X-Request-ID";
    private const string LogPropertyName = "CorrelationId";
    private const int MaxCorrelationHeaderLength = 128;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetSafeHeaderValue(context, CorrelationIdHeader)
            ?? GetSafeHeaderValue(context, RequestIdHeader)
            ?? context.TraceIdentifier;

        context.Items[LogPropertyName] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await _next(context);
        }
    }

    private static string? GetSafeHeaderValue(HttpContext context, string headerName)
    {
        var values = context.Request.Headers[headerName];
        if (values.Count != 1)
        {
            return null;
        }

        var value = values[0];
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCorrelationHeaderLength)
        {
            return null;
        }

        return value.All(character => character is >= '!' and <= '~')
            ? value
            : null;
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
