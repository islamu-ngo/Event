using Explore.Application.Authorization;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCustomProperties.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record CreateEventCustomPropertyDefinitionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventCustomPropertyDefinitionDto DefinitionDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
