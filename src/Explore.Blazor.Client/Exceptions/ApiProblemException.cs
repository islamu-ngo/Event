using System.Net;
namespace Explore.Blazor.Client.Exceptions;

/// <summary>
/// Represents an error response from the API deserialized from RFC 7807 ProblemDetails.
/// Thrown by <see cref="HttpResponseExtensions"/> when the API returns a non-success status code.
/// Consumers can match on <see cref="StatusCode"/> to decide how to handle or surface the error.
/// </summary>
public sealed class ApiProblemException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Title => ProblemDetails.Title ?? StatusCode.ToString();
    public string? Detail => ProblemDetails.Detail;
    public string? TraceId { get; }
    public string ServiceName { get; }
    public ApiProblemDetails ProblemDetails { get; }
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    public ApiProblemException(
        HttpStatusCode statusCode,
        ApiProblemDetails problemDetails,
        string serviceName,
        IReadOnlyDictionary<string, string[]>? validationErrors = null,
        string? traceId = null)
        : base(FormatMessage(statusCode, serviceName, problemDetails.Title, problemDetails.Detail))
    {
        StatusCode = statusCode;
        ProblemDetails = problemDetails;
        ServiceName = serviceName;
        ValidationErrors = validationErrors ?? new Dictionary<string, string[]>();
        TraceId = traceId;
    }

    public ApiProblemException(HttpStatusCode statusCode, string title, string? detail = null, string? traceId = null, string serviceName = "HTTP API")
        : this(
            statusCode,
            new ApiProblemDetails
            {
                Status = (int)statusCode,
                Title = title,
                Detail = detail
            },
            serviceName,
            traceId: traceId)
    {
    }

    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
    public bool IsGone => StatusCode == HttpStatusCode.Gone;
    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;
    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

    public static async Task<ApiProblemException> FromResponseAsync(
        HttpResponseMessage response,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var statusCode = response.StatusCode;
        var traceId = response.Headers.TryGetValues("traceparent", out var traceparentValues)
            ? traceparentValues.FirstOrDefault()
            : null;

        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return CreateFallback(statusCode, response.ReasonPhrase, serviceName, traceId);
            }

            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;

            var problemDetails = new ApiProblemDetails
            {
                Status = root.TryGetProperty("status", out var statusProp) && statusProp.TryGetInt32(out var parsedStatus)
                    ? parsedStatus
                    : (int)statusCode,
                Title = root.TryGetProperty("title", out var titleProp)
                    ? titleProp.GetString() ?? response.ReasonPhrase ?? statusCode.ToString()
                    : response.ReasonPhrase ?? statusCode.ToString(),
                Detail = root.TryGetProperty("detail", out var detailProp)
                    ? detailProp.GetString()
                    : root.TryGetProperty("error", out var legacyErrorProp)
                        ? legacyErrorProp.GetString()
                        : null,
                Type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null,
                Instance = root.TryGetProperty("instance", out var instanceProp) ? instanceProp.GetString() : null
            };

            if (root.TryGetProperty("traceId", out var traceIdProp))
            {
                traceId = traceIdProp.GetString() ?? traceId;
            }

            if (traceId is not null)
            {
                problemDetails.Extensions["traceId"] = traceId;
            }

            if (root.TryGetProperty("timestamp", out var timestampProp))
            {
                problemDetails.Extensions["timestamp"] = timestampProp.ValueKind == System.Text.Json.JsonValueKind.String
                    ? timestampProp.GetString() ?? string.Empty
                    : timestampProp.GetRawText();
            }

            return new ApiProblemException(
                statusCode,
                problemDetails,
                serviceName,
                ReadValidationErrors(root),
                traceId);
        }
        catch (System.Text.Json.JsonException)
        {
            return CreateFallback(statusCode, response.ReasonPhrase, serviceName, traceId);
        }
    }

    public static ApiProblemException FromApiException(Clients.ApiException exception, string serviceName)
    {
        var traceId = exception.Headers is not null && exception.Headers.TryGetValue("traceparent", out var traceparentValues)
            ? traceparentValues.FirstOrDefault()
            : null;

        var statusCode = (HttpStatusCode)exception.StatusCode;
        var body = exception.Response;

        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);
                var root = document.RootElement;

                var problemDetails = new ApiProblemDetails
                {
                    Status = root.TryGetProperty("status", out var statusProp) && statusProp.TryGetInt32(out var parsedStatus)
                        ? parsedStatus
                        : exception.StatusCode,
                    Title = root.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString() ?? exception.Message
                        : root.TryGetProperty("error", out var errorProp)
                            ? errorProp.GetString() ?? exception.Message
                            : exception.Message,
                    Detail = root.TryGetProperty("detail", out var detailProp)
                        ? detailProp.GetString()
                        : root.TryGetProperty("error", out var legacyErrorProp)
                            ? legacyErrorProp.GetString()
                            : exception.Response,
                    Type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null,
                    Instance = root.TryGetProperty("instance", out var instanceProp) ? instanceProp.GetString() : null
                };

                if (root.TryGetProperty("traceId", out var traceIdProp))
                {
                    traceId = traceIdProp.GetString() ?? traceId;
                }

                if (traceId is not null)
                {
                    problemDetails.Extensions["traceId"] = traceId;
                }

                return new ApiProblemException(statusCode, problemDetails, serviceName, ReadValidationErrors(root), traceId);
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        var fallbackMessage = !string.IsNullOrWhiteSpace(exception.Response)
            ? exception.Response
            : exception.Message;

        return CreateFallback(statusCode, fallbackMessage, serviceName, traceId);
    }

    public static ApiProblemException FromRefitException(Refit.ApiException exception, string serviceName)
    {
        var traceId = exception.Headers is not null && exception.Headers.TryGetValues("traceparent", out var traceparentValues)
            ? traceparentValues.FirstOrDefault()
            : null;

        var statusCode = exception.StatusCode;
        var body = exception.Content;

        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);
                var root = document.RootElement;

                var problemDetails = new ApiProblemDetails
                {
                    Status = root.TryGetProperty("status", out var statusProp) && statusProp.TryGetInt32(out var parsedStatus)
                        ? parsedStatus
                        : (int)exception.StatusCode,
                    Title = root.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString() ?? exception.Message
                        : root.TryGetProperty("error", out var errorProp)
                            ? errorProp.GetString() ?? exception.Message
                            : exception.Message,
                    Detail = root.TryGetProperty("detail", out var detailProp)
                        ? detailProp.GetString()
                        : root.TryGetProperty("error", out var legacyErrorProp)
                            ? legacyErrorProp.GetString()
                            : exception.Content,
                    Type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null,
                    Instance = root.TryGetProperty("instance", out var instanceProp) ? instanceProp.GetString() : null
                };

                if (root.TryGetProperty("traceId", out var traceIdProp))
                {
                    traceId = traceIdProp.GetString() ?? traceId;
                }

                if (traceId is not null)
                {
                    problemDetails.Extensions["traceId"] = traceId;
                }

                return new ApiProblemException(statusCode, problemDetails, serviceName, ReadValidationErrors(root), traceId);
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        var fallbackMessage = !string.IsNullOrWhiteSpace(exception.Content)
            ? exception.Content
            : exception.Message;

        return CreateFallback(statusCode, fallbackMessage, serviceName, traceId);
    }

    private static IReadOnlyDictionary<string, string[]> ReadValidationErrors(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errorsProp) || errorsProp.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return new Dictionary<string, string[]>();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in errorsProp.EnumerateObject())
        {
            if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                errors[property.Name] = property.Value
                    .EnumerateArray()
                    .Select(static item => item.GetString() ?? string.Empty)
                    .Where(static message => !string.IsNullOrWhiteSpace(message))
                    .ToArray();
            }
        }

        return errors;
    }

    private static ApiProblemException CreateFallback(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string serviceName,
        string? traceId)
    {
        return new ApiProblemException(
            statusCode,
            reasonPhrase ?? statusCode.ToString(),
            serviceName: serviceName,
            traceId: traceId);
    }

    private static string FormatMessage(HttpStatusCode statusCode, string serviceName, string? title, string? detail)
    {
        return string.IsNullOrWhiteSpace(detail)
            ? $"{serviceName} returned {(int)statusCode}: {title}"
            : $"{serviceName} returned {(int)statusCode}: {title} — {detail}";
    }
}

public sealed class ApiProblemDetails
{
    public int? Status { get; set; }
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public string? Type { get; set; }
    public string? Instance { get; set; }
    public IDictionary<string, object?> Extensions { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}
