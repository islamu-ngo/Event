using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable original explanation, captured alongside the committing effect. SnapshotJson is the
/// versioned typed notice snapshot, not an outbox payload or a serialization of a finding.
/// </summary>
public sealed class ModerationNoticeSource
{
    private ModerationNoticeSource() { }

    public Guid EffectId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid DecisionId { get; private set; }
    public long DecisionRevision { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid? TenantId { get; private set; }
    public int ScopeKind { get; private set; }
    public Guid ScopeId { get; private set; }
    public int SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;
    public string SnapshotHash { get; private set; } = string.Empty;
    public string EffectBindingHash { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset? RetainUntil { get; private set; }
    // A minimized projection of the same immutable capture, deleted with this source at R.
    // It is not a second retained record or a notification receipt.
    public string? StatusMetadataJson { get; private set; }
    public string? StatusMetadataHash { get; private set; }

    public static ModerationNoticeSource Capture(
        ModerationEffectIntent effect, Guid? tenantId, string snapshotJson, DateTimeOffset? retainUntil)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(snapshotJson)
            || Encoding.UTF8.GetByteCount(snapshotJson) > 128 * 1024
            || effect.CompletedAt is not null
            || retainUntil is { } deadline && (deadline.Offset != TimeSpan.Zero
                || deadline == DateTimeOffset.MaxValue || deadline <= effect.CreatedAt))
            throw new ArgumentException("Capture requires a pending effect and a bounded typed snapshot.");
        var source = new ModerationNoticeSource
        {
            EffectId = effect.Id,
            OperationId = effect.OperationId,
            DecisionId = effect.DecisionId,
            DecisionRevision = effect.DecisionRevision,
            TrustDomainId = effect.TrustDomainId,
            TenantId = tenantId,
            ScopeKind = effect.ScopeKind,
            ScopeId = effect.ScopeId,
            SubjectKind = effect.SubjectKind,
            SubjectId = effect.SubjectId,
            SnapshotJson = snapshotJson,
            SnapshotHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson))),
            EffectBindingHash = BindingHash(effect, retainUntil),
            CapturedAt = effect.CreatedAt,
            RetainUntil = retainUntil
        };
        var metadata = source.DeriveStatusMetadata(effect);
        if (metadata is not null)
        {
            source.StatusMetadataJson = JsonSerializer.Serialize(metadata);
            source.StatusMetadataHash = Hash(source.StatusMetadataJson);
        }
        return source;
    }

    public bool Matches(ModerationEffectIntent effect) =>
        EffectId == effect.Id && OperationId == effect.OperationId
        && DecisionId == effect.DecisionId && DecisionRevision == effect.DecisionRevision
        && TrustDomainId == effect.TrustDomainId && ScopeKind == effect.ScopeKind && ScopeId == effect.ScopeId
        && SubjectKind == effect.SubjectKind && SubjectId == effect.SubjectId && CapturedAt == effect.CreatedAt
        && EffectBindingHash == BindingHash(effect, RetainUntil)
        && (RetainUntil is null || RetainUntil is { Offset: var offset } && offset == TimeSpan.Zero
            && RetainUntil != DateTimeOffset.MaxValue && RetainUntil > CapturedAt)
        && !string.IsNullOrWhiteSpace(SnapshotJson) && Encoding.UTF8.GetByteCount(SnapshotJson) <= 128 * 1024
        && SnapshotHash == Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SnapshotJson)))
        && StatusMetadataMatchesCapture(effect);

    public bool HasContentRepresentative(Guid humanId)
    {
        if (SubjectKind != (int)ModerationSubjectKind.Content || humanId == Guid.Empty) return false;
        try
        {
            using var document = JsonDocument.Parse(SnapshotJson);
            var capture = document.RootElement;
            return capture.GetProperty("Version").GetInt32() == 2
                && capture.GetProperty("WriterGeneration").GetString() == "event.moderation.capture.v2"
                && capture.GetProperty("Decision").GetProperty("SubjectKind").GetInt32() == SubjectKind
                && capture.GetProperty("Decision").GetProperty("SubjectId").GetGuid() == SubjectId
                && capture.GetProperty("ContentRepresentatives").EnumerateArray().Any(row =>
                    row.GetProperty("HumanId").GetGuid() == humanId
                    && row.GetProperty("AssignmentId").GetGuid() != Guid.Empty
                    && row.GetProperty("AssignmentRevision").GetInt64() > 0);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private bool StatusMetadataMatchesCapture(ModerationEffectIntent effect)
    {
        var metadata = DeriveStatusMetadata(effect);
        return metadata is null
            ? StatusMetadataJson is null && StatusMetadataHash is null
            : StatusMetadataJson == JsonSerializer.Serialize(metadata) && StatusMetadataHash == Hash(StatusMetadataJson);
    }

    private StatusMetadata? DeriveStatusMetadata(ModerationEffectIntent effect)
    {
        if (RetainUntil is not { } ceiling
            || SubjectKind is not ((int)ModerationSubjectKind.Human) and not ((int)ModerationSubjectKind.Content))
            return null;
        try
        {
            using var document = JsonDocument.Parse(SnapshotJson);
            var capture = document.RootElement;
            var decision = capture.GetProperty("Decision");
            var notice = capture.GetProperty("Notice");
            if (capture.GetProperty("Version").GetInt32() != 2
                || capture.GetProperty("WriterGeneration").GetString() != "event.moderation.capture.v2"
                || capture.GetProperty("QualificationRevision").GetInt64() != effect.QualificationRevision
                || notice.ValueKind != JsonValueKind.Object
                || notice.GetProperty("Version").GetInt32() != 1
                || decision.GetProperty("Id").GetGuid() != DecisionId
                || decision.GetProperty("Revision").GetInt64() != DecisionRevision
                || decision.GetProperty("SubjectKind").GetInt32() != SubjectKind
                || decision.GetProperty("SubjectId").GetGuid() != SubjectId
                || notice.GetProperty("EffectId").GetGuid() != EffectId
                || notice.GetProperty("OperationId").GetGuid() != OperationId
                || notice.GetProperty("DecisionId").GetGuid() != DecisionId
                || notice.GetProperty("DecisionRevision").GetInt64() != DecisionRevision
                || notice.GetProperty("SubjectKind").GetInt32() != SubjectKind
                || notice.GetProperty("SubjectId").GetGuid() != SubjectId
                || notice.GetProperty("PolicyRevision").GetInt64() != effect.PolicyRevision
                || notice.GetProperty("CapturedAt").GetDateTimeOffset() != CapturedAt
                || notice.GetProperty("RetainUntil").GetDateTimeOffset() != ceiling)
                return null;
            var months = notice.GetProperty("ContestationCalendarMonths").GetInt32();
            var host = capture.GetProperty("HostBindingId").GetGuid();
            var writer = capture.GetProperty("WriterTenantId").GetGuid();
            var caseId = decision.GetProperty("CaseId").GetGuid();
            var instance = AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(TrustDomainId), capture.GetProperty("HostSolutionId").GetGuid()),
                capture.GetProperty("HostInstanceId").GetGuid());
            var origin = ScopeText(decision.GetProperty("SubjectOrigin"));
            var scope = decision.GetProperty("Scope");
            var representatives = SubjectKind == (int)ModerationSubjectKind.Content
                ? JsonSerializer.Deserialize<ModerationNoticeContentRepresentative[]>(
                    capture.GetProperty("ContentRepresentatives").GetRawText())?.ToImmutableArray() ?? default
                : ImmutableArray<ModerationNoticeContentRepresentative>.Empty;
            if (host == Guid.Empty || writer == Guid.Empty || caseId == Guid.Empty
                || origin != ScopeText(SubjectKind == (int)ModerationSubjectKind.Human
                    ? instance : AuthorityScope.Tenant(instance, writer))
                || representatives.IsDefault || representatives.Length > 32
                || SubjectKind == (int)ModerationSubjectKind.Content && representatives.IsEmpty
                || !RepresentativesValid(representatives)
                || ScopeText(notice.GetProperty("SubjectOrigin")) != origin
                || ScopeText(notice.GetProperty("Scope")) != ScopeText(scope)
                || scope.GetProperty("Kind").GetInt32() != ScopeKind
                || scope.GetProperty("Id").GetGuid() != ScopeId
                || !ModerationNoticeRetention.CanNotifyAt(CapturedAt, months, ceiling))
                return null;
            return new(host, writer, caseId, months, Hash(ScopeText(scope)), Hash(origin),
                SnapshotHash, BindingHash(effect, RetainUntil))
            { ContentRepresentatives = representatives };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            or InvalidOperationException or FormatException or ArgumentException)
        {
            // Inert, unsupported or corrupt captures do not acquire status proof.
            return null;
        }
    }

    /// <summary>
    /// Transient metadata of the original hashed capture, not a separately retained entity.
    /// Status selects only this small value; it never selects the private original JSON.
    /// </summary>
    public sealed record StatusMetadata(
        Guid HostBindingId, Guid WriterTenantId, Guid CaseId, int ContestationCalendarMonths,
        string ScopeBinding, string SubjectOriginBinding, string SnapshotHash, string EffectBindingHash)
    {
        public ImmutableArray<ModerationNoticeContentRepresentative> ContentRepresentatives { get; init; } = [];

        public bool Matches(ModerationEffectIntent effect, ModerationDecision decision,
            ModerationHostBinding host, Guid tenantId, DateTimeOffset retainUntil) =>
            HostBindingId == host.Id && WriterTenantId == tenantId && CaseId == decision.CaseId
            && effect.TrustDomainId == host.TrustDomainId && effect.DecisionId == decision.Id
            && ScopeBinding == Hash(ScopeText(decision.Scope))
            && SubjectOriginBinding == Hash(ScopeText(decision.Subject.Origin))
            && SnapshotHash.Length == 64 && SnapshotHash.All(char.IsAsciiHexDigitLower)
            && EffectBindingHash == BindingHash(effect, retainUntil)
            && (decision.SubjectKind == ModerationSubjectKind.Content
                ? !ContentRepresentatives.IsDefaultOrEmpty : ContentRepresentatives.IsEmpty)
            && RepresentativesValid(ContentRepresentatives)
            && ModerationNoticeRetention.CanNotifyAt(effect.CreatedAt, ContestationCalendarMonths, retainUntil);

        public static StatusMetadata? Read(string? json, string? hash)
        {
            if (json is null || json.Length > 8192 || hash != Hash(json)) return null;
            try
            {
                var metadata = JsonSerializer.Deserialize<StatusMetadata>(json);
                return metadata is not null && metadata.HostBindingId != Guid.Empty
                    && metadata.WriterTenantId != Guid.Empty && metadata.CaseId != Guid.Empty
                    && metadata.ContestationCalendarMonths is >= 0 and <= 120
                    && IsHash(metadata.ScopeBinding) && IsHash(metadata.SubjectOriginBinding)
                    && IsHash(metadata.SnapshotHash) && IsHash(metadata.EffectBindingHash)
                    && RepresentativesValid(metadata.ContentRepresentatives) ? metadata : null;
            }
            catch (JsonException) { return null; }
        }

        private static bool IsHash(string? value) =>
            value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

        public override string ToString() => nameof(StatusMetadata);
    }

    private static bool RepresentativesValid(ImmutableArray<ModerationNoticeContentRepresentative> representatives) =>
        !representatives.IsDefault && representatives.Length <= 32
        && representatives.All(row => row is not null && row.AssignmentId != Guid.Empty
            && row.AssignmentId.Version == 7 && row.AssignmentId.Variant is >= 8 and <= 11
            && row.AssignmentRevision is >= 1 and < long.MaxValue && row.HumanId != Guid.Empty)
        && representatives.Select(row => row.AssignmentId).Distinct().Count() == representatives.Length
        && representatives.Select(row => row.HumanId).Distinct().Count() == representatives.Length;

    private static string ScopeText(AuthorityScope scope) =>
        $"{(int)scope.Kind}:{scope.Id:D}/{(scope.Parent is null ? string.Empty : ScopeText(scope.Parent))}";

    private static string ScopeText(JsonElement scope) =>
        $"{scope.GetProperty("Kind").GetInt32()}:{scope.GetProperty("Id").GetGuid():D}/"
        + (scope.GetProperty("Parent").ValueKind == JsonValueKind.Null
            ? string.Empty : ScopeText(scope.GetProperty("Parent")));

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string BindingHash(ModerationEffectIntent effect, DateTimeOffset? retainUntil) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            effect.Id,
            effect.OperationId,
            effect.DecisionId,
            effect.DecisionRevision,
            effect.SanctionId,
            effect.SanctionRevision,
            effect.SupersededSanctionId,
            effect.SupersededSanctionRevision,
            effect.TrustDomainId,
            effect.ScopeKind,
            effect.ScopeId,
            effect.SubjectKind,
            effect.SubjectId,
            effect.OperationKind,
            effect.PolicyRevision,
            effect.QualificationRevision,
            effect.CreatedAt,
            RetainUntil = retainUntil
        })));

    public override string ToString() => nameof(ModerationNoticeSource);
}
