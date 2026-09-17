using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTemplates.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record DeleteEventTemplateCommand : ICommand<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
