using Explore.Application.DTOs.EventRegistrationPolicy;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventRegistrationPolicies.Requests.Queries;

public sealed record GetEventRegistrationPolicyListRequest : IQuery<List<EventRegistrationPolicyListDto>>
{
}
