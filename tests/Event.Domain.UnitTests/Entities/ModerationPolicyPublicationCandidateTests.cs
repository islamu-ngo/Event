using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationPolicyPublicationCandidateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly string RequestDigest = new('a', 64);
    private static readonly string ProposedStateDigest = new('B', 64);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreparationRetainsOriginalDefinitionAndAuthorityWithoutAdoptingIt(bool tenantScope)
    {
        var snapshot = Snapshot(tenantScope: tenantScope);
        var futureId = Guid.CreateVersion7();
        var candidate = Prepare(snapshot, futureId);

        candidate.ValidatePersistedState();
        await Assert.That(candidate.Id).IsEqualTo(futureId);
        await Assert.That(candidate.DefinitionId).IsEqualTo(snapshot.Id);
        await Assert.That(candidate.Definition).IsSameReferenceAs(snapshot);
        await Assert.That(candidate.PolicyId).IsEqualTo(snapshot.PolicyId);
        await Assert.That(candidate.ExpectedRevision).IsEqualTo(0L);
        await Assert.That(candidate.PreviousPublicationId).IsNull();
        await Assert.That(candidate.Jurisdiction).IsEqualTo(snapshot.Jurisdiction);
        await Assert.That(candidate.TenantId).IsEqualTo(snapshot.TenantId);
        await Assert.That(candidate.HostBindingId).IsEqualTo(snapshot.HostBindingId);
        await Assert.That(candidate.PreparedByHumanId).IsEqualTo(snapshot.PrincipalId);
        await Assert.That(candidate.OperationId).IsEqualTo(snapshot.OperationId);
        await Assert.That(candidate.RequestDigest).IsEqualTo(RequestDigest);
        await Assert.That(candidate.ProposedStateDigest).IsEqualTo(ProposedStateDigest);
        await Assert.That(candidate.AuthoritySourceId).IsEqualTo(snapshot.AuthoritySourceId);
        await Assert.That(candidate.AuthorityRevision).IsEqualTo(3L);
        await Assert.That(candidate.WriterGeneration).IsEqualTo(4L);
        await Assert.That(candidate.Revision).IsEqualTo(1L);
        await Assert.That(candidate.PreparedAt).IsEqualTo(Now);
        await Assert.That(candidate.ValidUntil).IsEqualTo(Now.AddMinutes(5));
        await Assert.That(snapshot.AdoptedRevision).IsNull();
        await Assert.That(snapshot.PublishedActive).IsFalse();
        await Assert.That(snapshot.Evidence.Count).IsEqualTo(0);
        await Assert.That(snapshot.Id == futureId).IsFalse();
    }

    [Test]
    public async Task AnUnadoptedDefinitionCannotBecomeASelectedPolicy()
    {
        await Assert.That(() => ModerationPolicySelection.Start(Snapshot())).Throws<ArgumentException>();
    }

    [Test]
    public async Task CompetingPreparationsRetainSamePredecessorWithoutReservingAnAdoptedSlot()
    {
        var policyId = Guid.CreateVersion7();
        var predecessor = Guid.CreateVersion7();
        var first = Prepare(Snapshot(2, predecessor, policyId));
        var second = Prepare(Snapshot(2, predecessor, policyId));

        await Assert.That(first.PolicyId).IsEqualTo(second.PolicyId);
        await Assert.That(first.ExpectedRevision).IsEqualTo(1L);
        await Assert.That(second.ExpectedRevision).IsEqualTo(1L);
        await Assert.That(first.PreviousPublicationId).IsEqualTo(predecessor);
        await Assert.That(second.PreviousPublicationId).IsEqualTo(predecessor);
        await Assert.That(first.Id == second.Id).IsFalse();
        await Assert.That(first.DefinitionId == second.DefinitionId).IsFalse();
        await Assert.That(first.OperationId == second.OperationId).IsFalse();
    }

    [Test]
    public async Task ActiveAdoptedEvidenceBearingAndInvalidSourceSnapshotsCannotPrepare()
    {
        foreach (var (property, value) in new (string, object?)[]
        {
            (nameof(ModerationPolicyPublication.AdoptedRevision), 1L),
            (nameof(ModerationPolicyPublication.PublishedActive), true),
            (nameof(ModerationPolicyPublication.EvidenceValidUntil), Now.AddMinutes(1)),
            (nameof(ModerationPolicyPublication.Revision), 0L),
            (nameof(ModerationPolicyPublication.PreviousPublicationId), Guid.CreateVersion7()),
            (nameof(ModerationPolicyPublication.AuthorityRevision), 0L),
            (nameof(ModerationPolicyPublication.AuthorityRevision), long.MaxValue - 1),
            (nameof(ModerationPolicyPublication.QualificationRevision), 5L),
            (nameof(ModerationPolicyPublication.PublishedAt), Now.ToOffset(TimeSpan.FromHours(1))),
            (nameof(ModerationPolicyPublication.Id), Guid.Empty),
            (nameof(ModerationPolicyPublication.PolicyId), Guid.NewGuid()),
            (nameof(ModerationPolicyPublication.TenantId), Guid.Empty),
            (nameof(ModerationPolicyPublication.JurisdictionTenantId), Guid.NewGuid()),
            (nameof(ModerationPolicyPublication.AuthoritySourceId), Guid.Empty),
            (nameof(ModerationPolicyPublication.PrincipalId), Guid.NewGuid())
        })
        {
            var snapshot = Snapshot();
            Set(snapshot, property, value);
            await Assert.That(() => Prepare(snapshot)).Throws<ArgumentException>();
        }

        var withEvidence = Snapshot();
        Set(withEvidence, nameof(ModerationPolicyPublication.Evidence),
            new[] { new ModerationPolicyPublicationEvidence
            {
                PublicationId = withEvidence.Id, RequirementDigest = new string('c', 64),
                SourceId = Guid.CreateVersion7(), SourceRevision = 1,
                WriterGeneration = "native-moderation-publication-v1",
                ApprovalId = null, ApprovalRevision = null, ObservedAt = Now,
                ValidUntil = Now.AddMinutes(1)
            } });
        await Assert.That(() => Prepare(withEvidence)).Throws<ArgumentException>();
        await Assert.That(() => Prepare(Snapshot(2))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task PreparationCannotSubstituteOriginalHumanOperationRequestOrInstant(int change)
    {
        var snapshot = Snapshot();
        await Assert.That(() => ModerationPolicyPublicationCandidate.Prepare(
            snapshot, Guid.CreateVersion7(), change == 0 ? Guid.CreateVersion7() : snapshot.OperationId,
            change == 1 ? new string('d', 64) : RequestDigest, ProposedStateDigest,
            change == 2 ? Guid.CreateVersion7() : snapshot.PrincipalId,
            change == 3 ? Now.AddTicks(1) : Now, Now.AddMinutes(5))).Throws<ArgumentException>();
    }

    [Test]
    public async Task PreparationRejectsReusedIdentityMalformedDigestsAndNonfiniteUtcWindows()
    {
        var snapshot = Snapshot();
        foreach (var id in new[] { snapshot.Id, Guid.Empty, Guid.NewGuid(),
            Guid.Parse("019a0000-0000-7000-0000-000000000001") })
            await Assert.That(() => Prepare(snapshot, id)).Throws<ArgumentException>();
        foreach (var digest in new[] { "", new string('a', 63), new string('z', 64), "aB" + new string('0', 62) })
        {
            await Assert.That(() => ModerationPolicyPublicationCandidate.Prepare(snapshot,
                Guid.CreateVersion7(), snapshot.OperationId, RequestDigest, digest,
                snapshot.PrincipalId, Now, Now.AddMinutes(5))).Throws<ArgumentException>();
            Set(snapshot, nameof(ModerationPolicyPublication.RequestDigest), digest);
            await Assert.That(() => ModerationPolicyPublicationCandidate.Prepare(snapshot,
                Guid.CreateVersion7(), snapshot.OperationId, digest, ProposedStateDigest,
                snapshot.PrincipalId, Now, Now.AddMinutes(5))).Throws<ArgumentException>();
            Set(snapshot, nameof(ModerationPolicyPublication.RequestDigest), RequestDigest);
        }
        foreach (var until in new[] { Now, Now.AddTicks(-1), DateTimeOffset.MaxValue,
            Now.AddMinutes(5).ToOffset(TimeSpan.FromHours(1)) })
            await Assert.That(() => ModerationPolicyPublicationCandidate.Prepare(snapshot,
                Guid.CreateVersion7(), snapshot.OperationId, RequestDigest, ProposedStateDigest,
                snapshot.PrincipalId, Now, until)).Throws<ArgumentException>();
        foreach (var instant in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
            Now.ToOffset(TimeSpan.FromHours(1)) })
        {
            Set(snapshot, nameof(ModerationPolicyPublication.PublishedAt), instant);
            await Assert.That(() => ModerationPolicyPublicationCandidate.Prepare(snapshot,
                Guid.CreateVersion7(), snapshot.OperationId, RequestDigest, ProposedStateDigest,
                snapshot.PrincipalId, instant, Now.AddMinutes(5))).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task ScalarHydrationCannotRewriteCandidateProvenanceEvenWithoutLoadedDefinition()
    {
        foreach (var (property, value) in new (string, object?)[]
        {
            (nameof(ModerationPolicyPublicationCandidate.ExpectedRevision), -1L),
            (nameof(ModerationPolicyPublicationCandidate.ExpectedRevision), long.MaxValue),
            (nameof(ModerationPolicyPublicationCandidate.WriterGeneration), 5L),
            (nameof(ModerationPolicyPublicationCandidate.AuthorityRevision), long.MaxValue - 1),
            (nameof(ModerationPolicyPublicationCandidate.Revision), 2L),
            (nameof(ModerationPolicyPublicationCandidate.PreparedByHumanId), Guid.Empty),
            (nameof(ModerationPolicyPublicationCandidate.PreparedByHumanId), Guid.NewGuid()),
            (nameof(ModerationPolicyPublicationCandidate.ValidUntil), DateTimeOffset.MaxValue)
        })
        {
            var candidate = Prepare(Snapshot());
            Set(candidate, nameof(ModerationPolicyPublicationCandidate.Definition), null);
            Set(candidate, property, value);
            await Assert.That(candidate.ValidatePersistedState).Throws<ArgumentException>();
        }
        var valid = Prepare(Snapshot());
        Set(valid, nameof(ModerationPolicyPublicationCandidate.Definition), null);
        valid.ValidatePersistedState();
        await Assert.That(valid.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Inactive_adopted_publication_advances_latest_without_withdrawing_active_history()
    {
        var first = Snapshot(adopted: true, active: true);
        var selection = ModerationPolicySelection.Start(first);
        var inactive = Snapshot(2, prior: first, adopted: true);

        selection.Replace(inactive, 1, first.Id);
        await Assert.That(selection.LatestPublicationId).IsEqualTo(inactive.Id);
        await Assert.That(selection.ActivePublicationId).IsEqualTo((Guid?)first.Id);
        await Assert.That(selection.Revision).IsEqualTo(2L);
        await Assert.That(first.PublishedActive).IsTrue();
        await Assert.That(inactive.PublishedActive).IsFalse();

        var next = Snapshot(3, prior: inactive, adopted: true, active: true);
        selection.Replace(next, 2, inactive.Id);
        await Assert.That(selection.LatestPublicationId).IsEqualTo(next.Id);
        await Assert.That(selection.ActivePublicationId).IsEqualTo((Guid?)next.Id);
        await Assert.That(selection.Revision).IsEqualTo(3L);
        await Assert.That(first.Id).IsNotEqualTo(next.Id);
    }

    [Test]
    [Arguments("revision")]
    [Arguments("expected-publication")]
    [Arguments("predecessor")]
    [Arguments("reused-identity")]
    public async Task Conflicting_publication_tuple_leaves_both_selection_pointers_unchanged(string defect)
    {
        var first = Snapshot(adopted: true, active: true);
        var selection = ModerationPolicySelection.Start(first);
        var next = Snapshot(2, prior: first, adopted: true);
        if (defect == "predecessor")
            Set(next, nameof(next.PreviousPublicationId), Guid.CreateVersion7());
        if (defect == "reused-identity")
            Set(next, nameof(next.Id), first.Id);

        await Assert.That(() => selection.Replace(next, defect == "revision" ? 0 : 1,
            defect == "expected-publication" ? Guid.CreateVersion7() : first.Id))
            .Throws<InvalidOperationException>();
        await Assert.That(selection.Revision).IsEqualTo(1L);
        await Assert.That(selection.LatestPublicationId).IsEqualTo(first.Id);
        await Assert.That(selection.ActivePublicationId).IsEqualTo((Guid?)first.Id);
    }

    [Test]
    [Arguments(nameof(ModerationPolicyPublication.PolicyId))]
    [Arguments(nameof(ModerationPolicyPublication.HostBindingId))]
    [Arguments(nameof(ModerationPolicyPublication.TrustDomainId))]
    [Arguments(nameof(ModerationPolicyPublication.JurisdictionTenantId))]
    public async Task A_foreign_publication_cannot_replace_the_selected_slot(string coordinate)
    {
        var first = Snapshot(adopted: true);
        var selection = ModerationPolicySelection.Start(first);
        var next = Snapshot(2, prior: first, adopted: true);
        Set(next, coordinate, Guid.CreateVersion7());

        await Assert.That(() => selection.Replace(next, 1, first.Id)).Throws<InvalidOperationException>();
        await Assert.That(selection.Revision).IsEqualTo(1L);
        await Assert.That(selection.LatestPublicationId).IsEqualTo(first.Id);
        await Assert.That(selection.ActivePublicationId).IsNull();
    }

    [Test]
    public async Task Unadopted_preparation_cannot_replace_an_existing_selected_policy()
    {
        var first = Snapshot(adopted: true);
        var selection = ModerationPolicySelection.Start(first);
        var preparation = Snapshot(2, prior: first);

        await Assert.That(() => selection.Replace(preparation, 1, first.Id)).Throws<InvalidOperationException>();
        await Assert.That(selection.LatestPublicationId).IsEqualTo(first.Id);
        await Assert.That(selection.ActivePublicationId).IsNull();
        await Assert.That(preparation.AdoptedRevision).IsNull();
    }

    [Test]
    [Arguments("missing-evidence")]
    [Arguments("missing-deadline")]
    [Arguments("expired")]
    public async Task Active_selection_requires_retained_evidence_and_a_future_publication_deadline(string defect)
    {
        var first = Snapshot(adopted: true);
        var selection = ModerationPolicySelection.Start(first);
        var next = Snapshot(2, prior: first, adopted: true, active: true);
        switch (defect)
        {
            case "missing-evidence": Set(next, nameof(next.Evidence), Array.Empty<ModerationPolicyPublicationEvidence>()); break;
            case "missing-deadline": Set(next, nameof(next.EvidenceValidUntil), null); break;
            case "expired": Set(next, nameof(next.EvidenceValidUntil), Now); break;
        }

        await Assert.That(() => selection.Replace(next, 1, first.Id)).Throws<InvalidOperationException>();
        await Assert.That(selection.Revision).IsEqualTo(1L);
        await Assert.That(selection.LatestPublicationId).IsEqualTo(first.Id);
        await Assert.That(selection.ActivePublicationId).IsNull();
    }

    private static ModerationPolicyPublicationCandidate Prepare(ModerationPolicyPublication snapshot, Guid? id = null) =>
        ModerationPolicyPublicationCandidate.Prepare(snapshot, id ?? Guid.CreateVersion7(),
            snapshot.OperationId, RequestDigest, ProposedStateDigest, snapshot.PrincipalId, Now, Now.AddMinutes(5));

    private static ModerationPolicyPublication Snapshot(
        long revision = 1, Guid? previous = null, Guid? policyId = null, bool tenantScope = true,
        ModerationPolicyPublication? prior = null, bool adopted = false, bool active = false)
    {
        var id = Guid.CreateVersion7();
        var profileId = Guid.CreateVersion7();
        var tenantId = prior?.TenantId ?? Guid.CreateVersion7();
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG05);
        var response = reference.Responses[0];
        var strategies = new[] { 0 }.Concat(reference.Strategies.Order().Select(strategy => (int)strategy)).ToArray();
        var plans = new[] { ModerationReferenceCatalogue.GetBundle(reference.Bundle).Resolve(reference.ProtectionPurpose) }
            .Concat(reference.Strategies.Order().Select(ModerationReferenceCatalogue.GetStrategy))
            .Select(plan => plan with { Recognition = ModerationRecognitionMode.NoCaptureFromThisControl }).ToArray();
        return new ModerationPolicyPublication
        {
            Id = id, PolicyId = policyId ?? prior?.PolicyId ?? Guid.CreateVersion7(), Revision = revision,
            AdoptedRevision = adopted ? revision : null, PreviousPublicationId = previous ?? prior?.Id,
            HostBindingId = prior?.HostBindingId ?? Guid.CreateVersion7(),
            TrustDomainId = prior?.TrustDomainId ?? Guid.CreateVersion7(),
            SolutionId = prior?.SolutionId ?? Guid.CreateVersion7(),
            InstanceId = prior?.InstanceId ?? Guid.CreateVersion7(),
            JurisdictionTenantId = prior is null ? tenantScope ? tenantId : null : prior.JurisdictionTenantId,
            TenantId = tenantId,
            PrincipalId = Guid.CreateVersion7(), OperationId = Guid.CreateVersion7(), RequestDigest = RequestDigest,
            DeclaredStateDigest = new string('c', 64), AuthoritySourceId = Guid.CreateVersion7(),
            AuthorityRevision = 3, QualificationRevision = 4, WriterGeneration = "native-moderation-publication-v1",
            CatalogueRevision = ModerationReferenceCatalogue.Revision, InventoryRevision = 1,
            InventoryFingerprint = new string('d', 64), PublishedActive = active,
            PublishedAt = Now, EvidenceValidUntil = active ? Now.AddHours(1) : null,
            MaximumFiniteDurationTicks = TimeSpan.FromDays(36600).Ticks,
            MaximumReviewDelayTicks = TimeSpan.FromDays(36600).Ticks, MinimumApprovals = 0, MaximumApprovals = 2,
            MinimumContestationCalendarMonths = 6,
            Classes = [new ModerationPolicyPublicationClass
            {
                PublicationId = id, Code = "class.CFG05", Revision = 1, Meaning = "Configured moderation definition.",
                Ground = reference.Ground, Basis = reference.Basis
            }],
            Reasons = [new ModerationPolicyPublicationReason
            {
                PublicationId = id, Code = "reason.CFG05", Revision = 1, Meaning = "Configured moderation reason.",
                ClassCode = "class.CFG05", ClassRevision = 1
            }],
            Modes = [new ModerationPolicyPublicationMode { PublicationId = id, Mode = 1 }],
            Evidence = active ? [new ModerationPolicyPublicationEvidence
            {
                PublicationId = id, RequirementDigest = new string('e', 64), ApprovalId = null,
                ApprovalRevision = null, SourceId = Guid.CreateVersion7(), SourceRevision = 1,
                WriterGeneration = "native-moderation-publication-v1", ObservedAt = Now,
                ValidUntil = Now.AddHours(1)
            }] : [],
            Profiles = [new ModerationPolicyPublicationProfile
            {
                PublicationId = id, ProfileId = profileId, RuleCode = "profile.CFG05", RuleRevision = 1,
                ReasonCode = "reason.CFG05", ReasonRevision = 1, ReferenceProfile = reference.Id,
                ReferenceRevision = reference.Revision, Grade = response.Grade, Bundle = reference.Bundle,
                ProtectionPurpose = reference.ProtectionPurpose, TimingKind = response.Timing.Kind,
                DurationTicks = response.Timing.Duration.Ticks, RequiredApprovals = response.RequiredApprovals,
                ContestationCalendarMonths = 6, Recognition = ModerationReferenceRecognition.Never, Supported = false,
                Scopes = [new ModerationPolicyPublicationScope
                {
                    PublicationId = id, ProfileId = profileId, Kind = ModerationScopeKind.Tenant
                }],
                Subjects = [new ModerationPolicyPublicationSubject
                {
                    PublicationId = id, ProfileId = profileId, Kind = reference.SubjectKinds.Order().First()
                }],
                Effects = reference.Effects.Select((effect, ordinal) => new ModerationPolicyPublicationEffect
                {
                    PublicationId = id, ProfileId = profileId, Ordinal = ordinal, Kind = effect.Kind,
                    Mode = effect.Mode, Target = effect.Target, Capability = effect.Capability,
                    Execution = effect.Execution, SemanticOwner = 0
                }).ToArray(),
                Plans = plans.Select((plan, ordinal) => new ModerationPolicyPublicationPlan
                {
                    PublicationId = id, ProfileId = profileId, Strategy = strategies[ordinal],
                    Content = plan.Content, Management = plan.Management, Resources = plan.Resources,
                    NewAdmission = plan.NewAdmission, ExistingAdmission = plan.ExistingAdmission,
                    PrivateDisclosure = plan.PrivateDisclosure, Settlement = plan.Settlement, Capacity = plan.Capacity,
                    Credentials = plan.Credentials, Notifications = plan.Notifications, Recognition = plan.Recognition,
                    PendingWork = plan.PendingWork, Restoration = plan.Restoration
                }).ToArray(),
                Consumers = [], Prerequisites = []
            }]
        };
    }

    private static void Set(object row, string property, object? value) =>
        (row.GetType().GetProperty(property) ?? throw new InvalidOperationException("Missing hydration property."))
        .SetValue(row, value);
}
