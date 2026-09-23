using Explore.Domain.Constants;

namespace Explore.Domain.Settings.Definitions;

public static class EventResourceSettingDefinitions
{
    public const string Category = "EventResources";

    public static readonly SettingDefinition EnabledDeliveryTypes = Coordinated(
        GovernanceSettingKeys.EventResources.EnabledDeliveryTypes,
        SettingValueType.Json,
        "[\"StoredFile\",\"ExternalLink\"]",
        "Enabled event-resource delivery types.");

    public static readonly SettingDefinition EnabledAudiences = Coordinated(
        GovernanceSettingKeys.EventResources.EnabledAudiences,
        SettingValueType.Json,
        "[\"Public\",\"AuthenticatedTenantMember\",\"SessionRegistrant\",\"TicketHolder\",\"CheckedInParticipant\",\"AnyEventSessionSpeaker\",\"SessionSpeaker\",\"EventStaff\",\"Organizer\"]",
        "Enabled event-resource audience kinds.");

    public static readonly SettingDefinition PermittedFileTypes = Coordinated(
        GovernanceSettingKeys.EventResources.PermittedFileTypes,
        SettingValueType.Json,
        "[\"application/pdf\",\"application/vnd.openxmlformats-officedocument.wordprocessingml.document\",\"application/vnd.openxmlformats-officedocument.presentationml.presentation\"]",
        "Permitted MIME types for stored event-resource files.");

    public static readonly SettingDefinition MaxUploadBytes = Coordinated(
        GovernanceSettingKeys.EventResources.MaxUploadBytes,
        SettingValueType.Long,
        "10485760",
        "Maximum event-resource upload size in bytes, further bounded by storage ceilings.");

    public static readonly SettingDefinition AllowUnscannedDocuments = Coordinated(
        GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
        SettingValueType.Boolean,
        "false",
        "Whether unscanned documents may be published and accessed.",
        SettingScope.Instance);

    public static readonly SettingDefinition ExternalOrigins = Coordinated(
        GovernanceSettingKeys.EventResources.ExternalOrigins,
        SettingValueType.Json,
        "[]",
        "Explicitly allowed canonical HTTPS origins for external event resources.");

    public static readonly SettingDefinition AuditRetentionDays = Coordinated(
        GovernanceSettingKeys.EventResources.AuditRetentionDays,
        SettingValueType.Integer,
        "30",
        "Event-resource management audit retention in days, from zero through 90.");

    public static readonly SettingDefinition MaxActiveResources = Coordinated(
        GovernanceSettingKeys.EventResources.MaxActiveResources,
        SettingValueType.Integer,
        "500",
        "Maximum active event resources per event, from zero through 500.");

    public static IReadOnlyList<SettingDefinition> All =>
    [
        EnabledDeliveryTypes,
        EnabledAudiences,
        PermittedFileTypes,
        MaxUploadBytes,
        AllowUnscannedDocuments,
        ExternalOrigins,
        AuditRetentionDays,
        MaxActiveResources
    ];

    private static SettingDefinition Coordinated(
        string key,
        SettingValueType valueType,
        string defaultValue,
        string description,
        SettingScope maxScope = SettingScope.Tenant) =>
        new(
            Key: key,
            ValueType: valueType,
            DefaultValue: defaultValue,
            Category: Category,
            Description: description,
            MinScope: SettingScope.Instance,
            MaxScope: maxScope,
            IsLockable: true)
        {
            RequiresCoordinatedMutation = true
        };
}
