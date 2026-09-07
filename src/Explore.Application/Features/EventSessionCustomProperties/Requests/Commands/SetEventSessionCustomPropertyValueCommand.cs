using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record SetEventSessionCustomPropertyValueCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required SetEventSessionCustomPropertyValueDto ValueDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
