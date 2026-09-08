using System.Text.Json;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Studio;

public sealed record EventTicketCatalogState(
    Guid EventId,
    Guid? CatalogId,
    int? VersionNumber,
    string CurrencyCode,
    int? StatusId,
    string? StatusCode,
    string? StatusName,
    IReadOnlyList<EventTicketTypeState> TicketTypes,
    IReadOnlyList<EventCapacityPoolState> CapacityPools,
    IReadOnlyDictionary<string, HalLink> Links,
    string? MerchantDisclosureText = null,
    string? RefundPolicyDisclosureText = null,
    string? SupportContactDisclosureText = null,
    EventTicketCatalogPaidPreflightState? PublicationPreflight = null)
{
    public bool HasLink(string relation) => Links.ContainsKey(relation);

    public static bool TryParse(
        HalResourceOfEventTicketCatalogManagementDto resource,
        out EventTicketCatalogState? state)
    {
        state = null;
        try
        {
            JsonElement root = JsonSerializer.SerializeToElement(resource.AdditionalProperties);
            if (!TryRequiredGuid(resource.EventId, root, "eventId", out Guid eventId)
                || !TryRequiredString(resource.CurrencyCode, root, "currencyCode", out string currencyCode)
                || !TryResourceLinks(resource, root, out Dictionary<string, HalLink> links)
                || !links.ContainsKey("self")
                || !TryEmbedded(resource, root, out JsonElement embedded)
                || !TryTicketTypes(embedded, out IReadOnlyList<EventTicketTypeState> ticketTypes)
                || !TryCapacityPools(embedded, out IReadOnlyList<EventCapacityPoolState> capacityPools))
            {
                return false;
            }

            state = new EventTicketCatalogState(
                eventId,
                OptionalGuid(resource.CatalogId, root, "catalogId"),
                OptionalInt(resource.VersionNumber, root, "versionNumber"),
                currencyCode,
                OptionalInt(resource.StatusId, root, "statusId"),
                OptionalString(resource.StatusCode, root, "statusCode"),
                OptionalString(resource.StatusName, root, "statusName"),
                ticketTypes,
                capacityPools,
                links,
                OptionalString(resource.MerchantDisclosureText, root, "merchantDisclosureText"),
                OptionalString(resource.RefundPolicyDisclosureText, root, "refundPolicyDisclosureText"),
                OptionalString(resource.SupportContactDisclosureText, root, "supportContactDisclosureText"),
                ParsePreflight(resource.PublicationPreflight, root));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryResourceLinks(
        HalResourceOfEventTicketCatalogManagementDto resource,
        JsonElement root,
        out Dictionary<string, HalLink> links) =>
        TryLinks(resource._links, required: true, out links)
        || TryLinks(root, required: true, out links);

    private static bool TryEmbedded(
        HalResourceOfEventTicketCatalogManagementDto resource,
        JsonElement root,
        out JsonElement embedded)
    {
        if (TryElement(resource._embedded, out embedded)
            && embedded.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        return root.TryGetProperty("_embedded", out embedded)
            && embedded.ValueKind == JsonValueKind.Object;
    }

    private static bool TryElement(object? value, out JsonElement element)
    {
        element = default;
        if (value is null)
        {
            return false;
        }

        element = value is JsonElement json
            ? json.Clone()
            : JsonSerializer.SerializeToElement(value);
        return element.ValueKind != JsonValueKind.Undefined;
    }

    private static bool TryTicketTypes(
        JsonElement embedded,
        out IReadOnlyList<EventTicketTypeState> states)
    {
        states = [];
        if (!embedded.TryGetProperty("ticket-types", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<EventTicketTypeState>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (!TryTicketType(item, out EventTicketTypeState? state))
            {
                return false;
            }

            parsed.Add(state!);
        }

        states = parsed;
        return true;
    }

    private static bool TryTicketType(JsonElement item, out EventTicketTypeState? state)
    {
        state = null;
        if (item.ValueKind != JsonValueKind.Object
            || !TryRequiredGuid(item, "id", out Guid id)
            || !TryRequiredString(item, "name", out string name)
            || !TryRequiredInt(item, "ticketPricingModeId", out int pricingModeId)
            || !TryRequiredInt(item, "participantDataCollectionModeId", out int dataCollectionModeId)
            || !TryEntitlements(item, out IReadOnlyList<TicketTypeEntitlementState> entitlements)
            || !TryLinks(item, required: false, out Dictionary<string, HalLink> links))
        {
            return false;
        }

        links = FilterItemLinks(links, id);
        state = new EventTicketTypeState(
            id,
            name,
            pricingModeId,
            OptionalString(item, "ticketPricingModeCode"),
            OptionalString(item, "ticketPricingModeName"),
            OptionalLong(item, "fixedPriceMinor"),
            OptionalLong(item, "minimumPriceMinor"),
            OptionalLong(item, "suggestedPriceMinor"),
            dataCollectionModeId,
            OptionalString(item, "participantDataCollectionModeCode"),
            OptionalString(item, "participantDataCollectionModeName"),
            OptionalGuid(item, "capacityPoolId"),
            OptionalInt(item, "minimumAge"),
            OptionalInt(item, "maximumAge"),
            OptionalBool(item, "requiresGuardian"),
            OptionalBool(item, "requiresApproval"),
            OptionalInt(item, "perOrderLimit"),
            OptionalInt(item, "perAccountLimit"),
            OptionalInt(item, "perVerifiedContactLimit"),
            OptionalInt(item, "perBookingPartyLimit"),
            entitlements,
            links);
        return true;
    }

    private static bool TryEntitlements(
        JsonElement item,
        out IReadOnlyList<TicketTypeEntitlementState> states)
    {
        states = [];
        if (!item.TryGetProperty("entitlements", out JsonElement entitlements)
            || entitlements.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<TicketTypeEntitlementState>();
        foreach (JsonElement entitlement in entitlements.EnumerateArray())
        {
            if (entitlement.ValueKind != JsonValueKind.Object
                || !TryRequiredInt(entitlement, "entitlementScopeTypeId", out int scopeTypeId)
                || !TryRequiredInt(entitlement, "includedQuantity", out int includedQuantity)
                || !TryRequiredInt(entitlement, "entitlementSelectionRuleId", out int selectionRuleId))
            {
                return false;
            }

            parsed.Add(new TicketTypeEntitlementState(
                scopeTypeId,
                OptionalString(entitlement, "entitlementScopeTypeCode"),
                OptionalString(entitlement, "entitlementScopeTypeName"),
                OptionalGuid(entitlement, "eventDayId"),
                OptionalGuid(entitlement, "eventSessionId"),
                includedQuantity,
                selectionRuleId,
                OptionalString(entitlement, "entitlementSelectionRuleCode"),
                OptionalString(entitlement, "entitlementSelectionRuleName")));
        }

        states = parsed;
        return parsed.Count > 0;
    }

    private static bool TryCapacityPools(
        JsonElement embedded,
        out IReadOnlyList<EventCapacityPoolState> states)
    {
        states = [];
        if (!embedded.TryGetProperty("capacity-pools", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<EventCapacityPoolState>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !TryRequiredGuid(item, "id", out Guid id)
                 || !TryRequiredString(item, "name", out string name)
                 || !TryRequiredInt(item, "holdDurationSeconds", out int holdDurationSeconds)
                 || !TryRequiredInt(item, "capacityHoldPolicyId", out int holdPolicyId)
                 || !TryRequiredInt(item, "capacityOversellPolicyId", out int oversellPolicyId)
                || !TryLinks(item, required: false, out Dictionary<string, HalLink> links))
            {
                return false;
            }

            parsed.Add(new EventCapacityPoolState(
                id,
                 name,
                 OptionalInt(item, "maximumQuantity"),
                 holdDurationSeconds,
                 holdPolicyId,
                 OptionalString(item, "capacityHoldPolicyCode"),
                 OptionalString(item, "capacityHoldPolicyName"),
                 oversellPolicyId,
                OptionalString(item, "capacityOversellPolicyCode"),
                OptionalString(item, "capacityOversellPolicyName"),
                OptionalBool(item, "isActive"),
                FilterItemLinks(links, id)));
        }

        states = parsed;
        return true;
    }

    private static bool TryLinks(
        JsonElement resource,
        bool required,
        out Dictionary<string, HalLink> links)
    {
        links = new Dictionary<string, HalLink>(StringComparer.Ordinal);
        if (!resource.TryGetProperty("_links", out JsonElement linkObject))
        {
            return !required;
        }

        if (linkObject.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty property in linkObject.EnumerateObject())
        {
            HalLink? link = property.Value.Deserialize<HalLink>();
            if (link is null || string.IsNullOrWhiteSpace(link.Href))
            {
                continue;
            }

            links[property.Name] = link;
        }

        return !required || links.Count > 0;
    }

    private static bool TryLinks(
        IDictionary<string, HalLink>? linkObject,
        bool required,
        out Dictionary<string, HalLink> links)
    {
        links = new Dictionary<string, HalLink>(StringComparer.Ordinal);
        if (linkObject is null)
        {
            return !required;
        }

        foreach ((string relation, HalLink? link) in linkObject)
        {
            if (link is null || string.IsNullOrWhiteSpace(link.Href))
            {
                continue;
            }

            links[relation] = link;
        }

        return !required || links.Count > 0;
    }

    private static Dictionary<string, HalLink> FilterItemLinks(
        Dictionary<string, HalLink> links,
        Guid itemId) =>
        links
            .Where(pair => pair.Key is not ("edit" or "delete") || LinkTargets(pair.Value, itemId))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static bool LinkTargets(HalLink link, Guid itemId)
    {
        string path = link.Href!.Split(['?', '#'], 2)[0];
        string? finalSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return Guid.TryParse(finalSegment, out Guid parsed) && parsed == itemId;
    }

    private static bool TryRequiredGuid(JsonElement source, string name, out Guid value)
    {
        value = Guid.Empty;
        return source.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && property.TryGetGuid(out value)
            && value != Guid.Empty;
    }

    private static bool TryRequiredGuid(Guid? direct, JsonElement source, string name, out Guid value)
    {
        value = direct.GetValueOrDefault();
        return value != Guid.Empty || TryRequiredGuid(source, name, out value);
    }

    private static bool TryRequiredInt(JsonElement source, string name, out int value)
    {
        value = default;
        return source.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryRequiredString(JsonElement source, string name, out string value)
    {
        value = string.Empty;
        if (!source.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryRequiredString(string? direct, JsonElement source, string name, out string value)
    {
        value = direct ?? string.Empty;
        return direct is not null || TryRequiredString(source, name, out value);
    }

    private static Guid? OptionalGuid(JsonElement source, string name) =>
        source.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
        && property.TryGetGuid(out Guid value)
        && value != Guid.Empty
            ? value
            : null;

    private static Guid? OptionalGuid(Guid? direct, JsonElement source, string name) =>
        direct is { } value && value != Guid.Empty
            ? value
            : OptionalGuid(source, name);

    private static int? OptionalInt(JsonElement source, string name) =>
        source.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out int value)
            ? value
            : null;

    private static int? OptionalInt(int? direct, JsonElement source, string name) =>
        direct ?? OptionalInt(source, name);

    private static long? OptionalLong(JsonElement source, string name) =>
        source.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt64(out long value)
            ? value
            : null;

    private static bool OptionalBool(JsonElement source, string name) =>
        source.TryGetProperty(name, out JsonElement property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
        && property.GetBoolean();

    private static string? OptionalString(JsonElement source, string name) =>
        source.TryGetProperty(name, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? OptionalString(string? direct, JsonElement source, string name) =>
        direct ?? OptionalString(source, name);

    private static EventTicketCatalogPaidPreflightState? ParsePreflight(
        PaidEventPublicationPreflightDto? direct,
        JsonElement root)
    {
        if (direct is not null)
        {
            return new EventTicketCatalogPaidPreflightState(
                direct.IsPaidCatalog == true,
                direct.IsReady == true,
                direct.Blockers?.Select(ParseBlocker).OfType<EventTicketCatalogPaidPreflightBlockerState>().ToArray() ?? []);
        }

        if (!root.TryGetProperty("publicationPreflight", out JsonElement preflight)
            || preflight.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new EventTicketCatalogPaidPreflightState(
            OptionalBool(preflight, "isPaidCatalog"),
            OptionalBool(preflight, "isReady"),
            ParseBlockers(preflight));
    }

    private static IReadOnlyList<EventTicketCatalogPaidPreflightBlockerState> ParseBlockers(JsonElement preflight)
    {
        if (!preflight.TryGetProperty("blockers", out JsonElement blockers)
            || blockers.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parsed = new List<EventTicketCatalogPaidPreflightBlockerState>();
        foreach (JsonElement blocker in blockers.EnumerateArray())
        {
            string? code = OptionalString(blocker, "code");
            string? explanation = OptionalString(blocker, "explanation");
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(explanation))
            {
                parsed.Add(new EventTicketCatalogPaidPreflightBlockerState(code, explanation));
            }
        }

        return parsed;
    }

    private static EventTicketCatalogPaidPreflightBlockerState? ParseBlocker(Blockers2 blocker) =>
        !string.IsNullOrWhiteSpace(blocker.Code) && !string.IsNullOrWhiteSpace(blocker.Explanation)
            ? new EventTicketCatalogPaidPreflightBlockerState(blocker.Code, blocker.Explanation)
            : null;
}

public sealed record EventTicketCatalogPaidPreflightState(
    bool IsPaidCatalog,
    bool IsReady,
    IReadOnlyList<EventTicketCatalogPaidPreflightBlockerState> Blockers);

public sealed record EventTicketCatalogPaidPreflightBlockerState(string Code, string Explanation);

public sealed record EventTicketTypeState(
    Guid Id,
    string Name,
    int TicketPricingModeId,
    string? TicketPricingModeCode,
    string? TicketPricingModeName,
    long? FixedPriceMinor,
    long? MinimumPriceMinor,
    long? SuggestedPriceMinor,
    int ParticipantDataCollectionModeId,
    string? ParticipantDataCollectionModeCode,
    string? ParticipantDataCollectionModeName,
    Guid? CapacityPoolId,
    int? MinimumAge,
    int? MaximumAge,
    bool RequiresGuardian,
    bool RequiresApproval,
    int? PerOrderLimit,
    int? PerAccountLimit,
    int? PerVerifiedContactLimit,
    int? PerBookingPartyLimit,
    IReadOnlyList<TicketTypeEntitlementState> Entitlements,
    IReadOnlyDictionary<string, HalLink> Links)
{
    public bool HasLink(string relation) => Links.ContainsKey(relation);

    public ManageEventTicketTypeDto ToRequest() => new()
    {
        Name = Name,
        TicketPricingModeId = TicketPricingModeId,
        FixedPriceMinor = FixedPriceMinor,
        MinimumPriceMinor = MinimumPriceMinor,
        SuggestedPriceMinor = SuggestedPriceMinor,
        ParticipantDataCollectionModeId = ParticipantDataCollectionModeId,
        CapacityPoolId = CapacityPoolId,
        MinimumAge = MinimumAge,
        MaximumAge = MaximumAge,
        RequiresGuardian = RequiresGuardian,
        RequiresApproval = RequiresApproval,
        PerOrderLimit = PerOrderLimit,
        PerAccountLimit = PerAccountLimit,
        PerVerifiedContactLimit = PerVerifiedContactLimit,
        PerBookingPartyLimit = PerBookingPartyLimit,
        Entitlements = Entitlements.Select(item => item.ToRequest()).ToArray()
    };
}

public sealed record TicketTypeEntitlementState(
    int EntitlementScopeTypeId,
    string? EntitlementScopeTypeCode,
    string? EntitlementScopeTypeName,
    Guid? EventDayId,
    Guid? EventSessionId,
    int IncludedQuantity,
    int EntitlementSelectionRuleId,
    string? EntitlementSelectionRuleCode,
    string? EntitlementSelectionRuleName)
{
    public ManageTicketTypeEntitlementDto ToRequest() => new()
    {
        EntitlementScopeTypeId = EntitlementScopeTypeId,
        EventDayId = EventDayId,
        EventSessionId = EventSessionId,
        IncludedQuantity = IncludedQuantity,
        EntitlementSelectionRuleId = EntitlementSelectionRuleId
    };
}

public sealed record EventCapacityPoolState(
    Guid Id,
    string Name,
    int? MaximumQuantity,
    int HoldDurationSeconds,
    int CapacityHoldPolicyId,
    string? CapacityHoldPolicyCode,
    string? CapacityHoldPolicyName,
    int CapacityOversellPolicyId,
    string? CapacityOversellPolicyCode,
    string? CapacityOversellPolicyName,
    bool IsActive,
    IReadOnlyDictionary<string, HalLink> Links)
{
    public bool HasLink(string relation) => Links.ContainsKey(relation);

    public ManageEventCapacityPoolDto ToRequest() => new()
    {
        Name = Name,
        MaximumQuantity = MaximumQuantity,
        HoldDurationSeconds = HoldDurationSeconds,
        CapacityHoldPolicyId = CapacityHoldPolicyId,
        CapacityOversellPolicyId = CapacityOversellPolicyId,
        IsActive = IsActive
    };
}
