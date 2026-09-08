using Explore.Application.DTOs.EventTemplateSync;

namespace Explore.API.Models;

public sealed record EventTemplateSyncApplyRequest(
    TemplateSyncPlanDto Plan,
    int BaseProvenanceVersion);
