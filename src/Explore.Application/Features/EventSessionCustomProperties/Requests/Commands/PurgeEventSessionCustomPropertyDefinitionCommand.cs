using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record PurgeEventSessionCustomPropertyDefinitionCommand : ICommand<BaseCommandResponse<CustomPropertyPurgeResultDto>>, ISecureRequest
{
    public Guid Id { get; init; }
    public required string Reason { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
