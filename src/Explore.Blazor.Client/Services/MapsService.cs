namespace Explore.Blazor.Client.Services;

public interface IMapsService
{
    Task<string> GetEmbedUrlAsync(string query);
}

public class MapsService : IMapsService
{
    private readonly IMapsApi _api;
    private readonly ILogger<MapsService> _logger;

    public MapsService(IMapsApi api, ILogger<MapsService> logger)
    {
        _api = api;
        _logger = logger;
    }

    public async Task<string> GetEmbedUrlAsync(string query)
    {
        try
        {
            if (string.IsNullOrEmpty(query))
            {
                return string.Empty;
            }

            var response = await _api.GetEmbedUrlAsync(query, CancellationToken.None);

            if (response.IsSuccessStatusCode && response.Content is not null)
            {
                return response.Content.Trim('"');
            }

            _logger.LogWarning("Error getting map embed URL: {StatusCode}", (int)response.StatusCode);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetEmbedUrlAsync");
            return string.Empty;
        }
    }
}
