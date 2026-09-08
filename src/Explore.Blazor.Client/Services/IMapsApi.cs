using Refit;

namespace Explore.Blazor.Client.Services;

public interface IMapsApi
{
    [Get("/bff/api/Maps/embed-url")]
    Task<IApiResponse<string>> GetEmbedUrlAsync([AliasAs("query")] string query, CancellationToken cancellationToken);
}
