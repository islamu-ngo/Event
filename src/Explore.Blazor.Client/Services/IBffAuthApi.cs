using Refit;

namespace Explore.Blazor.Client.Services;

public interface IBffAuthApi
{
    [Get("/auth/providers")]
    Task<IApiResponse<BffAuthProvidersResponse>> GetProvidersAsync(CancellationToken cancellationToken);

    [Post("/bff/auth/refresh-schemes")]
    Task<IApiResponse> RefreshSchemesAsync(CancellationToken cancellationToken);

    [Post("/bff/auth/refresh-session/internal")]
    Task<IApiResponse> RefreshSessionInternalAsync(CancellationToken cancellationToken);

    [Delete("/bff/setup-secret")]
    Task<IApiResponse> DeleteSetupSecretAsync(CancellationToken cancellationToken);
}

public sealed class BffAuthProvidersResponse
{
    public string PrimaryProvider { get; set; } = string.Empty;
    public bool AtprotoLoginEnabled { get; set; }
    public List<BffAuthProviderItem> Providers { get; set; } = [];
    public Explore.Blazor.Client.Clients.VisitorAccessCapabilityDto? VisitorAccess { get; set; }
    public Dictionary<string, Explore.Blazor.Client.Clients.HalLink>? _links { get; set; }
}

public sealed class BffAuthProviderItem
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Recommended { get; set; }
}
