using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationContributionSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task DraftSelection_SnapshotsEveryBindingWithoutApprovingNativeTargets()
    {
        var (selection, bindings) = Selection();
        selection.ValidatePersistedState();
        bindings.Clear();
        await Assert.That(selection.Bindings.Count).IsEqualTo(2);
        await Assert.That(selection.Bindings.Single(row => row.Kind == ModerationContributionBindingKind.Effect).Effect)
            .IsEqualTo(ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06).Effects[0]);
        await Assert.That(selection.Bindings.Single(row => row.Kind == ModerationContributionBindingKind.Consequence).Strategy).IsNull();
        await Assert.That(selection.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Binding_RejectsMixedMeaningSentinelGenerationOrUnqualifiedOperation(int invalid)
    {
        var (selection, _) = Selection();
        var original = selection.Bindings[0];
        var binding = new ModerationContributionBinding
        {
            SelectionId = original.SelectionId, Ordinal = original.Ordinal, Kind = original.Kind,
            EffectKind = original.EffectKind, EffectMode = original.EffectMode, EffectTarget = original.EffectTarget,
            Capability = original.Capability, Execution = original.Execution,
            Strategy = invalid == 0 ? ModerationAdditiveStrategyKind.PublicationIntegrity : null,
            Scope = original.Scope, SubjectKind = original.SubjectKind, SubjectId = original.SubjectId,
            TargetId = original.TargetId, TargetRevision = invalid == 1 ? long.MaxValue : original.TargetRevision,
            Operation = invalid == 2 ? "resource/read all" : original.Operation, WindowEnd = original.WindowEnd
        };
        await Assert.That(() => binding.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    public async Task Draft_selection_requires_a_canonical_author()
    {
        var selection = Selection().Selection;
        typeof(ModerationContributionSelection).GetProperty(nameof(selection.AuthoredByHumanId))!
            .SetValue(selection, Guid.NewGuid());
        await Assert.That(selection.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("selection")]
    [Arguments("trust-domain")]
    public async Task Bindings_cannot_be_transplanted_to_another_selection_or_trust_domain(string coordinate)
    {
        var selection = Selection().Selection;
        var binding = selection.Bindings[0];
        typeof(ModerationContributionBinding).GetProperty(coordinate == "selection"
            ? nameof(binding.SelectionId) : nameof(binding.ScopeTrustDomainId))!
            .SetValue(binding, Guid.CreateVersion7());
        binding.ValidatePersistedState();
        await Assert.That(selection.ValidatePersistedState).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("duplicate")]
    [Arguments("gap")]
    [Arguments("missing-effect")]
    public async Task Draft_selection_requires_complete_contiguous_effect_and_consequence_partitions(string defect)
    {
        var selection = Selection().Selection;
        if (defect == "missing-effect")
            typeof(ModerationContributionBinding).GetProperty(nameof(ModerationContributionBinding.Kind))!
                .SetValue(selection.Bindings[0], ModerationContributionBindingKind.Consequence);
        else
            typeof(ModerationContributionBinding).GetProperty(nameof(ModerationContributionBinding.Ordinal))!
                .SetValue(selection.Bindings[1], defect == "duplicate" ? 0 : 2);

        await Assert.That(selection.ValidatePersistedState).Throws<ArgumentException>();
    }

    internal static (ModerationContributionSelection Selection, List<ModerationContributionBinding> Bindings) Selection(
        ModerationScopeKind scopeKind = ModerationScopeKind.Tenant,
        ModerationSubjectKind subjectKind = ModerationSubjectKind.Human, bool substituteEvent = false)
    {
        var tenant = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        var targetId = Guid.CreateVersion7();
        var scopeId = substituteEvent ? Guid.CreateVersion7() : targetId;
        var scope = scopeKind switch
        {
            ModerationScopeKind.Event => AuthorityScope.Event(tenant, scopeId),
            ModerationScopeKind.Resource => AuthorityScope.Resource(tenant, scopeId),
            _ => tenant
        };
        var id = Guid.CreateVersion7();
        var effect = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06).Effects[0];
        var bindings = new List<ModerationContributionBinding>
        {
            new()
            {
                SelectionId = id, Ordinal = 0, Kind = ModerationContributionBindingKind.Effect,
                EffectKind = effect.Kind, EffectMode = effect.Mode, EffectTarget = effect.Target,
                Capability = effect.Capability, Execution = effect.Execution,
                Strategy = null, Scope = scope, SubjectKind = subjectKind,
                SubjectId = subjectKind == ModerationSubjectKind.Content ? targetId : Guid.CreateVersion7(),
                TargetId = targetId, TargetRevision = 1,
                Operation = "resource.read", WindowEnd = null
            },
            new()
            {
                SelectionId = id, Ordinal = 1, Kind = ModerationContributionBindingKind.Consequence,
                EffectKind = null, EffectMode = null, EffectTarget = null, Capability = null, Execution = null,
                Strategy = null, Scope = scope, SubjectKind = ModerationSubjectKind.Human,
                SubjectId = Guid.CreateVersion7(), TargetId = Guid.CreateVersion7(), TargetRevision = 1,
                Operation = "resource.consequence", WindowEnd = null
            }
        };
        var selection = new ModerationContributionSelection
        {
            Id = id, EffectId = Guid.CreateVersion7(), DecisionId = Guid.CreateVersion7(), DecisionRevision = 2,
            CaseId = Guid.CreateVersion7(), CaseRevision = 1, PolicyId = Guid.CreateVersion7(), PolicyRevision = 1,
            PublicationId = Guid.CreateVersion7(), ProfileId = Guid.CreateVersion7(),
            HostBindingId = Guid.CreateVersion7(), TrustDomainId = scope.TrustDomainId, TenantId = tenant.Id,
            AuthoredByHumanId = Guid.CreateVersion7(), WriterGeneration = 2, Revision = 1,
            SelectionDigest = new string('a', 64), StartsAt = Now, EventWindowEnd = null,
            RecordedAt = Now, ValidUntil = Now.AddHours(1), Bindings = bindings
        };
        return (selection, bindings);
    }
}
