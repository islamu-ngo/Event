using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record DeleteCustomPropertyDefinitionCommand : IRequest<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
