using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Features.ConfigurationManifest.Importing;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Server-owned optimistic reservation persisted with one approved session preview. Only bounded
/// identities and the native binding survive the request; authority and complete history never do.
/// This is not a portable contract or an authorization receipt.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ModerationConfigurationImportReservation
{
    public const int MaximumSerializedLength = 1_100_000;

    [JsonConstructor]
    public ModerationConfigurationImportReservation(
        Guid? baselinePublicationId, Guid policyId, Guid publicationId, string binding,
        IReadOnlyList<ModerationConfigurationReservedProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (baselinePublicationId is { } baseline && !Version7(baseline)
            || !Version7(policyId) || !Version7(publicationId) || policyId == publicationId
            || binding is null || binding.Length != 64 || binding.Any(value => !char.IsAsciiHexDigit(value))
            || profiles.Count > ModerationConfigurationDocument.MaximumDefinitions
            || profiles.Any(profile => profile is null || string.IsNullOrWhiteSpace(profile.Code)
                || profile.Code.Length > 4000 || profile.Revision <= 0 || !Version7(profile.ProfileId)
                || profile.ProfileId == policyId || profile.ProfileId == publicationId)
            || profiles.Select(profile => (profile.Code, profile.Revision)).Distinct().Count() != profiles.Count
            || profiles.Select(profile => profile.ProfileId).Distinct().Count() != profiles.Count)
            throw new ArgumentException("Invalid native moderation import reservation.");
        BaselinePublicationId = baselinePublicationId;
        PolicyId = policyId;
        PublicationId = publicationId;
        Binding = binding;
        Profiles = profiles.OrderBy(profile => profile.Code, StringComparer.Ordinal)
            .ThenBy(profile => profile.Revision).ToImmutableArray();
    }

    public Guid? BaselinePublicationId { get; }
    public Guid PolicyId { get; }
    public Guid PublicationId { get; }
    public string Binding { get; }
    public IReadOnlyList<ModerationConfigurationReservedProfile> Profiles { get; }
    [JsonIgnore]
    public string Digest => ConfigurationImportDigest.ComputeBytes(Encoding.UTF8.GetBytes(Serialize()));

    public string Serialize()
    {
        string value = JsonSerializer.Serialize(this,
            ModerationConfigurationReservationJsonContext.Default.ModerationConfigurationImportReservation);
        if (value.Length > MaximumSerializedLength)
            throw new ArgumentException("Native moderation reservation exceeds its storage bound.");
        return value;
    }

    public static ModerationConfigurationImportReservation Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumSerializedLength)
            throw new ArgumentException("Native moderation reservation exceeds its storage bound.");
        return JsonSerializer.Deserialize(value,
            ModerationConfigurationReservationJsonContext.Default.ModerationConfigurationImportReservation)
            ?? throw new JsonException("Native moderation reservation is missing.");
    }

    public override string ToString() => nameof(ModerationConfigurationImportReservation);

    private static bool Version7(Guid id) => id.Version == 7 && id.Variant is >= 8 and <= 11;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationConfigurationReservedProfile(string Code, long Revision, Guid ProfileId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ModerationConfigurationImportReservation))]
internal sealed partial class ModerationConfigurationReservationJsonContext : JsonSerializerContext;
