using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Lookup;

public interface ICultureLookupService
{
    Task<ICollection<LanguageListDto>> GetLanguagesAsync();
    Task<ICollection<MadhabListDto>> GetMadhabsAsync();
}
