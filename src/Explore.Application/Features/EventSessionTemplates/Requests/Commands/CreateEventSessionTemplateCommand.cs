using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionTemplates.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record CreateEventSessionTemplateCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventSessionTemplateDto SessionTemplateDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
