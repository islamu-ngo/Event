using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventRegistrationPolicy;
using Explore.Application.Features.EventRegistrationPolicies.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventRegistrationPolicies.Handlers.Queries;

public class GetEventRegistrationPolicyListRequestHandler : IRequestHandler<GetEventRegistrationPolicyListRequest, List<EventRegistrationPolicyListDto>>
{
    private readonly IEventRegistrationPolicyRepository _eventRegistrationPolicyRepository;
    private readonly IMapper _mapper;

    public GetEventRegistrationPolicyListRequestHandler(IEventRegistrationPolicyRepository eventRegistrationPolicyRepository, IMapper mapper)
    {
        _eventRegistrationPolicyRepository = eventRegistrationPolicyRepository;
        _mapper = mapper;
    }

    public async Task<List<EventRegistrationPolicyListDto>> Handle(GetEventRegistrationPolicyListRequest request, CancellationToken cancellationToken)
    {
        var policies = await _eventRegistrationPolicyRepository.GetAll();
        return _mapper.Map<List<EventRegistrationPolicyListDto>>(policies);
    }
}
