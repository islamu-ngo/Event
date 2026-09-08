using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Domain.Enums;

namespace Explore.API.Mcp;

/// <summary>
/// Location data is the sharpest disclosure risk in the MCP surface: an assistant that learns a private
/// venue address or room assignment cannot be made to forget it. This guard runs every location value an
/// Event tool is about to serialize through the AI context gateway and throws unless the gateway confirms
/// the exact expected disclosure — so a new field, a changed policy, or a partial sanitization stops the
/// response rather than quietly widening what the model sees.
/// <para>
/// It lives apart from the tool class because it is the one piece of that class whose failure mode is a
/// privacy incident rather than a wrong answer, and it should be reviewable and testable on its own.
/// </para>
/// </summary>
public sealed class EventMcpLocationDisclosureGuard(IAiContextGateway aiContextGateway)
{
    public void EnforcePublicSessionLocationDisclosureCeiling(
        IEnumerable<EventSessionListDto> sessions)
    {
        var requests = sessions
            .Select(session => session.EventLocation)
            .Where(location => location is not null)
            .Cast<EventLocationPublicDto>()
            .Distinct()
            .Select(CreatePublicEventLocationSanitizationInput)
            .ToArray();

        EnforceSuccessfulPublicLocationSanitization(requests, aiContextGateway.SanitizeMany(requests));
    }

    public void EnforcePublicProgramLocationDisclosureCeiling(EventProgramSummaryDto program)
    {
        var locations = new List<EventLocationPublicDto>();
        foreach (var group in program.Sections.SelectMany(section => section.SessionGroups))
        {
            if (group.EventLocation is not null)
            {
                locations.Add(group.EventLocation);
            }

            locations.AddRange(group.Days
                .SelectMany(day => day.Items)
                .Where(item => item.EventLocation is not null)
                .Select(item => item.EventLocation!));
        }

        AiContextSanitizationInput[] requests = locations
            .Distinct()
            .Select(CreatePublicEventLocationSanitizationInput)
            .ToArray();
        EnforceSuccessfulPublicLocationSanitization(
            requests,
            aiContextGateway.SanitizeMany(requests));
    }

    public void EnforceManagedProgramLocationDisclosureCeiling(
        IReadOnlyList<EventSessionListDto> sessions,
        IReadOnlyList<EventSessionGroupListDto> sessionGroups)
    {
        var requests = sessions
            .Select(session => CreateZeroDisclosureLocationSanitizationInput(
                session.LocationId,
                session.LocationFullName,
                session.LocationCity,
                session.RoomId,
                session.RoomName))
            .Concat(sessionGroups.Select(group => CreateZeroDisclosureLocationSanitizationInput(
                group.LocationId,
                group.LocationName,
                roomId: group.RoomId,
                roomName: group.RoomName)))
            .ToArray();

        EnforceSuccessfulZeroLocationSanitization(requests, aiContextGateway.SanitizeMany(requests));
    }

    private static AiContextSanitizationInput CreatePublicEventLocationSanitizationInput(
        EventLocationPublicDto location)
    {
        EventLocationPublicFieldsDto? fields = location.Fields;
        return new AiContextSanitizationInput(
            EntityName: nameof(EventLocationPublicDto),
            Fields: new Dictionary<string, object?>
            {
                [nameof(EventLocationPublicDto.EventLocationId)] = location.EventLocationId,
                [nameof(EventLocationPublicDto.State)] = location.State,
                [nameof(EventLocationPublicFieldsDto.Country)] = fields?.Country,
                [nameof(EventLocationPublicFieldsDto.Timezone)] = fields?.Timezone,
                [nameof(EventLocationPublicFieldsDto.City)] = fields?.City,
                [nameof(EventLocationPublicFieldsDto.VenueName)] = fields?.VenueName,
                [nameof(EventLocationPublicFieldsDto.RoomName)] = fields?.RoomName,
                [nameof(EventLocationPublicFieldsDto.StreetAddress)] = fields?.StreetAddress,
                [nameof(EventLocationPublicFieldsDto.Postcode)] = fields?.Postcode,
                [nameof(EventLocationPublicFieldsDto.Latitude)] = fields?.Latitude,
                [nameof(EventLocationPublicFieldsDto.Longitude)] = fields?.Longitude,
                [nameof(EventLocationPublicFieldsDto.FormattedAddress)] = fields?.FormattedAddress,
                [nameof(EventLocationPublicFieldsDto.MapUrl)] = fields?.MapUrl,
                [nameof(EventLocationPublicFieldsDto.Geohash)] = fields?.Geohash
            },
            ProviderTrustTier: AiProviderTrustTierEnum.Unknown,
            ViewerScope: AiViewerScopeEnum.Public,
            GrantedFieldKeys: new HashSet<string>(),
            PiiDisclosureEnabled: false,
            MaxSensitivity: AiContextSensitivityEnum.Public);
    }

    private static AiContextSanitizationInput CreateZeroDisclosureLocationSanitizationInput(
        Guid? locationId = null,
        string? locationName = null,
        string? locationCity = null,
        Guid? roomId = null,
        string? roomName = null)
        => new(
            EntityName: "LocationPii",
            Fields: new Dictionary<string, object?>
            {
                ["LocationId"] = locationId,
                ["LocationName"] = locationName,
                ["LocationCity"] = locationCity,
                ["RoomId"] = roomId,
                ["RoomName"] = roomName
            },
            ProviderTrustTier: AiProviderTrustTierEnum.Unknown,
            ViewerScope: AiViewerScopeEnum.Public,
            GrantedFieldKeys: new HashSet<string>(),
            PiiDisclosureEnabled: false,
            MaxSensitivity: AiContextSensitivityEnum.Public);

    private static void EnforceSuccessfulPublicLocationSanitization(
        IReadOnlyList<AiContextSanitizationInput> requests,
        IReadOnlyList<AiContextSanitizedEnvelope> envelopes)
    {
        if (requests.Count != envelopes.Count)
        {
            throw new InvalidOperationException("AI context disclosure failed for public EventLocation data.");
        }

        for (var index = 0; index < requests.Count; index++)
        {
            AiContextSanitizationInput request = requests[index];
            AiContextSanitizedEnvelope envelope = envelopes[index];
            if (!string.Equals(request.EntityName, envelope.EntityName, StringComparison.Ordinal)
                || !envelope.Succeeded
                || envelope.RedactedFieldNames.Count != 0
                || envelope.DeniedFieldNames.Count != 0
                || envelope.DisclosedFields.Count != request.Fields.Count
                || request.Fields.Any(field => !IsExactAllowedField(envelope.DisclosedFields, field)))
            {
                throw new InvalidOperationException("AI context disclosure failed for public EventLocation data.");
            }
        }
    }

    private static bool IsExactAllowedField(
        IReadOnlyList<AiContextDisclosedField> disclosedFields,
        KeyValuePair<string, object?> expected)
    {
        AiContextDisclosedField[] matches = disclosedFields
            .Where(field => string.Equals(field.Name, expected.Key, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1
            && matches[0].AppliedRule == AiContextDisclosureRuleEnum.Allow
            && Equals(matches[0].Value, expected.Value);
    }

    private static void EnforceSuccessfulZeroLocationSanitization(
        IReadOnlyList<AiContextSanitizationInput> requests,
        IReadOnlyList<AiContextSanitizedEnvelope> envelopes)
    {
        if (requests.Count != envelopes.Count)
        {
            throw new InvalidOperationException("AI context disclosure failed for public location data.");
        }

        for (var index = 0; index < requests.Count; index++)
        {
            var envelope = envelopes[index];
            if (!string.Equals(requests[index].EntityName, envelope.EntityName, StringComparison.Ordinal)
                || !envelope.Succeeded
                || envelope.DisclosedFields.Any(field => field.Value is not null))
            {
                throw new InvalidOperationException("AI context disclosure failed for public location data.");
            }
        }
    }

}
