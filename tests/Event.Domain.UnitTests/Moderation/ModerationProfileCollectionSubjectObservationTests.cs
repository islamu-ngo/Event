using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationProfileCollectionSubjectObservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalHumanBinding_PreservesOwnSequenceAndExactNativeCoordinates(bool tenant)
    {
        var data = new Data(tenant);
        var original = data.Observe();
        var binding = original.ToBinding();
        await Assert.That(binding.Target.HumanSubject!.Id).IsEqualTo(data.Human);
        await Assert.That(binding.Target.Origin).IsEqualTo(data.Instance);
        await Assert.That(binding.Jurisdiction).IsEqualTo(data.Jurisdiction);
        await Assert.That(binding.Purpose).IsEqualTo(ModerationIndividualPurpose.ProfileCompletion);
        await Assert.That(binding.PolicyId).IsEqualTo(data.Age.PolicyId);
        await Assert.That(binding.PolicyRevision).IsEqualTo(1L);
        await Assert.That(binding.SourceId).IsEqualTo(original.Id);
        await Assert.That(binding.SourceRevision).IsEqualTo(1L);
        await Assert.That(binding.SourceGeneration).IsEqualTo(1L);
        await Assert.That(binding.ObservationSequence).IsEqualTo(1L);
        await Assert.That(binding.ObservationStamp).IsEqualTo(original.ObservationStamp);
        await Assert.That(binding.ObservedAt).IsEqualTo(Now);
        await Assert.That(binding.ValidUntil).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(original.ProcedureSourceWriterGeneration).IsEqualTo(6L);
        await Assert.That(original.ProcedureAdoptionWriterGeneration).IsEqualTo(7L);
        await Assert.That(original.EpochRevision).IsEqualTo(7L);
        await Assert.That(original.SubjectHumanId.Version).IsEqualTo(4);
    }

    [Test]
    [Arguments(-1, false)]
    [Arguments(0, true)]
    [Arguments(119, true)]
    [Arguments(120, false)]
    public async Task OriginalWindow_IsHalfOpenAndConversionNeverRenewsIt(int seconds, bool expected)
    {
        var data = new Data();
        var original = data.Observe();
        var binding = original.ToBinding();
        await Assert.That(original.Covers(Now.AddSeconds(seconds))).IsEqualTo(expected);
        await Assert.That(original.ToBinding()).IsEqualTo(binding);
        await Assert.That(original.ObservedAt).IsEqualTo(Now);
        await Assert.That(original.ValidUntil).IsEqualTo(Now.AddMinutes(2));
    }

    [Test]
    [Arguments("v4-id")]
    [Arguments("subject-id")]
    [Arguments("procedure-id")]
    [Arguments("empty-human")]
    [Arguments("empty-stamp")]
    [Arguments("same-stamp")]
    [Arguments("zero-sequence")]
    [Arguments("max-sequence")]
    [Arguments("zero-epoch")]
    [Arguments("max-epoch")]
    [Arguments("empty-login")]
    [Arguments("foreign-trust")]
    [Arguments("foreign-tenant")]
    [Arguments("non-native-jurisdiction")]
    [Arguments("empty-window")]
    [Arguments("infinite-window")]
    [Arguments("extended-window")]
    [Arguments("before-adoption")]
    [Arguments("non-utc")]
    [Arguments("withdrawn-procedure")]
    [Arguments("withdrawn-parent")]
    [Arguments("digest")]
    public async Task Recording_RejectsAliasedIdentityUnboundedTimeAndUnacceptedParents(string change)
    {
        var data = new Data();
        if (change == "withdrawn-procedure") data.Accepted.Withdraw(1, data.Age.ReviewerHumanId, Now);
        if (change == "withdrawn-parent") data.Parent.Withdraw(1, data.Age.ReviewerHumanId, Now);
        await Assert.That(() => data.Observe(change)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("Revision", 2L)]
    [Arguments("Generation", 0L)]
    [Arguments("ObservationSequence", 2L)]
    [Arguments("ProcedureSourceRevision", 2L)]
    [Arguments("ProcedureAdoptionRevision", 2L)]
    [Arguments("EpochRevision", 6L)]
    public async Task MaterializedOriginals_RejectInvalidIndependentGenerations(string property, long value)
    {
        var original = new Data().Observe();
        typeof(ModerationProfileCollectionSubjectObservation).GetProperty(property)!.SetValue(original, value);
        await Assert.That(() => original.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    public async Task MaterializedOriginals_RejectProcedureAdoptionAliasingParentCoordinates()
    {
        var data = new Data();
        foreach (var id in new[] { data.Procedure.Id, data.Age.Id, data.Parent.Id,
            data.Age.HostBindingId, data.Age.TrustDomainId, data.Age.TenantId,
            data.Age.PolicyId, data.Age.PublicationId, data.Age.DefinitionId, data.Age.ProfileId })
        {
            var original = data.Observe();
            typeof(ModerationProfileCollectionSubjectObservation)
                .GetProperty(nameof(original.ProcedureAdoptionId))!.SetValue(original, id);
            await Assert.That(() => original.ValidatePersistedState()).Throws<ArgumentException>();
            await Assert.That(() => original.ToBinding()).Throws<ArgumentException>();
            await Assert.That(() => original.Covers(Now)).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task Coverage_RejectsCorruptOriginalRevisionInsteadOfReportingLive()
    {
        var original = new Data().Observe();
        typeof(ModerationProfileCollectionSubjectObservation)
            .GetProperty(nameof(original.Revision))!.SetValue(original, 2L);
        await Assert.That(() => original.Covers(Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task MaterializedOriginals_CannotRebindHumanPolicyOrProcedure()
    {
        foreach (var property in new[] { "ProcedureSourceId", "HostBindingId", "TrustDomainId",
            "TenantId", "PolicyId", "PublicationId", "ProfileId", "DefinitionId" })
        {
            var original = new Data().Observe();
            typeof(ModerationProfileCollectionSubjectObservation).GetProperty(property)!
                .SetValue(original, Guid.CreateVersion7());
            await Assert.That(() => original.ValidatePersistedState()).Throws<ArgumentException>();
        }
    }

    private sealed class Data
    {
        public Data(bool tenant = true)
        {
            Human = Guid.NewGuid();
            var reviewer = Guid.NewGuid();
            var owner = Guid.NewGuid();
            var ownership = new ModerationChildAgePolicyOwnership(reviewer, reviewer, owner, owner,
                Guid.NewGuid(), "Independent controllers.", "Privacy mandate.", "Child mandate.", "Collection mandate.");
            Age = ModerationChildAgePolicySource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64), Terms(tenant), ownership,
                ownership.GovernanceControllerHumanId, 4, Now, Now.AddMinutes(5));
            Parent = ModerationChildAgePolicyAdoption.Adopt(Guid.CreateVersion7(), Age, reviewer, 5,
                Now, Now.AddMinutes(4));
            var vocabulary = new ModerationProfileCollectionProcedure(ModerationIndividualPurpose.ProfileCompletion,
                ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
                Enum.GetValues<ModerationProfilePreliminaryFieldKind>(), [ModerationProfileNormalFieldKind.DateOfBirth],
                "Separate relationship references.", "Qualified manual issuer references.");
            Procedure = ModerationProfileCollectionProcedureSource.Record(Guid.CreateVersion7(), Age, Parent,
                vocabulary, owner, 6, Now, Now.AddMinutes(3));
            Accepted = ModerationProfileCollectionProcedureAdoption.Adopt(Guid.CreateVersion7(),
                Procedure, Age, Parent, reviewer, 7, Now, Now.AddMinutes(2));
            Instance = AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Age.TrustDomainId),
                Guid.CreateVersion7()), Guid.CreateVersion7());
            Jurisdiction = tenant ? AuthorityScope.Tenant(Instance, Age.TenantId) : Instance;
        }

        public Guid Human { get; }
        public AuthorityScope Instance { get; }
        public AuthorityScope Jurisdiction { get; }
        public ModerationChildAgePolicySource Age { get; }
        public ModerationChildAgePolicyAdoption Parent { get; }
        public ModerationProfileCollectionProcedureSource Procedure { get; }
        public ModerationProfileCollectionProcedureAdoption Accepted { get; }

        public ModerationProfileCollectionSubjectObservation Observe(string change = "")
        {
            var id = change == "v4-id" ? Guid.NewGuid() : change == "subject-id" ? Human
                : change == "procedure-id" ? Procedure.Id : Guid.CreateVersion7();
            var jurisdiction = change == "foreign-tenant" ? AuthorityScope.Tenant(Instance, Guid.CreateVersion7())
                : change == "non-native-jurisdiction" ? Instance.Parent! : Jurisdiction;
            if (change == "foreign-trust")
                jurisdiction = AuthorityScope.Instance(AuthorityScope.Solution(
                    AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
            return ModerationProfileCollectionSubjectObservation.Record(id,
                change == "empty-human" ? Guid.Empty : Human, change == "empty-login" ? Guid.Empty : null,
                Instance, jurisdiction, Guid.CreateVersion7(),
                change == "zero-epoch" ? 0 : change == "max-epoch" ? long.MaxValue : 7,
                Procedure, Accepted, Age, Parent,
                change == "zero-sequence" ? 0 : change == "max-sequence" ? long.MaxValue : 1,
                change == "empty-stamp" ? Guid.Empty : change == "same-stamp" ? id : Guid.CreateVersion7(),
                change == "non-utc" ? Now.ToOffset(TimeSpan.FromHours(1))
                    : change == "before-adoption" ? Now.AddTicks(-1) : Now,
                change == "empty-window" ? Now : change == "infinite-window" ? DateTimeOffset.MaxValue
                    : change == "extended-window" ? Accepted.ValidUntil.AddTicks(1) : Accepted.ValidUntil,
                change == "digest" ? "invalid" : new string('a', 64), new string('b', 64), new string('c', 64));
        }
    }

    private static ModerationChildAgePolicyTerms Terms(bool tenant) => new(
        tenant ? ModerationScopeKind.Tenant : ModerationScopeKind.Instance,
        ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling,
        ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites,
        ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob,
        ModerationChildAgeProviderAssurance.NotAccepted,
        ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty,
        ModerationChildAgeClassification.ReviewedContentNotEventFormat, 18,
        ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation,
        ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser,
        ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration,
        ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent,
        ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace,
        ModerationChildAgeCalendar.Gregorian, ModerationChildAgeTimeZone.Utc,
        ModerationChildAgeLeapDay.MarchFirstInNonLeapYear,
        ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes, 18,
        "Child risk review.", "Privacy purpose review.", "Minimum profile collection.",
        "Independent prerequisite assessment.", "Qualified manual review.", null,
        "Reviewed classification.", "Gregorian UTC review.");
}
