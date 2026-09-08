namespace Explore.Application.Features.AiAssistant.Tools;

internal static class EventAspectToolFieldPolicy
{
    public static AiToolScopeMetadata ScopeMetadata { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/events/{eventId}",
            "/events/detail",
            "/events/manage",
            "/calendar"
        },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "event-management",
            "event-aspects"
        },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "event",
            "event-management-context",
            "event-aspect-context"
        });

    public static IReadOnlySet<string> DestructiveAspectPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "eventId",
        "expectedConcurrencyStamp",
        "aspectKind",
        "managementContextHasEdit",
        "destructiveSummary",
        "confirmationPhrase",
        "acknowledgedConsequences"
    };

    public static IReadOnlySet<string> ForbiddenPayloadFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "tenantId",
        "actorId",
        "actor",
        "organizationId",
        "groupId",
        "eventStatusId",
        "status",
        "createdBy",
        "updatedBy",
        "createdAt",
        "updatedAt",
        "deletedBy",
        "deletedAt",
        "isDeleted",
        "publishedAt",
        "isPublished",
        "outboxMessages",
        "notificationFanout",
        "sessions",
        "sessionGroups",
        "agendaItems",
        "registrations",
        "roleAssignments",
        "concurrencyStamp"
    };
}
