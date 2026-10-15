using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Explore.Domain;

/// <summary>
/// Exact native Event owner or manager assignment selected for an original Content explanation.
/// Representation grants no finding against the human and cannot replace the Content subject.
/// </summary>
public sealed record ModerationNoticeContentRepresentative(Guid AssignmentId, long AssignmentRevision, Guid HumanId)
{
    public override string ToString() => nameof(ModerationNoticeContentRepresentative);
}

/// <summary>Authorable subject-safe content for one future native transition, never finding justification or target authority.</summary>
public sealed record ModerationNoticeDisclosureIntent(
    Guid ApprovalId, Guid EffectId, Guid DecisionId, long ExpectedDecisionRevision,
    Guid CaseId, long ExpectedCaseRevision, Guid PolicyId, long ExpectedPolicyRevision,
    Guid PublicationId, Guid ProfileId, Guid ContributionId, string SelectionDigest,
    ModerationNoticeSafeText RuleMeaning, ModerationNoticeSafeText ReasonMeaning,
    ImmutableArray<ModerationNoticeSafeText> Facts, ModerationNoticeSafeText RemainingActions,
    ModerationNoticeSafeText Recourse, ModerationNoticeSafeText Automation, ModerationNoticeSafeText Retention,
    DateTimeOffset ValidUntil, DateTimeOffset RetainUntil)
{
    public ImmutableArray<ModerationNoticeContentRepresentative> ContentRepresentatives { get; init; } = [];

    public void Validate()
    {
        foreach (var id in new[] { ApprovalId, EffectId, DecisionId, CaseId, PolicyId, PublicationId, ProfileId, ContributionId })
            ModerationHostBinding.RequireUuidV7(id, nameof(ApprovalId));
        if (ExpectedDecisionRevision is < 2 or long.MaxValue || ExpectedCaseRevision is < 1 or long.MaxValue
            || ExpectedPolicyRevision is < 1 or long.MaxValue || SelectionDigest is null
            || SelectionDigest.Length != 64 || !SelectionDigest.All(char.IsAsciiHexDigitLower)
            || RuleMeaning is null || ReasonMeaning is null || RemainingActions is null || Recourse is null
            || Automation is null || Retention is null || Facts.IsDefault || Facts.Length > 16
            || Facts.Any(row => row is null)
            || ContentRepresentatives.IsDefault || ContentRepresentatives.Length > 32
            || ContentRepresentatives.Any(row => row is null || row.AssignmentId == Guid.Empty
                || row.AssignmentId.Version != 7 || row.AssignmentId.Variant is < 8 or > 11
                || row.AssignmentRevision is < 1 or long.MaxValue || row.HumanId == Guid.Empty)
            || ContentRepresentatives.Select(row => row.AssignmentId).Distinct().Count() != ContentRepresentatives.Length
            || ContentRepresentatives.Select(row => row.HumanId).Distinct().Count() != ContentRepresentatives.Length
            || ValidUntil == DateTimeOffset.MinValue || ValidUntil == DateTimeOffset.MaxValue
            || ValidUntil.Offset != TimeSpan.Zero || RetainUntil.Offset != TimeSpan.Zero
            || RetainUntil == DateTimeOffset.MinValue || RetainUntil == DateTimeOffset.MaxValue)
            throw new ArgumentException("Disclosure intent requires exact native generations and bounded independently approved plain text.");
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(this)) > 128 * 1024)
            throw new ArgumentException("Approved disclosure content exceeds its bounded native capture.");
    }

    public override string ToString() => nameof(ModerationNoticeDisclosureIntent);
}

/// <summary>
/// Native human approval of exact safe content. Current profile, finding, target sources and
/// delivery remain separate owners; this row does not grant guilt, activation or disclosure access.
/// </summary>
public sealed class ModerationNoticeDisclosureApproval
{
    private ModerationNoticeDisclosureApproval() { }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EffectId { get; private set; }
    public Guid DecisionId { get; private set; }
    public long DecisionRevision { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid ProfileId { get; private set; }
    public Guid ContributionId { get; private set; }
    public string SelectionDigest { get; private set; } = string.Empty;
    public string RuleMeaning { get; private set; } = string.Empty;
    public string ReasonMeaning { get; private set; } = string.Empty;
    public string FactsJson { get; private set; } = string.Empty;
    public string RemainingActions { get; private set; } = string.Empty;
    public string Recourse { get; private set; } = string.Empty;
    public string Automation { get; private set; } = string.Empty;
    public string Retention { get; private set; } = string.Empty;
    public string ContentRepresentativesJson { get; private set; } = "[]";
    public Guid ApprovedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }
    public Guid? RevokedByHumanId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public ModerationNoticeDisclosureIntent Intent => new(
        Id, EffectId, DecisionId, DecisionRevision, CaseId, CaseRevision, PolicyId, PolicyRevision,
        PublicationId, ProfileId, ContributionId, SelectionDigest, new(RuleMeaning), new(ReasonMeaning),
        (JsonSerializer.Deserialize<string[]>(FactsJson)
            ?? throw new ArgumentException("The retained disclosure facts are unavailable."))
            .Select(value => new ModerationNoticeSafeText(value)).ToImmutableArray(),
        new(RemainingActions), new(Recourse), new(Automation), new(Retention), ValidUntil, RetainUntil)
    {
        ContentRepresentatives = (JsonSerializer.Deserialize<ModerationNoticeContentRepresentative[]>(ContentRepresentativesJson)
            ?? throw new ArgumentException("The retained Content representatives are unavailable.")).ToImmutableArray()
    };

    public static ModerationNoticeDisclosureApproval Approve(
        ModerationNoticeDisclosureIntent intent, Guid hostBindingId, Guid trustDomainId,
        Guid tenantId, Guid approvedByHumanId, long writerGeneration, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(intent);
        intent.Validate();
        var row = new ModerationNoticeDisclosureApproval
        {
            Id = intent.ApprovalId, HostBindingId = hostBindingId, TrustDomainId = trustDomainId, TenantId = tenantId,
            EffectId = intent.EffectId, DecisionId = intent.DecisionId, DecisionRevision = intent.ExpectedDecisionRevision,
            CaseId = intent.CaseId, CaseRevision = intent.ExpectedCaseRevision,
            PolicyId = intent.PolicyId, PolicyRevision = intent.ExpectedPolicyRevision, PublicationId = intent.PublicationId,
            ProfileId = intent.ProfileId, ContributionId = intent.ContributionId, SelectionDigest = intent.SelectionDigest,
            RuleMeaning = intent.RuleMeaning.Value, ReasonMeaning = intent.ReasonMeaning.Value,
            FactsJson = JsonSerializer.Serialize(intent.Facts.Select(value => value.Value).ToArray()),
            RemainingActions = intent.RemainingActions.Value, Recourse = intent.Recourse.Value,
            Automation = intent.Automation.Value, Retention = intent.Retention.Value,
            ContentRepresentativesJson = JsonSerializer.Serialize(intent.ContentRepresentatives),
            ApprovedByHumanId = approvedByHumanId, WriterGeneration = writerGeneration, Revision = 1,
            ApprovedAt = now.ToUniversalTime(), ValidUntil = intent.ValidUntil, RetainUntil = intent.RetainUntil
        };
        row.ValidatePersistedState();
        return row;
    }

    public void Revoke(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || RevokedAt is not null)
            throw new InvalidOperationException("Disclosure revocation requires its exact current generation.");
        if (humanId == Guid.Empty || now < ApprovedAt)
            throw new ArgumentException("Disclosure revocation requires a current native human and non-regressing time.");
        Revision = checked(Revision + 1);
        RevokedByHumanId = humanId;
        RevokedAt = now.ToUniversalTime();
    }

    public bool IsCurrentAt(ModerationNoticeDisclosureIntent intent, DateTimeOffset now)
    {
        ValidatePersistedState();
        return RevokedAt is null && now >= ApprovedAt && now < ValidUntil
            && JsonSerializer.Serialize(Intent) == JsonSerializer.Serialize(intent);
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { HostBindingId, TrustDomainId, TenantId })
            ModerationHostBinding.RequireUuidV7(id, nameof(HostBindingId));
        Intent.Validate();
        if (ApprovedByHumanId == Guid.Empty || WriterGeneration is < 1 or long.MaxValue
            || (RevokedAt is null) != (RevokedByHumanId is null) || RevokedByHumanId == Guid.Empty
            || Revision != (RevokedAt is null ? 1 : 2) || RevokedAt < ApprovedAt || RetainUntil <= ApprovedAt
            || RevokedAt is { Offset: var offset } && offset != TimeSpan.Zero)
            throw new ArgumentException("Disclosure approval requires its actual human, writer generation and complete revocation.");
        ModerationQualificationPolicySource.RequireValidity(ApprovedAt, ValidUntil);
    }

    public override string ToString() => nameof(ModerationNoticeDisclosureApproval);
}
