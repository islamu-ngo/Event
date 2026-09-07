using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;

namespace Explore.Blazor.Client.Services.Lookup;

public class RegistrationScopeService : IRegistrationScopeService
{
    private readonly IRegistrationScopeClient _client;

    public RegistrationScopeService(IRegistrationScopeClient client)
    {
        _client = client;
    }

    public async Task<ICollection<RegistrationScopeListDto>> GetRegistrationScopesAsync()
    {
        return await _client.GetRegistrationScopesAsync();
    }
}
