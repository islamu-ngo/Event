using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionTemplateSync.Commands.ApplyEventSessionTemplateSync;

[AuthorizeResource(ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.SyncApply)]
public sealed record ApplyEventSessionTemplateSyncCommand(
    Guid EventSessionId,
    TemplateSyncPlanDto Plan,
    int BaseProvenanceVersion
) : ICommand<BaseCommandResponse<TemplateSyncOutcomeDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventSessionId.ToString();
}
