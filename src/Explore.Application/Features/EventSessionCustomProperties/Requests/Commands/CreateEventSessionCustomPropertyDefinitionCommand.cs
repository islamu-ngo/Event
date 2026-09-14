using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record CreateEventSessionCustomPropertyDefinitionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventSessionCustomPropertyDefinitionDto DefinitionDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
