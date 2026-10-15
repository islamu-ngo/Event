using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Native, payload-free custody index for supplied allegation material. Deduplication is rooted
/// in the exact native object/version and supplied occurrence/material, never the reporter,
/// mailbox, IP address or an alleged human. A local incident owns its own target identity.
/// </summary>
public sealed class ModerationCaseTargetSource
{
    private ModerationCaseTargetSource() { }
    public Guid Id { get; private set; }
    public Guid Version { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid? EventId { get; private set; }
    public Guid? EventVersion { get; private set; }
    public ModerationCaseTargetKind Kind { get; private set; }
    public ModerationCaseTargetKind NativeKind { get; private set; }
    public Guid NativeTargetId { get; private set; }
    public Guid NativeTargetVersion { get; private set; }
    public string NativeFactsDigest { get; private set; } = string.Empty;
    public Guid OccurrenceId { get; private set; }
    public Guid OccurrenceVersion { get; private set; }
    public Guid MaterialFactId { get; private set; }
    public string OccurrenceDigest { get; private set; } = string.Empty;
    public string MaterialDigest { get; private set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; private set; }

    public static ModerationCaseTargetSource Record(
        Guid id, Guid version, Guid tenantId, Guid trustDomainId, Guid? eventId, Guid? eventVersion,
        ModerationCaseTargetKind kind, ModerationCaseTargetKind nativeKind, Guid nativeTargetId,
        Guid nativeTargetVersion, string nativeFactsDigest, Guid occurrenceId, Guid occurrenceVersion, Guid materialFactId,
        string occurrenceDigest, string materialDigest, DateTimeOffset now)
    {
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        if (version == Guid.Empty || tenantId == Guid.Empty || trustDomainId == Guid.Empty
            || eventId == Guid.Empty || eventVersion == Guid.Empty || (eventId is null) != (eventVersion is null)
            || eventId is null && kind is not (ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service)
            || nativeTargetId == Guid.Empty
            || nativeTargetVersion == Guid.Empty || occurrenceId == Guid.Empty || occurrenceVersion == Guid.Empty
            || materialFactId == Guid.Empty || now == default || !Enum.IsDefined(kind) || !Enum.IsDefined(nativeKind)
            || nativeKind is ModerationCaseTargetKind.HiddenContentReceipt or ModerationCaseTargetKind.LocalIncident
            || kind == ModerationCaseTargetKind.LocalIncident && (nativeKind != ModerationCaseTargetKind.EventVersion
                || nativeTargetId != eventId)
            || kind is not (ModerationCaseTargetKind.LocalIncident or ModerationCaseTargetKind.HiddenContentReceipt)
                && kind != nativeKind
            || !IsDigest(nativeFactsDigest) || !IsDigest(occurrenceDigest) || !IsDigest(materialDigest))
            throw new ArgumentException("Material provenance requires exact native source coordinates and digests.");
        return new()
        {
            Id = id, Version = version, TenantId = tenantId, TrustDomainId = trustDomainId,
            EventId = eventId, EventVersion = eventVersion, Kind = kind, NativeKind = nativeKind,
            NativeTargetId = nativeTargetId, NativeTargetVersion = nativeTargetVersion,
            NativeFactsDigest = nativeFactsDigest,
            OccurrenceId = occurrenceId, OccurrenceVersion = occurrenceVersion, MaterialFactId = materialFactId,
            OccurrenceDigest = occurrenceDigest, MaterialDigest = materialDigest, RecordedAt = now
        };
    }

    public ModerationEvidenceReference Evidence(AuthorityScope tenant)
    {
        if (tenant.Kind != ModerationScopeKind.Tenant || tenant.Id != TenantId || tenant.TrustDomainId != TrustDomainId)
            throw new ArgumentException("Source ancestry does not match the native tenant.", nameof(tenant));
        var origin = Kind is ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service ? tenant.Parent! : tenant;
        var target = Kind switch
        {
            ModerationCaseTargetKind.LocalIncident or ModerationCaseTargetKind.HiddenContentReceipt =>
                ModerationCaseTarget.Create(Kind, OccurrenceId, origin, OccurrenceVersion),
            _ => ModerationCaseTarget.Create(Kind, NativeTargetId, origin, NativeTargetVersion)
        };
        return ModerationEvidenceReference.Create(target, tenant, OccurrenceId, Id, Version, MaterialFactId);
    }

    private static bool IsDigest(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigitLower);

    public override string ToString() => nameof(ModerationCaseTargetSource);
}
