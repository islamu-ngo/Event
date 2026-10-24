using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Features.ConfigurationManifest.Importing;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Complete server-owned destination allocation for one artifact, selection and mapping.
/// Native definition reservations remain individual optimistic bindings, never authority.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ModerationConfigurationImportReservationSet
{
    public const int MaximumSerializedLength = ModerationConfigurationImportReservation.MaximumSerializedLength;
    public const int MaximumTargets = ConfigurationPortabilityContentLimits.MaximumTenantCount + 1;

    [JsonConstructor]
    public ModerationConfigurationImportReservationSet(
        string targetAuthorityKey, string artifactDigest, string selectionDigest, string mappingDigest,
        IReadOnlyList<ModerationConfigurationReservedTarget> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ConfigurationImportContractGuard.ValidateDigest(artifactDigest, nameof(artifactDigest));
        ConfigurationImportContractGuard.ValidateDigest(selectionDigest, nameof(selectionDigest));
        ConfigurationImportContractGuard.ValidateDigest(mappingDigest, nameof(mappingDigest));
        if (entries.Count is < 1 or > MaximumTargets
            || entries.Any(entry => entry is null || entry.Definition is null
                || !Version7(entry.OperationId)
                || entry.SectionKey is not (ModerationConfigurationImportSections.Instance or ModerationConfigurationImportSections.Tenant)
                || entry.SectionKey == ModerationConfigurationImportSections.Instance
                    && (entry.TargetTenantId is not null || entry.SourceName != "instance" || entry.TargetName != "instance")
                || entry.SectionKey == ModerationConfigurationImportSections.Tenant
                    && (entry.TargetTenantId is not { } tenant || !Version7(tenant))
                || string.IsNullOrWhiteSpace(entry.SourceName) || entry.SourceName.Length > 200
                || string.IsNullOrWhiteSpace(entry.TargetName) || entry.TargetName.Length > 200)
            || entries.Select(entry => (entry.SectionKey, entry.SourceName)).Distinct().Count() != entries.Count
            || entries.Select(entry => entry.TargetTenantId).Distinct().Count() != entries.Count
            || entries.Select(entry => (entry.SectionKey, entry.TargetName)).Distinct().Count() != entries.Count)
            throw new ArgumentException("Invalid complete moderation reservation.");
        var identities = entries.SelectMany(entry => new[]
            { entry.OperationId, entry.Definition.PolicyId, entry.Definition.PublicationId }
            .Concat(entry.Definition.Profiles.Select(profile => profile.ProfileId))).ToArray();
        if (identities.Distinct().Count() != identities.Length
            || targetAuthorityKey != "instance"
                && (entries.Count != 1 || entries[0].SectionKey != ModerationConfigurationImportSections.Tenant
                    || targetAuthorityKey != ConfigurationImportTarget.ForTenant(entries[0].TargetTenantId!.Value).AuthorityKey))
            throw new ArgumentException("Moderation reservation identities overlap or escape the target.");
        TargetAuthorityKey = targetAuthorityKey;
        ArtifactDigest = artifactDigest;
        SelectionDigest = selectionDigest;
        MappingDigest = mappingDigest;
        Entries = entries.OrderBy(entry => entry.SectionKey, StringComparer.Ordinal)
            .ThenBy(entry => entry.TargetTenantId).ToImmutableArray();
        _ = Serialize();
    }

    public string TargetAuthorityKey { get; }
    public string ArtifactDigest { get; }
    public string SelectionDigest { get; }
    public string MappingDigest { get; }
    public IReadOnlyList<ModerationConfigurationReservedTarget> Entries { get; }
    [JsonIgnore]
    public string Digest => ConfigurationImportDigest.ComputeBytes(Encoding.UTF8.GetBytes(Serialize()));

    public string Serialize()
    {
        var json = JsonSerializer.Serialize(this, ModerationConfigurationReservationSetJsonContext.Default.ModerationConfigurationImportReservationSet);
        if (json.Length > MaximumSerializedLength)
            throw new ArgumentException("Complete moderation reservation exceeds its storage bound.");
        return json;
    }

    public static ModerationConfigurationImportReservationSet Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumSerializedLength)
            throw new ArgumentException("Complete moderation reservation exceeds its storage bound.");
        return JsonSerializer.Deserialize(json, ModerationConfigurationReservationSetJsonContext.Default.ModerationConfigurationImportReservationSet)
            ?? throw new JsonException("Complete moderation reservation is missing.");
    }

    public override string ToString() => nameof(ModerationConfigurationImportReservationSet);
    private static bool Version7(Guid id) => id.Version == 7 && id.Variant is >= 8 and <= 11;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationConfigurationReservedTarget(
    string SectionKey, string SourceName, string TargetName, Guid? TargetTenantId,
    Guid OperationId, ModerationConfigurationImportReservation Definition);

public sealed record ModerationConfigurationImportSource(
    string SectionKey, string SourceName, string TargetName, ModerationConfigurationDocument Document);

public sealed record ModerationConfigurationTargetPreview(
    ModerationConfigurationReservedTarget Target, ModerationConfigurationImportPreview Preview);

public sealed class ModerationConfigurationImportSetPreview(
    ModerationConfigurationImportReservationSet reservation,
    IEnumerable<ModerationConfigurationTargetPreview> targets)
{
    public ModerationConfigurationImportReservationSet Reservation { get; } = reservation;
    public ImmutableArray<ModerationConfigurationTargetPreview> Targets { get; } = targets.ToImmutableArray();
    public bool CanApply => Targets.Length == Reservation.Entries.Count && Targets.All(target => target.Preview.CanApply);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ModerationConfigurationImportReservationSet))]
internal sealed partial class ModerationConfigurationReservationSetJsonContext : JsonSerializerContext;
