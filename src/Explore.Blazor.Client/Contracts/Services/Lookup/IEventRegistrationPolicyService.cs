using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Lookup;

public interface IEventRegistrationPolicyService
{
    Task<ICollection<EventRegistrationPolicyListDto>> GetEventRegistrationPoliciesAsync();
}
