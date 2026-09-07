using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Net.Http.Headers;

namespace Explore.ServiceDefaults.HealthChecks;

public static class HealthCheckResponseWriter
{
    public const string RedactedValue = "redacted";
    public const string RedactedErrorMessage = "Health check failed. See service logs for details.";
    private const string HealthStatusHeaderName = "X-Health-Status";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] SensitiveKeyFragments =
    [
        "accesskey",
        "address",
        "apikey",
        "body",
        "bucket",
        "connectionstring",
        "connection",
        "credential",
        "endpoint",
        "eventtitle",
        "evidence",
        "exception",
        "filesystem",
        "modelid",
        "objectkey",
        "password",
        "path",
        "payload",
        "prompt",
        "providerresponse",
        "providerid",
        "providermessageid",
        "recipient",
        "requestid",
        "response",
        "secret",
        "subject",
        "tenantid",
        "token",
        "userid",
        "uri",
        "url"
    ];

    private static readonly string[] SensitiveValueFragments =
    [
        "access_key",
        "apikey",
        "api_key",
        "authorization:",
        "bearer ",
        "credential",
        "data source=",
        "database=",
        "host=",
        "password",
        "secret",
        "server=",
        "token"
    ];

    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers[HeaderNames.Connection] = "close";
        context.Response.Headers[HeaderNames.AccessControlAllowOrigin] = "*";
        context.Response.Headers[HealthStatusHeaderName] = report.Status.ToString();
        context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
        context.Response.Headers[HeaderNames.Pragma] = "no-cache";

        var response = new
        {
            status = report.Status.ToString(),
            message = report.Status switch
            {
                HealthStatus.Healthy => "Ok",
                HealthStatus.Degraded => "Degraded",
                HealthStatus.Unhealthy => "Service Unavailable",
                _ => "Unknown"
            },
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = SanitizeDescription(e.Value.Description),
                duration = e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception is null ? null : RedactedErrorMessage,
                data = e.Value.Data.Count > 0 ? SanitizeData(e.Value.Data) : null
            })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static Dictionary<string, object?> SanitizeData(IReadOnlyDictionary<string, object> data)
    {
        var sanitized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in data)
        {
            sanitized[key] = SanitizeDataValue(key, value);
        }

        return sanitized;
    }

    private static object? SanitizeDataValue(string key, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (IsSensitiveKey(key))
        {
            return RedactedValue;
        }

        if (IsSafePrimitive(value))
        {
            return value;
        }

        if (value is string text)
        {
            return IsSensitiveText(text)
                ? RedactedValue
                : Truncate(text);
        }

        if (value is Uri)
        {
            return RedactedValue;
        }

        return RedactedValue;
    }

    private static string? SanitizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        return IsSensitiveText(description) ? null : Truncate(description);
    }

    private static bool IsSafePrimitive(object value)
    {
        return value is bool
            or byte
            or sbyte
            or short
            or ushort
            or int
            or uint
            or long
            or ulong
            or float
            or double
            or decimal;
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = Normalize(key);
        return SensitiveKeyFragments.Any(normalized.Contains);
    }

    private static bool IsSensitiveText(string text)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out _))
        {
            return true;
        }

        if (text.Contains("://", StringComparison.Ordinal)
            || text.StartsWith('/')
            || text.Contains(":\\", StringComparison.Ordinal))
        {
            return true;
        }

        var normalized = text.Trim().ToLowerInvariant();
        return SensitiveValueFragments.Any(normalized.Contains);
    }

    private static string Normalize(string value)
    {
        return value.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static string Truncate(string value)
    {
        const int maxLength = 128;
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
    }
}
