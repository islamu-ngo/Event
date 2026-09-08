using Explore.Domain.Enums;

namespace Explore.API.Mcp;

public sealed record EventMcpSearchResultDescriptor(
    int PageNumber,
    int PageSize,
    bool PageSizeWasClamped,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<EventMcpSummaryDescriptor> Events);

public sealed record EventMcpMyEventsResultDescriptor(
    int PageNumber,
    int PageSize,
    bool PageSizeWasClamped,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<EventMcpSummaryDescriptor> Events);

public sealed record EventMcpCreationContextDescriptor(
    bool CanCreate,
    bool AllowPersonalPublishing,
    bool AllowOrganizationPublishing,
    bool AllowGroupPublishing,
    bool RequiresApproval,
    string? DefaultPublisherMode,
    string? UnavailableReason,
    int PublisherOptionCount,
    int ReturnedPublisherOptionCount,
    bool PublisherOptionsWereTruncated,
    IReadOnlyList<EventMcpCreationPublisherOptionDescriptor> PublisherOptions,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpCreationPublisherOptionDescriptor(
    string PublisherMode,
    Guid? PublisherId,
    string DisplayName,
    bool CanPublish,
    string? Reason);

public sealed record EventMcpManagementContextResultDescriptor(
    bool Found,
    Guid EventId,
    string? FailureCode,
    EventMcpManagementContextDescriptor? Context)
{
    public static EventMcpManagementContextResultDescriptor NotFound(Guid eventId)
        => new(
            Found: false,
            EventId: eventId,
            FailureCode: "not_found",
            Context: null);
}

public sealed record EventMcpManagementContextDescriptor(
    Guid EventId,
    Guid ConcurrencyStamp,
    string Title,
    string? Slug,
    string Status,
    string Visibility,
    string Format,
    bool PublishReadinessAvailable,
    EventMcpPublishReadinessDescriptor? PublishReadiness,
    IReadOnlyList<EventMcpManagementActionDescriptor> Actions,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpManagementActionDescriptor(
    string Rel,
    bool Available,
    string? Method,
    string? Href,
    string? Title);

public sealed record EventMcpPublishReadinessDescriptor(
    Guid EventId,
    bool IsReady,
    int ErrorCount,
    bool ErrorsWereTruncated,
    IReadOnlyList<EventMcpPublishReadinessIssueDescriptor> Errors);

public sealed record EventMcpPublishReadinessIssueDescriptor(
    string Code,
    string FieldPath,
    string Severity,
    string Message);

public sealed record EventMcpPublishReadinessResultDescriptor(
    bool Found,
    Guid EventId,
    bool Available,
    string? FailureCode,
    EventMcpPublishReadinessDescriptor? PublishReadiness,
    IReadOnlyList<string> TruncatedFields)
{
    public static EventMcpPublishReadinessResultDescriptor NotFound(Guid eventId)
        => new(
            Found: false,
            EventId: eventId,
            Available: false,
            FailureCode: "not_found",
            PublishReadiness: null,
            TruncatedFields: []);

    public static EventMcpPublishReadinessResultDescriptor Unavailable(Guid eventId)
        => new(
            Found: true,
            EventId: eventId,
            Available: false,
            FailureCode: "not_available",
            PublishReadiness: null,
            TruncatedFields: []);
}

public sealed record EventMcpProgramManagementResultDescriptor(
    bool Found,
    Guid EventId,
    bool Available,
    string? FailureCode,
    EventMcpProgramManagementContextDescriptor? Context)
{
    public static EventMcpProgramManagementResultDescriptor NotFound(Guid eventId)
        => new(false, eventId, false, "not_found", null);

    public static EventMcpProgramManagementResultDescriptor Unavailable(Guid eventId)
        => new(true, eventId, false, "not_available", null);
}

public sealed record EventMcpProgramManagementContextDescriptor(
    Guid EventId,
    Guid ConcurrencyStamp,
    int SessionCount,
    int ReturnedSessionCount,
    bool SessionsWereTruncated,
    IReadOnlyList<EventMcpSessionSummaryDescriptor> Sessions,
    int SessionGroupCount,
    int ReturnedSessionGroupCount,
    bool SessionGroupsWereTruncated,
    IReadOnlyList<EventMcpSessionGroupDescriptor> SessionGroups,
    int DayCount,
    int ReturnedDayCount,
    bool DaysWereTruncated,
    IReadOnlyList<EventMcpDayDescriptor> Days,
    int AgendaItemCount,
    int ReturnedAgendaItemCount,
    bool AgendaItemsWereTruncated,
    IReadOnlyList<EventMcpAgendaItemDescriptor> AgendaItems,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpSessionGroupDescriptor(
    Guid SessionGroupId,
    Guid EventId,
    string Name,
    string? Slug,
    string? Color,
    int SortOrder,
    bool IsPublished);

public sealed record EventMcpDayDescriptor(
    Guid DayId,
    Guid EventId,
    DateOnly LocalDate,
    string? Label,
    int SortOrder,
    bool IsPublished,
    bool AllowsDayScopeRegistration);

public sealed record EventMcpAgendaItemDescriptor(
    Guid AgendaItemId,
    Guid EventId,
    string Title,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateOnly LocalStartDate,
    TimeOnly LocalStartTime,
    TimeOnly LocalEndTime,
    int? KindId,
    string? KindName,
    int SortOrder);

public sealed record EventMcpCustomPropertiesResultDescriptor(
    bool Found,
    Guid EventId,
    bool Available,
    string? FailureCode,
    EventMcpCustomPropertiesContextDescriptor? Context)
{
    public static EventMcpCustomPropertiesResultDescriptor NotFound(Guid eventId)
        => new(false, eventId, false, "not_found", null);

    public static EventMcpCustomPropertiesResultDescriptor Unavailable(Guid eventId)
        => new(true, eventId, false, "not_available", null);
}

public sealed record EventMcpCustomPropertiesContextDescriptor(
    Guid EventId,
    Guid ConcurrencyStamp,
    int PageNumber,
    int PageSize,
    bool PageSizeWasClamped,
    int TotalDefinitionCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    int ReturnedDefinitionCount,
    IReadOnlyList<EventMcpCustomPropertyDefinitionDescriptor> Definitions,
    int ValueCount,
    int ReturnedValueCount,
    bool ValuesWereTruncated,
    IReadOnlyList<EventMcpCustomPropertyValueDescriptor> Values,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpCustomPropertyDefinitionDescriptor(
    Guid DefinitionId,
    Guid EventId,
    string Namespace,
    string Key,
    string DisplayName,
    string PropertyType,
    bool IsRequired,
    bool IsActive,
    int SortOrder,
    string ExposureLevel,
    bool IsTemplateBacked,
    int OptionCount);

public sealed record EventMcpCustomPropertyValueDescriptor(
    Guid ValueId,
    Guid DefinitionId,
    Guid EventId,
    int Ordinal,
    string ValueType,
    string? Value);

public sealed record EventMcpRegistrationsContextResultDescriptor(
    bool Found,
    Guid EventId,
    bool Available,
    string? FailureCode,
    EventMcpRegistrationsContextDescriptor? Context)
{
    public static EventMcpRegistrationsContextResultDescriptor NotFound(Guid eventId)
        => new(false, eventId, false, "not_found", null);

    public static EventMcpRegistrationsContextResultDescriptor Unavailable(Guid eventId)
        => new(true, eventId, false, "not_available", null);
}

public sealed record EventMcpRegistrationsContextDescriptor(
    Guid EventId,
    Guid ConcurrencyStamp,
    int PageNumber,
    int PageSize,
    bool PageSizeWasClamped,
    int TotalRegistrationCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<EventMcpRegistrationDescriptor> Registrations,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpRegistrationDescriptor(
    Guid RegistrationOrderId,
    Guid EventId,
    int StatusId,
    string? StatusCode,
    string? StatusName,
    string? CurrencyCode,
    long TotalDueMinor,
    DateTime? ExpiresAt);

public sealed record EventMcpTeamContextResultDescriptor(
    bool Found,
    Guid EventId,
    bool Available,
    string? FailureCode,
    EventMcpTeamContextDescriptor? Context)
{
    public static EventMcpTeamContextResultDescriptor NotFound(Guid eventId)
        => new(false, eventId, false, "not_found", null);

    public static EventMcpTeamContextResultDescriptor Unavailable(Guid eventId)
        => new(true, eventId, false, "not_available", null);
}

public sealed record EventMcpTeamContextDescriptor(
    Guid EventId,
    Guid ConcurrencyStamp,
    bool IncludeInactive,
    EventMcpCurrentUserPermissionsDescriptor CurrentUserPermissions,
    int TeamMemberCount,
    int ReturnedTeamMemberCount,
    bool TeamMembersWereTruncated,
    IReadOnlyList<EventMcpTeamMemberDescriptor> TeamMembers,
    int AssignableRolePresetCount,
    int ReturnedAssignableRolePresetCount,
    bool AssignableRolePresetsWereTruncated,
    IReadOnlyList<EventMcpRolePresetDescriptor> AssignableRolePresets,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpCurrentUserPermissionsDescriptor(
    Guid EventId,
    bool HasAnyRole,
    bool IsOwner,
    bool IsManager,
    IReadOnlyList<string> RoleCodes,
    IReadOnlyList<string> PermissionCodes);

public sealed record EventMcpTeamMemberDescriptor(
    Guid AssignmentId,
    string UserEmail,
    string UserFullName,
    string RoleName,
    string RoleMasterCode,
    string Status,
    DateTime StartsAtUtc,
    DateTime? ExpiresAtUtc,
    bool IsEffective);

public sealed record EventMcpRolePresetDescriptor(
    int RoleId,
    string MasterCode,
    string FullName,
    string? Description,
    IReadOnlyList<string> PermissionCodes);

public sealed record EventMcpTemplateCatalogResultDescriptor(
    bool Found,
    Guid EventId,
    bool Available,
    string? FailureCode,
    EventMcpTemplateCatalogContextDescriptor? Context)
{
    public static EventMcpTemplateCatalogResultDescriptor NotFound(Guid eventId)
        => new(false, eventId, false, "not_found", null);

    public static EventMcpTemplateCatalogResultDescriptor Unavailable(Guid eventId)
        => new(true, eventId, false, "not_available", null);
}

public sealed record EventMcpTemplateCatalogContextDescriptor(
    Guid EventId,
    Guid ConcurrencyStamp,
    int? EventTypeId,
    int PageNumber,
    int PageSize,
    bool PageSizeWasClamped,
    int TotalTemplateCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<EventMcpTemplateDescriptor> Templates,
    EventMcpSessionTemplateCatalogPageDescriptor? SessionTemplates,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpTemplateDescriptor(
    Guid TemplateId,
    string TemplateKey,
    string DisplayName,
    string? Description,
    int? EventTypeId,
    int Version,
    bool IsPublished,
    bool IsActive,
    int SortOrder,
    int DefinitionCount);

public sealed record EventMcpSessionTemplateCatalogPageDescriptor(
    Guid EventTemplateId,
    int PageNumber,
    int PageSize,
    bool PageSizeWasClamped,
    int TotalSessionTemplateCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<EventMcpSessionTemplateDescriptor> SessionTemplates);

public sealed record EventMcpSessionTemplateDescriptor(
    Guid SessionTemplateId,
    Guid EventTemplateId,
    string SessionTemplateKey,
    string DisplayName,
    string? Description,
    int Version,
    bool IsPublished,
    bool IsActive,
    int SortOrder,
    int DefinitionCount,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpTemplateSyncContextResultDescriptor(
    bool Found,
    Guid EventId,
    bool Available,
    string? FailureCode,
    EventMcpTemplateSyncContextDescriptor? Context)
{
    public static EventMcpTemplateSyncContextResultDescriptor NotFound(Guid eventId)
        => new(false, eventId, false, "not_found", null);

    public static EventMcpTemplateSyncContextResultDescriptor Unavailable(Guid eventId)
        => new(true, eventId, false, "not_available", null);
}

public sealed record EventMcpTemplateSyncContextDescriptor(
    Guid EventId,
    Guid ConcurrencyStamp,
    int? RequestedTargetTemplateVersion,
    bool DiffAvailable,
    string? DiffFailureCode,
    EventMcpTemplateDiffDescriptor? Diff,
    EventMcpTemplateSyncHistoryPageDescriptor History,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpSessionTemplateSyncContextResultDescriptor(
    bool Found,
    Guid EventId,
    Guid SessionId,
    bool Available,
    string? FailureCode,
    EventMcpSessionTemplateSyncContextDescriptor? Context)
{
    public static EventMcpSessionTemplateSyncContextResultDescriptor NotFound(Guid eventId, Guid sessionId)
        => new(false, eventId, sessionId, false, "not_found", null);

    public static EventMcpSessionTemplateSyncContextResultDescriptor Unavailable(Guid eventId, Guid sessionId)
        => new(true, eventId, sessionId, false, "not_available", null);
}

public sealed record EventMcpSessionTemplateSyncContextDescriptor(
    Guid EventId,
    Guid SessionId,
    Guid EventConcurrencyStamp,
    int? RequestedTargetTemplateVersion,
    bool DiffAvailable,
    string? DiffFailureCode,
    EventMcpTemplateDiffDescriptor? Diff,
    EventMcpTemplateSyncHistoryPageDescriptor History,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpTemplateDiffDescriptor(
    int TargetTemplateVersion,
    int BaseProvenanceVersion,
    int ChangeCount,
    IReadOnlyList<string> AddedDefinitionKeys,
    IReadOnlyList<string> ModifiedDefinitionKeys,
    IReadOnlyList<string> RetiredDefinitionKeys,
    IReadOnlyList<string> AddedOptionKeys,
    IReadOnlyList<string> ModifiedOptionKeys,
    IReadOnlyList<string> RetiredOptionKeys,
    IReadOnlyList<string> UntouchedLocalDefinitionKeys,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpTemplateSyncHistoryPageDescriptor(
    int PageNumber,
    int PageSize,
    bool PageSizeWasClamped,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<EventMcpTemplateSyncHistoryItemDescriptor> Items);

public sealed record EventMcpTemplateSyncHistoryItemDescriptor(
    int BaseProvenanceVersion,
    int TargetTemplateVersion,
    int AppliedCount,
    IReadOnlyList<string> Applied,
    int SkippedCount,
    IReadOnlyList<string> Skipped,
    int ConflictCount,
    IReadOnlyList<EventMcpTemplateSyncConflictDescriptor> Conflicts,
    DateTimeOffset SyncedAt);

public sealed record EventMcpTemplateSyncConflictDescriptor(
    string Key,
    string Reason);

public sealed record EventMcpSummaryDescriptor(
    Guid EventId,
    string Title,
    string? Subtitle,
    string? Description,
    string? Slug,
    string EventType,
    string ActorDisplayName,
    string Status,
    string Visibility,
    string Format,
    DateOnly? FirstSessionDate,
    DateOnly? LastSessionDate,
    string? Timezone,
    int? SessionCount,
    string? ParticipationHandlingModeCode,
    string? ParticipationHandlingModeName,
    string? AdvanceRegistrationObligationCode,
    string? AdvanceRegistrationObligationName,
    string? IdentityAccessModeCode,
    string? IdentityAccessModeName,
    GuestRecoveryPolicyEnum? GuestRecoveryPolicy);

public sealed record EventMcpEventResultDescriptor(
    bool Found,
    Guid EventId,
    string? FailureCode,
    EventMcpDetailDescriptor? Event)
{
    public static EventMcpEventResultDescriptor NotFound(Guid eventId)
        => new(
            Found: false,
            EventId: eventId,
            FailureCode: "not_found",
            Event: null);
}

public sealed record EventMcpDetailDescriptor(
    Guid EventId,
    string Title,
    string? Subtitle,
    string? Description,
    string? Content,
    string? Slug,
    string? EventType,
    string ActorDisplayName,
    string Status,
    string Visibility,
    string Format,
    DateOnly? FirstSessionDate,
    DateOnly? LastSessionDate,
    string? Timezone,
    int? SessionCount,
    string? ParticipationHandlingModeCode,
    string? ParticipationHandlingModeName,
    string? AdvanceRegistrationObligationCode,
    string? AdvanceRegistrationObligationName,
    string? IdentityAccessModeCode,
    string? IdentityAccessModeName,
    GuestRecoveryPolicyEnum? GuestRecoveryPolicy,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> AvailableAspects,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpProgramResultDescriptor(
    bool Found,
    Guid EventId,
    string? FailureCode,
    EventMcpProgramSummaryDescriptor? Program)
{
    public static EventMcpProgramResultDescriptor NotFound(Guid eventId)
        => new(
            Found: false,
            EventId: eventId,
            FailureCode: "not_found",
            Program: null);
}

public sealed record EventMcpProgramSummaryDescriptor(
    Guid EventId,
    string EventTitle,
    string? TimeZoneId,
    int SectionCount,
    int ItemCount,
    int WarningCount,
    bool ProgramItemsWereTruncated,
    bool WarningsWereTruncated,
    IReadOnlyList<EventMcpProgramSectionDescriptor> Sections,
    IReadOnlyList<EventMcpReadinessWarningDescriptor> ReadinessWarnings,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpProgramSectionDescriptor(
    string SectionKey,
    string Title,
    int SortOrder,
    IReadOnlyList<EventMcpProgramSessionGroupDescriptor> SessionGroups);

public sealed record EventMcpProgramSessionGroupDescriptor(
    Guid? SessionGroupId,
    string Title,
    int SortOrder,
    string? Color,
    EventMcpLocationDescriptor? EventLocation,
    IReadOnlyList<EventMcpProgramDayDescriptor> Days);

public sealed record EventMcpProgramDayDescriptor(
    DateOnly LocalDate,
    string DisplayLabel,
    IReadOnlyList<EventMcpProgramItemDescriptor> Items);

public sealed record EventMcpProgramItemDescriptor(
    Guid SessionId,
    string Title,
    int? EventSessionKindId,
    string? EventSessionKindName,
    string? EventSessionKindMasterCode,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateOnly LocalDate,
    TimeOnly LocalStartTime,
    TimeOnly LocalEndTime,
    int SortOrder,
    Guid? SessionGroupId,
    EventMcpLocationDescriptor? EventLocation,
    int? Capacity,
    string? RegistrationModeName,
    IReadOnlyList<EventMcpReadinessWarningDescriptor> ReadinessWarnings);

public sealed record EventMcpReadinessWarningDescriptor(
    string Path,
    string Severity,
    string Message);

public sealed record EventMcpSessionListResultDescriptor(
    bool Found,
    Guid EventId,
    string? FailureCode,
    int TotalCount,
    int ReturnedCount,
    bool SessionsWereTruncated,
    IReadOnlyList<EventMcpSessionSummaryDescriptor> Sessions)
{
    public static EventMcpSessionListResultDescriptor NotFound(Guid eventId)
        => new(
            Found: false,
            EventId: eventId,
            FailureCode: "not_found",
            TotalCount: 0,
            ReturnedCount: 0,
            SessionsWereTruncated: false,
            Sessions: []);
}

public sealed record EventMcpSessionSummaryDescriptor(
    Guid SessionId,
    Guid EventId,
    string EventTitle,
    string? Title,
    string? Slug,
    int? EventSessionKindId,
    string? EventSessionKindName,
    string? EventSessionKindMasterCode,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    DateOnly? LocalStartDate,
    TimeOnly? LocalStartTime,
    TimeOnly? LocalEndTime,
    int SortOrder,
    int? Capacity,
    string? RegistrationMode,
    IReadOnlyList<string> SessionGroups,
    EventMcpLocationDescriptor? EventLocation,
    IReadOnlyList<string> TruncatedFields);

public sealed record EventMcpLocationDescriptor(
    Guid EventLocationId,
    string State,
    string? Country,
    string? Timezone,
    string? City,
    string? VenueName,
    string? RoomName,
    string? StreetAddress,
    string? Postcode,
    double? Latitude,
    double? Longitude,
    string? FormattedAddress,
    string? MapUrl,
    string? Geohash);
