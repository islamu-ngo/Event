using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTemplateSync.Commands.ApplyEventTemplateSync;

[AuthorizeResource(ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.SyncApply)]
public sealed record ApplyEventTemplateSyncCommand(
    Guid EventId,
    TemplateSyncPlanDto Plan,
    int BaseProvenanceVersion
) : ICommand<BaseCommandResponse<TemplateSyncOutcomeDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId.ToString();
}
