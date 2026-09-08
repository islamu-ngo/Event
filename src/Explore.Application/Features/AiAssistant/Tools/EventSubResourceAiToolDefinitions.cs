using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class EventSubResourceAiToolDefinitions
{
    private const string Uuid = """{ "type": "string", "format": "uuid" }""";
    private const string NullableUuid = """{ "type": ["string", "null"], "format": "uuid" }""";
    private const string ShortText = """{ "type": "string", "maxLength": 500 }""";
    private const string LongText = """{ "type": ["string", "null"], "maxLength": 5000 }""";
    private const string NullableShortText = """{ "type": ["string", "null"], "maxLength": 500 }""";
    private const string RequiredEmail = """{ "type": "string", "maxLength": 320 }""";
    private const string Date = """{ "type": "string", "format": "date" }""";
    private const string DateTime = """{ "type": "string", "format": "date-time" }""";
    private const string NullableDateTime = """{ "type": ["string", "null"], "format": "date-time" }""";
    private const string Integer = """{ "type": "integer" }""";
    private const string Number = """{ "type": "number", "minimum": 0 }""";
    private const string Boolean = """{ "type": "boolean" }""";
    private const string TrueBoolean = """{ "type": "boolean", "enum": [true] }""";
    private const string StringArray = """{ "type": "array", "items": { "type": "string", "maxLength": 500 } }""";
    private const string UuidArray = """{ "type": "array", "items": { "type": "string", "format": "uuid" } }""";
    private const string SyncPlan = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["targetTemplateVersion", "baseProvenanceVersion"],
          "properties": {
            "targetTemplateVersion": { "type": "integer" },
            "baseProvenanceVersion": { "type": "integer" },
            "addedDefinitionKeys": { "type": "array", "items": { "type": "string", "maxLength": 500 } },
            "modifiedDefinitionKeys": { "type": "array", "items": { "type": "string", "maxLength": 500 } },
            "retiredDefinitionKeys": { "type": "array", "items": { "type": "string", "maxLength": 500 } },
            "addedOptionKeys": { "type": "array", "items": { "type": "string", "maxLength": 500 } },
            "modifiedOptionKeys": { "type": "array", "items": { "type": "string", "maxLength": 500 } },
            "retiredOptionKeys": { "type": "array", "items": { "type": "string", "maxLength": 500 } }
          }
        }
        """;

    private static readonly IReadOnlyDictionary<string, string> EventSessionFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasAddSession"] = TrueBoolean,
        ["managementContextHasEdit"] = TrueBoolean,
        ["sessionId"] = Uuid,
        ["title"] = ShortText,
        ["startTime"] = DateTime,
        ["endTime"] = DateTime,
        ["locationId"] = NullableUuid,
        ["roomId"] = NullableUuid,
        ["featuredImageId"] = NullableUuid,
        ["sortOrder"] = Integer,
        ["eventSessionKindId"] = """{ "type": ["integer", "null"] }""",
        ["description"] = LongText,
        ["slug"] = NullableShortText,
        ["maxAudienceAttendees"] = """{ "type": ["integer", "null"] }""",
        ["registrationModeId"] = """{ "type": ["integer", "null"] }""",
        ["sessionTemplateId"] = NullableUuid
    };

    private static readonly IReadOnlyDictionary<string, string> EventSessionGroupFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasEdit"] = TrueBoolean,
        ["groupId"] = Uuid,
        ["sessionId"] = Uuid,
        ["name"] = ShortText,
        ["slug"] = NullableShortText,
        ["description"] = LongText,
        ["locationId"] = NullableUuid,
        ["roomId"] = NullableUuid,
        ["color"] = NullableShortText,
        ["sortOrder"] = Integer,
        ["isPublished"] = Boolean,
        ["isPrimary"] = Boolean
    };

    private static readonly IReadOnlyDictionary<string, string> EventDayFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasEdit"] = TrueBoolean,
        ["dayId"] = Uuid,
        ["localDate"] = Date,
        ["label"] = NullableShortText,
        ["description"] = LongText,
        ["bannerText"] = NullableShortText,
        ["bannerImageId"] = NullableUuid,
        ["isPublished"] = Boolean,
        ["sortOrder"] = Integer,
        ["allowsDayScopeRegistration"] = Boolean
    };

    private static readonly IReadOnlyDictionary<string, string> EventAgendaItemFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasEdit"] = TrueBoolean,
        ["agendaItemId"] = Uuid,
        ["title"] = ShortText,
        ["description"] = LongText,
        ["startTime"] = DateTime,
        ["endTime"] = DateTime,
        ["locationId"] = NullableUuid,
        ["roomId"] = NullableUuid,
        ["kindId"] = """{ "type": ["integer", "null"] }""",
        ["sortOrder"] = Integer
    };

    private static readonly IReadOnlyDictionary<string, string> CustomPropertyDefinitionFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["definitionId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasEdit"] = TrueBoolean,
        ["namespace"] = ShortText,
        ["key"] = ShortText,
        ["displayName"] = ShortText,
        ["description"] = LongText,
        ["propertyType"] = Integer,
        ["isRequired"] = Boolean,
        ["isMulti"] = Boolean,
        ["isActive"] = Boolean,
        ["sortOrder"] = Integer,
        ["exposureLevel"] = Integer,
        ["isSearchable"] = Boolean,
        ["isFilterable"] = Boolean,
        ["isExportable"] = Boolean,
        ["isModerationRelevant"] = Boolean,
        ["isAnalyticsRelevant"] = Boolean,
        ["defaultTextValue"] = NullableShortText,
        ["defaultNumberValue"] = """{ "type": ["number", "null"] }""",
        ["defaultBooleanValue"] = """{ "type": ["boolean", "null"] }""",
        ["defaultDateTimeValue"] = NullableDateTime,
        ["defaultOptionId"] = NullableUuid,
        ["minLength"] = """{ "type": ["integer", "null"] }""",
        ["maxLength"] = """{ "type": ["integer", "null"] }""",
        ["regexPattern"] = NullableShortText,
        ["minNumber"] = """{ "type": ["number", "null"] }""",
        ["maxNumber"] = """{ "type": ["number", "null"] }""",
        ["minDateTime"] = NullableDateTime,
        ["maxDateTime"] = NullableDateTime,
        ["allowedUrlSchemes"] = NullableShortText
    };

    private static readonly IReadOnlyDictionary<string, string> CustomPropertyValueFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["definitionId"] = Uuid,
        ["eventCustomPropertyDefinitionId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasEdit"] = TrueBoolean,
        ["ordinal"] = Integer,
        ["textValue"] = LongText,
        ["numberValue"] = """{ "type": ["number", "null"] }""",
        ["booleanValue"] = """{ "type": ["boolean", "null"] }""",
        ["dateTimeValue"] = NullableDateTime,
        ["optionId"] = NullableUuid,
        ["values"] = """
            {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["eventCustomPropertyDefinitionId", "eventId", "ordinal"],
                "properties": {
                  "eventCustomPropertyDefinitionId": { "type": "string", "format": "uuid" },
                  "eventId": { "type": "string", "format": "uuid" },
                  "ordinal": { "type": "integer" },
                  "textValue": { "type": ["string", "null"], "maxLength": 5000 },
                  "numberValue": { "type": ["number", "null"] },
                  "booleanValue": { "type": ["boolean", "null"] },
                  "dateTimeValue": { "type": ["string", "null"], "format": "date-time" },
                  "optionId": { "type": ["string", "null"], "format": "uuid" }
                }
              }
            }
            """
    };

    private static readonly IReadOnlyDictionary<string, string> EventTeamFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["assignmentId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasManageTeam"] = TrueBoolean,
        ["targetUserEmail"] = RequiredEmail,
        ["roleId"] = Integer,
        ["startsAtUtc"] = DateTime,
        ["expiresAtUtc"] = NullableDateTime
    };

    private static readonly IReadOnlyDictionary<string, string> EventTemplateFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["templateId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasEdit"] = TrueBoolean,
        ["templateKey"] = ShortText,
        ["displayName"] = ShortText,
        ["description"] = LongText,
        ["eventTypeId"] = """{ "type": ["integer", "null"] }""",
        ["version"] = Integer,
        ["isPublished"] = Boolean,
        ["isActive"] = Boolean,
        ["sortOrder"] = Integer,
        ["definitionKeys"] = StringArray
    };

    private static readonly IReadOnlyDictionary<string, string> EventSessionTemplateFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sessionTemplateId"] = Uuid,
        ["eventTemplateId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasEdit"] = TrueBoolean,
        ["templateKey"] = ShortText,
        ["displayName"] = ShortText,
        ["description"] = LongText,
        ["eventSessionKindId"] = """{ "type": ["integer", "null"] }""",
        ["version"] = Integer,
        ["isPublished"] = Boolean,
        ["isActive"] = Boolean,
        ["sortOrder"] = Integer,
        ["definitionKeys"] = StringArray
    };

    private static readonly IReadOnlyDictionary<string, string> EventTemplateSyncFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["eventId"] = Uuid,
        ["sessionId"] = Uuid,
        ["expectedConcurrencyStamp"] = Uuid,
        ["managementContextHasEdit"] = TrueBoolean,
        ["baseProvenanceVersion"] = Integer,
        ["plan"] = SyncPlan
    };

    private static readonly IReadOnlyDictionary<string, string> DestructiveFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["managementContextHasDelete"] = TrueBoolean,
        ["destructiveSummary"] = """{ "type": "string", "maxLength": 1000 }""",
        ["confirmationPhrase"] = ShortText,
        ["acknowledgedConsequences"] = TrueBoolean
    };

    private static readonly IReadOnlySet<string> ForbiddenPayloadFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "tenantId",
        "actorId",
        "actorUserId",
        "userId",
        "createdBy",
        "updatedBy",
        "deletedBy",
        "createdAt",
        "updatedAt",
        "deletedAt",
        "isDeleted",
        "tenant",
        "actor",
        "organizationId",
        "roleAssignments",
        "outboxMessages",
        "notificationFanout",
        "atprotoRecordId",
        "concurrencyStamp",
        "sourceTemplateKey",
        "sourceTemplateVersion",
        "sourceTemplateDefinitionId",
        "sourceTemplateOptionId",
        "instantiatedAt",
        "instantiatedFromTemplateAt",
        "lastSyncedFromTemplateAt"
    };

    public static IReadOnlyList<AiToolDefinition> CreateAll()
        =>
        [
            Definition(
                AiProposedActionKind.CreateEventSession,
                "CreateEventSession",
                "Create event session",
                ResourceKinds.EventSession,
                AuthorizationActions.Create,
                EventSessionFields,
                ["eventId", "expectedConcurrencyStamp", "managementContextHasAddSession", "title", "startTime", "endTime"],
                "event-session-proposal-card",
                requiredHalLinkRel: "add-session",
                workflowScope: "event-sessions",
                contextScope: "event-session-context"),
            Definition(
                AiProposedActionKind.UpdateEventSession,
                "UpdateEventSession",
                "Update event session",
                ResourceKinds.EventSession,
                AuthorizationActions.Update,
                Without(EventSessionFields, "managementContextHasAddSession"),
                ["sessionId", "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "title", "startTime", "endTime"],
                "event-session-update-proposal-card",
                requiredHalLinkRel: "edit",
                workflowScope: "event-sessions",
                contextScope: "event-session-context"),
            DestructiveDefinition(
                AiProposedActionKind.DeleteEventSession,
                "DeleteEventSession",
                "Delete event session",
                ResourceKinds.EventSession,
                AuthorizationActions.Delete,
                Pick(EventSessionFields, "sessionId", "eventId", "expectedConcurrencyStamp"),
                ["sessionId", "eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"],
                "DELETE_EVENT_SESSION",
                "event-session-delete-proposal-card",
                requiredHalLinkRel: "delete",
                workflowScope: "event-sessions",
                contextScope: "event-session-context"),

            Definition(AiProposedActionKind.CreateEventSessionGroup, "CreateEventSessionGroup", "Create event session group", ResourceKinds.EventSessionGroup, AuthorizationActions.Create, Without(EventSessionGroupFields, "groupId", "sessionId", "isPrimary"), ["eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "name"], "event-session-group-proposal-card", "edit", "event-program", "event-session-group-context"),
            Definition(AiProposedActionKind.UpdateEventSessionGroup, "UpdateEventSessionGroup", "Update event session group", ResourceKinds.EventSessionGroup, AuthorizationActions.Update, Without(EventSessionGroupFields, "sessionId", "isPrimary"), ["groupId", "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "name"], "event-session-group-update-proposal-card", "edit", "event-program", "event-session-group-context"),
            DestructiveDefinition(AiProposedActionKind.DeleteEventSessionGroup, "DeleteEventSessionGroup", "Delete event session group", ResourceKinds.EventSessionGroup, AuthorizationActions.Delete, Pick(EventSessionGroupFields, "groupId", "eventId", "expectedConcurrencyStamp"), ["groupId", "eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "DELETE_EVENT_SESSION_GROUP", "event-session-group-delete-proposal-card", "delete", "event-program", "event-session-group-context"),
            Definition(AiProposedActionKind.AssignSessionToEventSessionGroup, "AssignSessionToEventSessionGroup", "Assign session to group", ResourceKinds.EventSessionGroup, AuthorizationActions.Update, Pick(EventSessionGroupFields, "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "groupId", "sessionId", "isPrimary", "sortOrder"), ["eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "groupId", "sessionId"], "event-session-group-assignment-proposal-card", "edit", "event-program", "event-session-group-context"),
            DestructiveDefinition(AiProposedActionKind.UnassignSessionFromEventSessionGroup, "UnassignSessionFromEventSessionGroup", "Unassign session from group", ResourceKinds.EventSessionGroup, AuthorizationActions.Update, Pick(EventSessionGroupFields, "eventId", "expectedConcurrencyStamp", "groupId", "sessionId"), ["eventId", "expectedConcurrencyStamp", "groupId", "sessionId", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "UNASSIGN_EVENT_SESSION_GROUP", "event-session-group-unassign-proposal-card", "delete", "event-program", "event-session-group-context"),

            Definition(AiProposedActionKind.CreateEventDay, "CreateEventDay", "Create event day", ResourceKinds.EventDay, AuthorizationActions.Create, Without(EventDayFields, "dayId"), ["eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "localDate"], "event-day-proposal-card", "edit", "event-agenda", "event-day-context"),
            Definition(AiProposedActionKind.UpdateEventDay, "UpdateEventDay", "Update event day", ResourceKinds.EventDay, AuthorizationActions.Update, EventDayFields, ["dayId", "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "localDate"], "event-day-update-proposal-card", "edit", "event-agenda", "event-day-context"),
            DestructiveDefinition(AiProposedActionKind.DeleteEventDay, "DeleteEventDay", "Delete event day", ResourceKinds.EventDay, AuthorizationActions.Delete, Pick(EventDayFields, "dayId", "eventId", "expectedConcurrencyStamp"), ["dayId", "eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "DELETE_EVENT_DAY", "event-day-delete-proposal-card", "delete", "event-agenda", "event-day-context"),
            Definition(AiProposedActionKind.CreateEventAgendaItem, "CreateEventAgendaItem", "Create event agenda item", ResourceKinds.EventAgendaItem, AuthorizationActions.Create, Without(EventAgendaItemFields, "agendaItemId"), ["eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "title", "startTime", "endTime"], "event-agenda-item-proposal-card", "edit", "event-agenda", "event-agenda-item-context"),
            Definition(AiProposedActionKind.UpdateEventAgendaItem, "UpdateEventAgendaItem", "Update event agenda item", ResourceKinds.EventAgendaItem, AuthorizationActions.Update, EventAgendaItemFields, ["agendaItemId", "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "title", "startTime", "endTime"], "event-agenda-item-update-proposal-card", "edit", "event-agenda", "event-agenda-item-context"),
            DestructiveDefinition(AiProposedActionKind.DeleteEventAgendaItem, "DeleteEventAgendaItem", "Delete event agenda item", ResourceKinds.EventAgendaItem, AuthorizationActions.Delete, Pick(EventAgendaItemFields, "agendaItemId", "eventId", "expectedConcurrencyStamp"), ["agendaItemId", "eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "DELETE_EVENT_AGENDA_ITEM", "event-agenda-item-delete-proposal-card", "delete", "event-agenda", "event-agenda-item-context"),

            Definition(AiProposedActionKind.CreateEventCustomPropertyDefinition, "CreateEventCustomPropertyDefinition", "Create event custom property definition", ResourceKinds.CustomPropertyDefinition, AuthorizationActions.Create, Without(CustomPropertyDefinitionFields, "definitionId"), ["eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "namespace", "key", "displayName", "propertyType"], "event-custom-property-definition-proposal-card", "edit", "event-custom-properties", "event-custom-property-context"),
            Definition(AiProposedActionKind.UpdateEventCustomPropertyDefinition, "UpdateEventCustomPropertyDefinition", "Update event custom property definition", ResourceKinds.CustomPropertyDefinition, AuthorizationActions.Update, CustomPropertyDefinitionFields, ["definitionId", "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "namespace", "key", "displayName", "propertyType"], "event-custom-property-definition-update-proposal-card", "edit", "event-custom-properties", "event-custom-property-context"),
            DestructiveDefinition(AiProposedActionKind.DeleteEventCustomPropertyDefinition, "DeleteEventCustomPropertyDefinition", "Delete event custom property definition", ResourceKinds.CustomPropertyDefinition, AuthorizationActions.Delete, Pick(CustomPropertyDefinitionFields, "definitionId", "eventId", "expectedConcurrencyStamp"), ["definitionId", "eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "DELETE_EVENT_CUSTOM_PROPERTY_DEFINITION", "event-custom-property-definition-delete-proposal-card", "delete", "event-custom-properties", "event-custom-property-context"),
            DestructiveDefinition(AiProposedActionKind.PurgeEventCustomPropertyDefinition, "PurgeEventCustomPropertyDefinition", "Purge event custom property definition", ResourceKinds.CustomPropertyDefinition, AuthorizationActions.Delete, Pick(CustomPropertyDefinitionFields, "definitionId", "eventId", "expectedConcurrencyStamp"), ["definitionId", "eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "PURGE_EVENT_CUSTOM_PROPERTY_DEFINITION", "event-custom-property-definition-purge-proposal-card", "delete", "event-custom-properties", "event-custom-property-context"),
            Definition(AiProposedActionKind.SetEventCustomPropertyValue, "SetEventCustomPropertyValue", "Set event custom property value", ResourceKinds.CustomPropertyValue, AuthorizationActions.Update, Without(CustomPropertyValueFields, "definitionId", "values"), ["eventCustomPropertyDefinitionId", "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "ordinal"], "event-custom-property-value-proposal-card", "edit", "event-custom-properties", "event-custom-property-context"),
            Definition(AiProposedActionKind.SetEventCustomPropertyMultiValues, "SetEventCustomPropertyMultiValues", "Set event custom property multi-values", ResourceKinds.CustomPropertyValue, AuthorizationActions.Update, Pick(CustomPropertyValueFields, "definitionId", "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "values"), ["definitionId", "eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "values"], "event-custom-property-multi-value-proposal-card", "edit", "event-custom-properties", "event-custom-property-context"),

            Definition(AiProposedActionKind.AssignEventTeamRole, "AssignEventTeamRole", "Assign event team role", ResourceKinds.Event, AuthorizationActions.Events.ManageTeam, Without(EventTeamFields, "assignmentId"), ["eventId", "expectedConcurrencyStamp", "managementContextHasManageTeam", "targetUserEmail", "roleId"], "event-team-role-proposal-card", "team", "event-team", "event-team-context"),
            DestructiveDefinition(AiProposedActionKind.RevokeEventTeamRole, "RevokeEventTeamRole", "Revoke event team role", ResourceKinds.Event, AuthorizationActions.Events.ManageTeam, Pick(EventTeamFields, "assignmentId", "eventId", "expectedConcurrencyStamp"), ["assignmentId", "eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "REVOKE_EVENT_TEAM_ROLE", "event-team-role-revoke-proposal-card", "delete", "event-team", "event-team-context"),

            Definition(AiProposedActionKind.CreateEventTemplate, "CreateEventTemplate", "Create event template", ResourceKinds.CustomPropertyTemplate, AuthorizationActions.Create, Without(EventTemplateFields, "templateId"), ["expectedConcurrencyStamp", "managementContextHasEdit", "templateKey", "displayName"], "event-template-proposal-card", "edit", "event-templates", "event-template-context"),
            Definition(AiProposedActionKind.UpdateEventTemplate, "UpdateEventTemplate", "Update event template", ResourceKinds.CustomPropertyTemplate, AuthorizationActions.Update, EventTemplateFields, ["templateId", "expectedConcurrencyStamp", "managementContextHasEdit", "templateKey", "displayName"], "event-template-update-proposal-card", "edit", "event-templates", "event-template-context"),
            DestructiveDefinition(AiProposedActionKind.DeleteEventTemplate, "DeleteEventTemplate", "Delete event template", ResourceKinds.CustomPropertyTemplate, AuthorizationActions.Delete, Pick(EventTemplateFields, "templateId", "expectedConcurrencyStamp"), ["templateId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "DELETE_EVENT_TEMPLATE", "event-template-delete-proposal-card", "delete", "event-templates", "event-template-context"),
            Definition(AiProposedActionKind.CreateEventSessionTemplate, "CreateEventSessionTemplate", "Create event session template", ResourceKinds.CustomPropertyTemplate, AuthorizationActions.Create, Without(EventSessionTemplateFields, "sessionTemplateId"), ["eventTemplateId", "expectedConcurrencyStamp", "managementContextHasEdit", "templateKey", "displayName"], "event-session-template-proposal-card", "edit", "event-templates", "event-session-template-context"),
            Definition(AiProposedActionKind.UpdateEventSessionTemplate, "UpdateEventSessionTemplate", "Update event session template", ResourceKinds.CustomPropertyTemplate, AuthorizationActions.Update, EventSessionTemplateFields, ["sessionTemplateId", "eventTemplateId", "expectedConcurrencyStamp", "managementContextHasEdit", "templateKey", "displayName"], "event-session-template-update-proposal-card", "edit", "event-templates", "event-session-template-context"),
            DestructiveDefinition(AiProposedActionKind.DeleteEventSessionTemplate, "DeleteEventSessionTemplate", "Delete event session template", ResourceKinds.CustomPropertyTemplate, AuthorizationActions.Delete, Pick(EventSessionTemplateFields, "sessionTemplateId", "expectedConcurrencyStamp"), ["sessionTemplateId", "expectedConcurrencyStamp", "managementContextHasDelete", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "DELETE_EVENT_SESSION_TEMPLATE", "event-session-template-delete-proposal-card", "delete", "event-templates", "event-session-template-context"),
            DestructiveDefinition(AiProposedActionKind.ApplyEventTemplateSync, "ApplyEventTemplateSync", "Apply event template sync", ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.SyncApply, EventTemplateSyncFields, ["eventId", "expectedConcurrencyStamp", "managementContextHasEdit", "baseProvenanceVersion", "plan", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "APPLY_EVENT_TEMPLATE_SYNC", "event-template-sync-apply-proposal-card", "edit", "event-templates", "event-template-sync-context"),
            DestructiveDefinition(AiProposedActionKind.ApplyEventSessionTemplateSync, "ApplyEventSessionTemplateSync", "Apply event session template sync", ResourceKinds.CustomPropertyTemplate, AuthorizationActions.CustomPropertyTemplates.SyncApply, Without(EventTemplateSyncFields, "eventId"), ["sessionId", "expectedConcurrencyStamp", "managementContextHasEdit", "baseProvenanceVersion", "plan", "destructiveSummary", "confirmationPhrase", "acknowledgedConsequences"], "APPLY_EVENT_SESSION_TEMPLATE_SYNC", "event-session-template-sync-apply-proposal-card", "edit", "event-templates", "event-session-template-sync-context")
        ];

    private static AiToolDefinition Definition(
        AiProposedActionKind kind,
        string name,
        string displayName,
        string resourceKind,
        string authorizationAction,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<string> requiredFields,
        string presentationCard,
        string? requiredHalLinkRel,
        string workflowScope,
        string contextScope)
        => new(
            kind,
            name,
            displayName,
            BuildSchema(requiredFields, fields),
            fields.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ForbiddenPayloadFields,
            typeof(EventSubResourceAiActionMapper),
            new AiToolAuthorizationRequirement(resourceKind, authorizationAction),
            ExposeToProvider: false,
            ExposeToMcp: true,
            AgentMetadata: Metadata(
                displayName,
                presentationCard,
                requiredHalLinkRel,
                workflowScope,
                contextScope,
                destructive: false));

    private static AiToolDefinition DestructiveDefinition(
        AiProposedActionKind kind,
        string name,
        string displayName,
        string resourceKind,
        string authorizationAction,
        IReadOnlyDictionary<string, string> baseFields,
        IReadOnlyList<string> requiredFields,
        string confirmationPhrase,
        string presentationCard,
        string? requiredHalLinkRel,
        string workflowScope,
        string contextScope)
    {
        var fields = Merge(baseFields, DestructiveFields)
            .ToDictionary(StringComparer.OrdinalIgnoreCase);
        fields["confirmationPhrase"] = $$"""{ "type": "string", "enum": ["{{confirmationPhrase}}"] }""";

        return new AiToolDefinition(
            kind,
            name,
            displayName,
            BuildSchema(requiredFields, fields),
            fields.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ForbiddenPayloadFields,
            typeof(EventSubResourceAiActionMapper),
            new AiToolAuthorizationRequirement(resourceKind, authorizationAction),
            ExposeToProvider: false,
            ExposeToMcp: true,
            AgentMetadata: Metadata(
                displayName,
                presentationCard,
                requiredHalLinkRel,
                workflowScope,
                contextScope,
                destructive: true));
    }

    private static AiToolAgentMetadata Metadata(
        string displayName,
        string presentationCard,
        string? requiredHalLinkRel,
        string workflowScope,
        string contextScope,
        bool destructive)
        => new(
            new AiToolScopeMetadata(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "/events/{eventId}",
                    "/events/manage",
                    "/events/program",
                    "/calendar"
                },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { workflowScope },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { contextScope }),
            destructive ? AiToolRiskClass.High : AiToolRiskClass.Medium,
            AiToolApprovalMode.HumanConfirmationRequired,
            "Available only when the current API/HAL context exposes the required event-management affordance.",
            destructive ? AiToolFollowUpPolicy.ShowWarningsBeforeConfirmation : AiToolFollowUpPolicy.AskClarifyingQuestionBeforeProposal,
            $"Read the current management context first, use server-issued identifiers and concurrency stamps, propose {displayName.ToLowerInvariant()} only, and do not claim side effects happened before confirmation.",
            new AiToolResultPresentationMetadata(
                presentationCard,
                $"Review {displayName.ToLowerInvariant()} proposal",
                $"{displayName} confirmed",
                $"{displayName} was not applied"),
            requiredHalLinkRel,
            destructive);

    private static string BuildSchema(IReadOnlyList<string> requiredFields, IReadOnlyDictionary<string, string> fields)
    {
        var requiredJson = string.Join(", ", requiredFields.Select(field => $"\"{field}\""));
        var propertiesJson = string.Join(
            ",\n",
            fields.Select(field => $"            \"{field.Key}\": {field.Value}"));

        return $$"""
            {
              "type": "object",
              "additionalProperties": false,
              "required": [{{requiredJson}}],
              "properties": {
                {{propertiesJson}}
              }
            }
            """;
    }

    private static IReadOnlyDictionary<string, string> Pick(
        IReadOnlyDictionary<string, string> source,
        params string[] names)
        => names.ToDictionary(name => name, name => source[name], StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> Without(
        IReadOnlyDictionary<string, string> source,
        params string[] names)
    {
        var excluded = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return source
            .Where(pair => !excluded.Contains(pair.Key))
            .ToDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<KeyValuePair<string, string>> Merge(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        foreach (var pair in first)
        {
            yield return pair;
        }

        foreach (var pair in second)
        {
            yield return pair;
        }
    }
}
