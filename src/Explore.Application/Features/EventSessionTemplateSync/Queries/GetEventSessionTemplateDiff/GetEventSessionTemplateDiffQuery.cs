using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;

[AuthorizeResource(ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.SyncDiff)]
public sealed record GetEventSessionTemplateDiffQuery(
    Guid EventSessionId,
    int TargetTemplateVersion
) : IQuery<BaseCommandResponse<TemplateDiffDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventSessionId.ToString();
}
