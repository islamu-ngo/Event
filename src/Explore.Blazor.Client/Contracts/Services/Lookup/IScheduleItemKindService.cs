using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Lookup;

public interface IScheduleItemKindService
{
    Task<ICollection<ScheduleItemKindListDto>> GetScheduleItemKindsAsync();
}
