using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;

[AuthorizeResource(ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.SyncDiff)]
public sealed record GetEventSessionTemplateDiffQuery(
    Guid EventSessionId,
    int TargetTemplateVersion
) : IRequest<BaseCommandResponse<TemplateDiffDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventSessionId.ToString();
}
