using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventRegistrationPolicy;
using Explore.Application.Features.EventRegistrationPolicies.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventRegistrationPolicies.Handlers.Queries;

public class GetEventRegistrationPolicyListRequestHandler : IQueryHandler<GetEventRegistrationPolicyListRequest, List<EventRegistrationPolicyListDto>>
{
    private readonly IEventRegistrationPolicyRepository _eventRegistrationPolicyRepository;

    public GetEventRegistrationPolicyListRequestHandler(IEventRegistrationPolicyRepository eventRegistrationPolicyRepository)
    {
        _eventRegistrationPolicyRepository = eventRegistrationPolicyRepository;
    }

    public async Task<List<EventRegistrationPolicyListDto>> QueryAsync(GetEventRegistrationPolicyListRequest request, CancellationToken cancellationToken)
    {
        var policies = await _eventRegistrationPolicyRepository.GetAll();
        return policies.Select(RegistrationMapper.ToListItem).ToList();
    }
}
