using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record CreateCustomPropertyDefinitionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateCustomPropertyDefinitionDto DefinitionDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
