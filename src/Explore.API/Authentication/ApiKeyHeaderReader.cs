using Explore.Application.Constants;

namespace Explore.API.Authentication;

public static class ApiKeyHeaderReader
{
    public static bool HasNonEmptyApiKey(HttpRequest request, string headerName = ApiAuthenticationHeaderNames.ApiKey) =>
        GetFirstNonEmptyValue(request, headerName) is not null;

    public static string? GetFirstNonEmptyValue(HttpRequest request, string headerName = ApiAuthenticationHeaderNames.ApiKey)
    {
        if (!request.Headers.TryGetValue(headerName, out var values))
        {
            return null;
        }

        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }
}
