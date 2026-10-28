using System.Text.Json.Serialization;
using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>Explicit human operational capacity; not permission, retention authority or a service mandate.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModerationOperationalBudgetTerms(
    ModerationSubjectKind SubjectKind, ModerationOperationalAction Action, int MaximumOccurrences, TimeSpan Window)
{
    public void Validate()
    {
        if (SubjectKind != ModerationSubjectKind.Human || !Enum.IsDefined(Action)
            || MaximumOccurrences <= 0 || Window <= TimeSpan.Zero || Window == TimeSpan.MaxValue)
            throw new ArgumentException("Operational budgets require a human subject, defined action and explicit finite positive capacity and window.");
    }
}

/// <summary>
/// Immutable tenant/action budget terms recorded for separate acceptance by the named independent
/// operator. Recording or accepting these terms confers no native admission or counter authority.
/// </summary>
public sealed class ModerationOperationalBudgetSource
{
    private ModerationOperationalBudgetSource() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public ModerationOperationalAction Action { get; private set; }
    public int MaximumOccurrences { get; private set; }
    public long WindowTicks { get; private set; }
    public Guid OperatorHumanId { get; private set; }
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public ModerationOperationalBudgetTerms Terms => new(SubjectKind, Action, MaximumOccurrences, TimeSpan.FromTicks(WindowTicks));

    public static ModerationOperationalBudgetSource Record(
        Guid id, Guid hostId, Guid trustId, Guid tenantId, ModerationOperationalBudgetTerms terms,
        Guid operatorHumanId, Guid recorder, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(terms);
        terms.Validate();
        var source = new ModerationOperationalBudgetSource
        {
            Id = id,
            HostBindingId = hostId,
            TrustDomainId = trustId,
            TenantId = tenantId,
            SubjectKind = terms.SubjectKind,
            Action = terms.Action,
            MaximumOccurrences = terms.MaximumOccurrences,
            WindowTicks = terms.Window.Ticks,
            OperatorHumanId = operatorHumanId,
            RecordedByHumanId = recorder,
            WriterGeneration = writerGeneration,
            Revision = 1,
            RecordedAt = now,
            ValidUntil = validUntil
        };
        source.ValidatePersistedState();
        return source;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        Terms.Validate();
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (OperatorHumanId == Guid.Empty || RecordedByHumanId == Guid.Empty || OperatorHumanId == RecordedByHumanId
            || Revision != 1 || WriterGeneration is < 1 or long.MaxValue
            || WindowTicks >= RecordedAt.UtcTicks
            || WindowTicks >= DateTimeOffset.MaxValue.UtcTicks - RecordedAt.UtcTicks)
            throw new ArgumentException("Operational budgets require an independent named operator, exact finite generation and representable window.");
    }

    public override string ToString() => nameof(ModerationOperationalBudgetSource);
}
