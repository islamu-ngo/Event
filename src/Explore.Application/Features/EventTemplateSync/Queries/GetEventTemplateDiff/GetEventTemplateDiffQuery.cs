using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;

[AuthorizeResource(ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.SyncDiff)]
public sealed record GetEventTemplateDiffQuery(
    Guid EventId,
    int TargetTemplateVersion
) : IQuery<BaseCommandResponse<TemplateDiffDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString();
}
