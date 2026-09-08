using Explore.Application.DTOs.EventSessionTemplateSync;

namespace Explore.API.Models;

public sealed record EventSessionTemplateSyncApplyRequest(
    TemplateSyncPlanDto Plan,
    int BaseProvenanceVersion);
