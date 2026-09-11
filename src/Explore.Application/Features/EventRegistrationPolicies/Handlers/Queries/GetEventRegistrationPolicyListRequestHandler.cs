using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventRegistrationPolicy;
using Explore.Application.Features.EventRegistrationPolicies.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventRegistrationPolicies.Handlers.Queries;

public class GetEventRegistrationPolicyListRequestHandler : IRequestHandler<GetEventRegistrationPolicyListRequest, List<EventRegistrationPolicyListDto>>
{
    private readonly IEventRegistrationPolicyRepository _eventRegistrationPolicyRepository;

    public GetEventRegistrationPolicyListRequestHandler(IEventRegistrationPolicyRepository eventRegistrationPolicyRepository)
    {
        _eventRegistrationPolicyRepository = eventRegistrationPolicyRepository;
    }

    public async Task<List<EventRegistrationPolicyListDto>> Handle(GetEventRegistrationPolicyListRequest request, CancellationToken cancellationToken)
    {
        var policies = await _eventRegistrationPolicyRepository.GetAll();
        return policies.Select(RegistrationMapper.ToListItem).ToList();
    }
}
