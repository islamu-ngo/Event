using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventTemplates.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record CreateEventTemplateCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventTemplateDto TemplateDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
