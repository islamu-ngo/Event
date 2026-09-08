using System.Net.Http.Json;
using System.Text.Json;
using Explore.Blazor.Client.Exceptions;

namespace Explore.Blazor.Client.Extensions;

/// <summary>
/// Provides extension methods that enforce status-code-first deserialization on <see cref="HttpResponseMessage"/>.
/// All API-calling code should use these instead of raw <c>ReadFromJsonAsync</c> or <c>GetFromJsonAsync</c>.
/// </summary>
public static class HttpResponseExtensions
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Reads the response body as <typeparamref name="T"/> if the status code indicates success (2xx).
    /// Throws <see cref="ApiProblemException"/> on non-success status codes.
    /// </summary>
    public static async Task<T> ReadJsonOrThrowAsync<T>(
        this HttpResponseMessage response,
        string serviceName = "HTTP API",
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<T>(DefaultJsonOptions, cancellationToken);
            return result ?? throw new InvalidOperationException(
                $"API returned {(int)response.StatusCode} but response body deserialized to null for type {typeof(T).Name}.");
        }

        throw await ApiProblemException.FromResponseAsync(response, serviceName, cancellationToken);
    }

    /// <summary>
    /// Reads the response body as <typeparamref name="T"/> if the status code indicates success (2xx).
    /// Returns <c>default</c> on non-success without throwing.
    /// </summary>
    public static async Task<T?> ReadJsonOrDefaultAsync<T>(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
            return default;

        return await response.Content.ReadFromJsonAsync<T>(DefaultJsonOptions, cancellationToken);
    }

    /// <summary>
    /// Ensures the response is successful (2xx). Throws <see cref="ApiProblemException"/> on failure.
    /// Unlike <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>, this preserves ProblemDetails.
    /// </summary>
    public static async Task EnsureSuccessOrThrowAsync(
        this HttpResponseMessage response,
        string serviceName = "HTTP API",
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
            throw await ApiProblemException.FromResponseAsync(response, serviceName, cancellationToken);
    }

    public static Task EnsureSuccessOrThrowProblem(
        this HttpResponseMessage response,
        string serviceName,
        CancellationToken cancellationToken = default)
        => response.EnsureSuccessOrThrowAsync(serviceName, cancellationToken);

    public static async Task<ApiProblemDetails?> ReadProblemDetailsAsync(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var exception = await ApiProblemException.FromResponseAsync(response, "HTTP API", cancellationToken);
        return exception.ProblemDetails;
    }
}
