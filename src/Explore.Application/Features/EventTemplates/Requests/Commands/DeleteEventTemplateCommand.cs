using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.EventTemplates.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record DeleteEventTemplateCommand : IRequest<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
