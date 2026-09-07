using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record CreateCustomPropertyDefinitionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateCustomPropertyDefinitionDto DefinitionDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
