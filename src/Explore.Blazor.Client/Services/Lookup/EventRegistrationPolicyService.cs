using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;

namespace Explore.Blazor.Client.Services.Lookup;

public class EventRegistrationPolicyService : IEventRegistrationPolicyService
{
    private readonly IEventRegistrationPolicyClient _client;

    public EventRegistrationPolicyService(IEventRegistrationPolicyClient client)
    {
        _client = client;
    }

    public async Task<ICollection<EventRegistrationPolicyListDto>> GetEventRegistrationPoliciesAsync()
    {
        return await _client.GetEventRegistrationPoliciesAsync();
    }
}
