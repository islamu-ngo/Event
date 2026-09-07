using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Lookup;

public interface IRegistrationScopeService
{
    Task<ICollection<RegistrationScopeListDto>> GetRegistrationScopesAsync();
}
