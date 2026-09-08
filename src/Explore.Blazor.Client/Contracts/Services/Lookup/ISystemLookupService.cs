using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Lookup;

public interface ISystemLookupService
{
    Task<ICollection<FileTypeListDto>> GetFileTypesAsync();
    Task<ICollection<DidCustodyTypeListDto>> GetDidCustodyTypesAsync();
}
