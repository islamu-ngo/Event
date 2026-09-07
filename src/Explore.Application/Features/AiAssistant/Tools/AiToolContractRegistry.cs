using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Tools;

public sealed class AiToolContractRegistry : IAiToolContractRegistry
{
    private readonly IReadOnlyDictionary<AiProposedActionKind, AiToolDefinition> _definitionsByKind;

    public static AiToolContractRegistry CreateDefault()
        => new(
        [
            CreateEventDraftAiToolDefinition.Create(),
            UpdateEventDraftAiToolDefinition.Create(),
            PublishEventAiToolDefinition.Create(),
            DeleteEventAiToolDefinition.Create(),
            UpsertEventIslamicAspectAiToolDefinition.Create(),
            DeleteEventIslamicAspectAiToolDefinition.Create(),
            UpsertEventTechAspectAiToolDefinition.Create(),
            DeleteEventTechAspectAiToolDefinition.Create(),
            .. EventSubResourceAiToolDefinitions.CreateAll(),
            .. EventModerationAiToolDefinitions.CreateAll()
        ]);

    public AiToolContractRegistry(IEnumerable<AiToolDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var definitionList = definitions.ToArray();
        var duplicateKind = definitionList
            .GroupBy(definition => definition.Kind)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateKind is not null)
        {
            throw new ArgumentException("AI tool definitions must be unique by proposed-action kind.", nameof(definitions));
        }

        Definitions = definitionList;
        _definitionsByKind = definitionList.ToDictionary(definition => definition.Kind);
    }

    public IReadOnlyList<AiToolDefinition> Definitions { get; }

    public AiToolDefinition? FindDefinition(AiProposedActionKind kind)
        => _definitionsByKind.GetValueOrDefault(kind);

    public AiToolValidationResult ValidatePayload(
        AiProposedActionKind kind,
        string payloadJson,
        bool allowProviderNormalization = false)
    {
        var definition = FindDefinition(kind);
        if (definition is null)
        {
            return AiToolValidationResult.Failure(
                "unknown_action_kind",
                "AI provider returned an unsupported proposed action kind.",
                AiToolCorrectionMessages.SchemaExactRetry);
        }

        var validation = AiToolPayloadGuard.ValidateJsonObject(
            payloadJson,
            definition.AllowedPayloadFields,
            definition.ForbiddenPayloadFields,
            definition.JsonSchema);

        if (!allowProviderNormalization
            || kind != AiProposedActionKind.CreateEventDraft
            || !ShouldNormalizeCreateEventDraftProviderPayload(validation))
        {
            return validation;
        }

        return TryNormalizeCreateEventDraftPayload(payloadJson, definition, out var normalizedPayloadJson)
            ? ValidateNormalizedPayload(normalizedPayloadJson, definition)
            : validation;
    }

    private static bool ShouldNormalizeCreateEventDraftProviderPayload(AiToolValidationResult validation)
        => !validation.Succeeded
            && !string.Equals(validation.FailureCode, "forbidden_tool_argument", StringComparison.Ordinal);

    private static AiToolValidationResult ValidateNormalizedPayload(
        string normalizedPayloadJson,
        AiToolDefinition definition)
    {
        var validation = AiToolPayloadGuard.ValidateJsonObject(
            normalizedPayloadJson,
            definition.AllowedPayloadFields,
            definition.ForbiddenPayloadFields,
            definition.JsonSchema);

        return validation.Succeeded
            ? AiToolValidationResult.Success(normalizedPayloadJson)
            : validation;
    }

    private static bool TryNormalizeCreateEventDraftPayload(
        string payloadJson,
        AiToolDefinition definition,
        out string normalizedPayloadJson)
    {
        normalizedPayloadJson = payloadJson;

        JsonObject? payload;
        try
        {
            payload = JsonNode.Parse(payloadJson) as JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null)
        {
            return false;
        }

        var changed = NormalizeCreateEventDraftAliases(payload);
        changed |= RemoveInvalidOptionalUuid(payload, "organizationId");
        changed |= RemoveInvalidOptionalUuid(payload, "groupId");

        if (!changed)
        {
            return false;
        }

        var allowedFieldsOnly = payload
            .Select(property => property.Key)
            .All(definition.AllowedPayloadFields.Contains);
        if (!allowedFieldsOnly)
        {
            return false;
        }

        normalizedPayloadJson = payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return true;
    }

    private static bool NormalizeCreateEventDraftAliases(JsonObject payload)
    {
        var changed = false;

        changed |= MoveAliasedProperty(payload, payload, "title", "eventTitle", "eventName", "name");
        changed |= MoveAliasedProperty(payload, payload, "description", "summary", "shortDescription");
        changed |= MoveAliasedProperty(payload, payload, "eventUrl", "url", "eventLink", "websiteUrl");

        changed |= NormalizeIslamicAspectAliases(payload);
        changed |= NormalizeLocationAliases(payload);
        changed |= NormalizeRoomAliases(payload);
        changed |= NormalizeSessionAliases(payload);
        changed |= PruneIncompleteCreateEventDraftNestedPayloads(payload);

        return changed;
    }

    private static bool PruneIncompleteCreateEventDraftNestedPayloads(JsonObject payload)
    {
        var changed = false;
        var hasCompleteLocation = HasCompleteLocation(payload);

        if (FindObject(payload, "location") is not null && !hasCompleteLocation)
        {
            changed |= RemoveProperty(payload, "location");
            hasCompleteLocation = false;
        }

        var room = FindObject(payload, "room");
        if (room is not null && (!hasCompleteLocation || !HasNonBlankStringProperty(room, "name")))
        {
            changed |= RemoveProperty(payload, "room");
        }

        var session = FindObject(payload, "session");
        if (session is not null
            && (!HasNonBlankStringProperty(session, "startTime")
                || !HasNonBlankStringProperty(session, "endTime")))
        {
            changed |= RemoveProperty(payload, "session");
        }

        return changed;
    }

    private static bool HasCompleteLocation(JsonObject payload)
    {
        var location = FindObject(payload, "location");
        return location is not null
            && HasNonBlankStringProperty(location, "fullName")
            && HasNonBlankStringProperty(location, "address")
            && HasNonBlankStringProperty(location, "postcode")
            && HasNonBlankStringProperty(location, "country")
            && HasNonBlankStringProperty(location, "city");
    }

    private static bool NormalizeIslamicAspectAliases(JsonObject payload)
    {
        var changed = false;
        JsonObject? aspect = FindObject(payload, "islamicAspect");
        if (aspect is null
            && HasAnyProperty(
                payload,
                "genderMode",
                "includesQuranRecitation",
                "primaryLanguageId",
                "referencePrayer",
                "prayerTimeOffset"))
        {
            aspect = [];
            payload["islamicAspect"] = aspect;
            changed = true;
        }

        if (aspect is null)
        {
            return changed;
        }

        changed |= MoveAliasedProperty(payload, aspect, "genderMode", "genderMode");
        changed |= MoveAliasedProperty(payload, aspect, "includesQuranRecitation", "includesQuranRecitation");
        changed |= MoveAliasedProperty(payload, aspect, "primaryLanguageId", "primaryLanguageId");
        changed |= MoveAliasedProperty(payload, aspect, "referencePrayer", "referencePrayer");
        changed |= MoveAliasedProperty(payload, aspect, "prayerTimeOffset", "prayerTimeOffset");
        return changed;
    }

    private static bool NormalizeLocationAliases(JsonObject payload)
    {
        var changed = false;
        JsonObject? location = FindObject(payload, "location");

        if (location is null && TryTakeProperty(payload, "location", out var locationValue))
        {
            if (locationValue?.GetValueKind() == JsonValueKind.String)
            {
                location = new JsonObject
                {
                    ["fullName"] = locationValue
                };
                payload["location"] = location;
                changed = true;
            }
            else
            {
                payload["location"] = locationValue;
            }
        }

        if (location is null
            && HasAnyProperty(
                payload,
                "locationName",
                "locationFullName",
                "venueName",
                "venue",
                "address",
                "streetAddress",
                "postcode",
                "postalCode",
                "country",
                "city",
                "locationTimezone"))
        {
            location = [];
            payload["location"] = location;
            changed = true;
        }

        if (location is null)
        {
            return changed;
        }

        changed |= MoveAliasedProperty(location, location, "fullName", "name", "locationName", "locationFullName", "venueName", "venue");
        changed |= MoveAliasedProperty(location, location, "address", "streetAddress");
        changed |= MoveAliasedProperty(location, location, "postcode", "postalCode", "zipCode");
        changed |= MoveAliasedProperty(location, location, "timezone", "timeZone", "locationTimezone");
        changed |= MoveAliasedProperty(payload, location, "fullName", "locationName", "locationFullName", "venueName", "venue");
        changed |= MoveAliasedProperty(payload, location, "address", "address", "streetAddress");
        changed |= MoveAliasedProperty(payload, location, "postcode", "postcode", "postalCode", "zipCode");
        changed |= MoveAliasedProperty(payload, location, "country", "country");
        changed |= MoveAliasedProperty(payload, location, "city", "city");
        changed |= MoveAliasedProperty(payload, location, "timezone", "locationTimezone");
        return changed;
    }

    private static bool NormalizeRoomAliases(JsonObject payload)
    {
        var changed = false;
        JsonObject? room = FindObject(payload, "room");

        if (room is null && TryTakeProperty(payload, "room", out var roomValue))
        {
            if (roomValue?.GetValueKind() == JsonValueKind.String)
            {
                room = new JsonObject
                {
                    ["name"] = roomValue
                };
                payload["room"] = room;
                changed = true;
            }
            else
            {
                payload["room"] = roomValue;
            }
        }

        if (room is null && HasAnyProperty(payload, "roomName", "hallName"))
        {
            room = [];
            payload["room"] = room;
            changed = true;
        }

        if (room is null)
        {
            return changed;
        }

        changed |= MoveAliasedProperty(room, room, "name", "roomName", "hallName");
        changed |= MoveAliasedProperty(payload, room, "name", "roomName", "hallName");
        return changed;
    }

    private static bool NormalizeSessionAliases(JsonObject payload)
    {
        var changed = false;
        JsonObject? session = FindObject(payload, "session");

        if (session is null
            && HasAnyProperty(
                payload,
                "startTime",
                "startsAt",
                "startDateTime",
                "endTime",
                "endsAt",
                "endDateTime",
                "sessionTitle",
                "sessionName",
                "eventSessionKindId",
                "maxAudienceAttendees",
                "registrationModeId",
                "languageIds",
                "speakerActorIds"))
        {
            session = [];
            payload["session"] = session;
            changed = true;
        }

        if (session is null)
        {
            return changed;
        }

        changed |= MoveAliasedProperty(session, session, "startTime", "startsAt", "startDateTime");
        changed |= MoveAliasedProperty(session, session, "endTime", "endsAt", "endDateTime");
        changed |= MoveAliasedProperty(session, session, "title", "name", "sessionTitle", "sessionName");
        changed |= MoveAliasedProperty(payload, session, "startTime", "startTime", "startsAt", "startDateTime");
        changed |= MoveAliasedProperty(payload, session, "endTime", "endTime", "endsAt", "endDateTime");
        changed |= MoveAliasedProperty(payload, session, "title", "sessionTitle", "sessionName");
        changed |= MoveAliasedProperty(payload, session, "eventSessionKindId", "eventSessionKindId");
        changed |= MoveAliasedProperty(payload, session, "maxAudienceAttendees", "maxAudienceAttendees");
        changed |= MoveAliasedProperty(payload, session, "registrationModeId", "registrationModeId");
        changed |= MoveAliasedProperty(payload, session, "languageIds", "languageIds");
        changed |= MoveAliasedProperty(payload, session, "speakerActorIds", "speakerActorIds");
        return changed;
    }

    private static JsonObject? FindObject(JsonObject payload, string propertyName)
    {
        var actualName = FindPropertyName(payload, propertyName);
        return actualName is not null && payload[actualName] is JsonObject value ? value : null;
    }

    private static bool MoveAliasedProperty(
        JsonObject source,
        JsonObject target,
        string targetPropertyName,
        params string[] aliases)
    {
        var changed = false;
        var targetAlreadySet = FindPropertyName(target, targetPropertyName) is not null;

        foreach (var alias in aliases)
        {
            var sourcePropertyName = FindPropertyName(source, alias);
            if (sourcePropertyName is null)
            {
                continue;
            }

            if (ReferenceEquals(source, target)
                && string.Equals(sourcePropertyName, targetPropertyName, StringComparison.Ordinal))
            {
                continue;
            }

            var value = source[sourcePropertyName];
            source.Remove(sourcePropertyName);
            if (!targetAlreadySet)
            {
                target[targetPropertyName] = value;
                targetAlreadySet = true;
            }

            changed = true;
        }

        return changed;
    }

    private static bool HasAnyProperty(JsonObject payload, params string[] propertyNames)
        => propertyNames.Any(propertyName => FindPropertyName(payload, propertyName) is not null);

    private static bool TryTakeProperty(JsonObject payload, string propertyName, out JsonNode? value)
    {
        var actualName = FindPropertyName(payload, propertyName);
        if (actualName is null)
        {
            value = null;
            return false;
        }

        value = payload[actualName];
        payload.Remove(actualName);
        return true;
    }

    private static bool RemoveProperty(JsonObject payload, string propertyName)
    {
        var actualName = FindPropertyName(payload, propertyName);
        return actualName is not null && payload.Remove(actualName);
    }

    private static bool HasNonBlankStringProperty(JsonObject payload, string propertyName)
    {
        var actualName = FindPropertyName(payload, propertyName);
        if (actualName is null)
        {
            return false;
        }

        var value = payload[actualName];
        return value?.GetValueKind() == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetValue<string>());
    }

    private static string? FindPropertyName(JsonObject payload, string propertyName)
        => payload
            .Select(property => property.Key)
            .FirstOrDefault(name => string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase));

    private static bool RemoveInvalidOptionalUuid(JsonObject payload, string propertyName)
    {
        var actualName = FindPropertyName(payload, propertyName);
        if (actualName is null)
        {
            return false;
        }

        var value = payload[actualName];
        if (value is null)
        {
            return false;
        }

        if (value.GetValueKind() == JsonValueKind.String
            && Guid.TryParse(value.GetValue<string>(), out _))
        {
            return false;
        }

        return payload.Remove(actualName);
    }

}
