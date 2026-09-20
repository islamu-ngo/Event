using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record PurgeCustomPropertyDefinitionCommand : ICommand<BaseCommandResponse<CustomPropertyPurgeResultDto>>, ISecureRequest
{
    public Guid Id { get; init; }
    public required string Reason { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
