using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;

namespace Explore.Blazor.Client.Services.Lookup;

public class ScheduleItemKindService : IScheduleItemKindService
{
    private readonly IScheduleItemKindClient _client;

    public ScheduleItemKindService(IScheduleItemKindClient client)
    {
        _client = client;
    }

    public async Task<ICollection<ScheduleItemKindListDto>> GetScheduleItemKindsAsync()
    {
        return await _client.GetScheduleItemKindsAsync();
    }
}
