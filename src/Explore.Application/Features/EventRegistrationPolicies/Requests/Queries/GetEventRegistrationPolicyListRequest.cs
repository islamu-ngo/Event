using Explore.Application.DTOs.EventRegistrationPolicy;
using MediatR;

namespace Explore.Application.Features.EventRegistrationPolicies.Requests.Queries;

public sealed record GetEventRegistrationPolicyListRequest : IRequest<List<EventRegistrationPolicyListDto>>
{
}
