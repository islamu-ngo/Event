using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTemplates.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record CreateEventTemplateCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventTemplateDto TemplateDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
