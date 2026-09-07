namespace Explore.API.Hateoas.Resources;

public sealed record EventTemplateSyncResource(
    Guid TenantId,
    Guid EventId,
    int TargetTemplateVersion,
    bool HasChanges);
