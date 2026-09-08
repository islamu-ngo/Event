namespace Explore.API.Hateoas.Resources;

public sealed record EventSessionTemplateSyncResource(
    Guid TenantId,
    Guid SessionId,
    int TargetTemplateVersion,
    bool HasChanges);
