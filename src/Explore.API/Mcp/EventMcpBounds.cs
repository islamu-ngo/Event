namespace Explore.API.Mcp;

/// <summary>
/// Every bound an MCP tool applies before serializing a response. They live together because they are one
/// contract, not thirty independent numbers: each caps how much of the platform a single AI-facing call can
/// observe, and a response trimmed by any of them must say so through its truncation indicators.
/// <para>
/// Raising one of these widens what an assistant can pull in a single turn, so changes belong in review
/// alongside the AI disclosure rules rather than being adjusted at a call site.
/// </para>
/// </summary>
internal static class EventMcpBounds
{
    public const int DefaultPublicEventPageSize = 10;
    public const int MaxPublicEventPageSize = 25;
    public const int DefaultMyEventsPageSize = 10;
    public const int MaxMyEventsPageSize = 25;
    public const int MaxSearchTermLength = 120;
    public const int MaxShortTextLength = 500;
    public const int MaxLongTextLength = 2_000;
    public const int MaxPublicProgramSections = 10;
    public const int MaxPublicProgramSessionGroups = 50;
    public const int MaxPublicProgramDays = 30;
    public const int MaxPublicProgramItems = 100;
    public const int MaxPublicSessions = 100;
    public const int MaxReadinessWarnings = 25;
    public const int MaxPublishReadinessErrors = 25;
    public const int MaxCreationPublisherOptions = 50;
    public const int DefaultManagementPageSize = 10;
    public const int MaxManagementPageSize = 25;
    public const int MaxManagedSessions = 100;
    public const int MaxManagedSessionGroups = 50;
    public const int MaxManagedDays = 30;
    public const int MaxManagedAgendaItems = 100;
    public const int MaxCustomPropertyDefinitions = 25;
    public const int MaxCustomPropertyValues = 100;
    public const int MaxManagedRegistrations = 100;
    public const int MaxTeamMembers = 50;
    public const int MaxAssignableRolePresets = 50;
    public const int MaxPermissionCodes = 100;
    public const int MaxTemplateCatalogItems = 25;
    public const int MaxSyncKeys = 50;
    public const int MaxSyncHistoryItems = 25;
}
