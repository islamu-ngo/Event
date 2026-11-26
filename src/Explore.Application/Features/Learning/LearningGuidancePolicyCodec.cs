using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Learning;
using Wire = ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning;

/// <summary>Pure strict document conversion; neither decoding nor defaults confer authority.</summary>
public static class LearningGuidancePolicyCodec
{
    public const int SchemaVersion = 1;
    public const string DefaultsVersion = "learning-guidance:1";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };

    public static LearningGuidancePolicy Decode(LearningGuidanceDocumentSource? source)
    {
        if (source is null) return LearningGuidancePolicy.Default;
        if (source.Id == Guid.Empty || source.TenantId == Guid.Empty || source.ConcurrencyStamp == Guid.Empty
            || source.SchemaVersion != SchemaVersion || source.DefaultsVersion != DefaultsVersion)
            throw new InvalidOperationException("The stored learning guidance document version or identity is invalid.");
        try
        {
            using var json = JsonDocument.Parse(source.PayloadJson);
            RequireClosedObject(json.RootElement,
                ["enabled", "automaticInvitations", "invitationAudience", "manualAccessAudience",
                    "requirementPolicy", "familyOverrides"]);
            if (json.RootElement.GetProperty("familyOverrides").ValueKind != JsonValueKind.Array)
                throw new JsonException("Family overrides must be an array.");
            foreach (var item in json.RootElement.GetProperty("familyOverrides").EnumerateArray())
                RequireClosedObject(item, ["family", "enabled", "automaticInvitations"],
                    ["invitationAudience", "manualAccessAudience"]);
            return FromPayload(json.RootElement.Deserialize<Wire.LearningGuidancePolicyPayload>(Options)
                ?? throw new JsonException("A policy object is required."));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidOperationException("The stored learning guidance policy is malformed.", exception);
        }
    }

    public static LearningGuidancePolicy FromPayload(Wire.LearningGuidancePolicyPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payload.FamilyOverrides);
        if (payload.FamilyOverrides.Count > 4 || payload.FamilyOverrides.Any(item => item is null))
            throw new ArgumentException("Use at most four non-null family overrides.", nameof(payload));
        return new(payload.Enabled, payload.AutomaticInvitations,
            (LearningGuidanceAudience)payload.InvitationAudience,
            (LearningGuidanceAudience)payload.ManualAccessAudience,
            (LearningRequirementPolicy)payload.RequirementPolicy,
            payload.FamilyOverrides.Select(item => new LearningGuidanceFamilyOverride(
                (LearningTourFamily)item.Family, item.Enabled, item.AutomaticInvitations,
                (LearningGuidanceAudience?)item.InvitationAudience,
                (LearningGuidanceAudience?)item.ManualAccessAudience)).ToArray());
    }

    public static Wire.LearningGuidancePolicyPayload ToPayload(LearningGuidancePolicy policy) => new()
    {
        Enabled = policy.Enabled,
        AutomaticInvitations = policy.AutomaticInvitations,
        InvitationAudience = (Wire.LearningGuidanceAudience)policy.InvitationAudience,
        ManualAccessAudience = (Wire.LearningGuidanceAudience)policy.ManualAccessAudience,
        RequirementPolicy = (Wire.LearningRequirementPolicy)policy.RequirementPolicy,
        FamilyOverrides = policy.FamilyOverrides.OrderBy(item => item.Family).Select(item =>
            new Wire.LearningGuidanceFamilyOverrideDto
            {
                Family = (Wire.LearningTourFamily)item.Family,
                Enabled = item.Enabled,
                AutomaticInvitations = item.AutomaticInvitations,
                InvitationAudience = (Wire.LearningGuidanceAudience?)item.InvitationAudience,
                ManualAccessAudience = (Wire.LearningGuidanceAudience?)item.ManualAccessAudience
            }).ToArray()
    };

    /// <summary>Canonical semantic image, independent of provider JSON whitespace and property order.</summary>
    public static string Encode(LearningGuidancePolicy policy) =>
        JsonSerializer.Serialize(ToPayload(policy), Options);

    public static Wire.LearningGuidancePolicyDto ToDto(ResolvedLearningGuidancePolicy resolved,
        bool isCurrentTenantAdministrator) => new()
        {
            SchemaVersion = resolved.SchemaVersion,
            DefaultsVersion = resolved.DefaultsVersion,
            ConcurrencyStamp = resolved.ConcurrencyStamp,
            Policy = ToPayload(resolved.Policy),
            CanEdit = isCurrentTenantAdministrator && resolved.AllowTenantEdits
        };

    private static void RequireClosedObject(JsonElement value, string[] required, string[]? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new JsonException("A policy object is required.");
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !required.Contains(property.Name, StringComparer.Ordinal)
                && optional?.Contains(property.Name, StringComparer.Ordinal) != true)
                throw new JsonException("Unknown or repeated policy property.");
        if (required.Any(name => !seen.Contains(name)))
            throw new JsonException("A required policy property is missing.");
    }
}
