using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    /// <summary>
    /// Mapping witnesses only. The existing fixture declares historical ACTIVE read state,
    /// not measured publication activation. Real owner/reviewer writers create a new five-field
    /// procedure; assessment and binding are valid Domain starting states, not authenticated
    /// applicability, live evidence, consent, conformance or collection receipts.
    /// </summary>
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ProfileAssessment_RoundtripPreservesExplicitEmptyOrCompleteOriginalSet(
        bool empty, bool instanceJurisdiction)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture, empty, instanceJurisdiction);
        fixture.Context.ChangeTracker.Clear();
        var accepted = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>()
            .AsNoTracking().SingleAsync();
        accepted.ValidatePersistedState();
        var source = accepted.Source;
        await Assert.That(source.Matches(original.Source)).IsTrue();
        await Assert.That(source.Required.SequenceEqual(original.Source.Required)).IsTrue();
        await Assert.That(source.Required.Count).IsEqualTo(empty ? 0 : 3);
        await Assert.That(source.Binding).IsEqualTo(original.Source.Binding);
        await Assert.That(source.ProcedureSource.Matches(original.Source.ProcedureSource)).IsTrue();
        await Assert.That(source.Generation).IsEqualTo(2);
        await Assert.That(accepted.Generation).IsEqualTo(3);
        await Assert.That(accepted.SourceId).IsEqualTo(source.Id);
        await Assert.That(accepted.SourceRevision).IsEqualTo(1);
        await Assert.That(accepted.SourceGeneration).IsEqualTo(2);
        await Assert.That(accepted.AdoptedByHumanId).IsEqualTo(fixture.ReviewerId);
        await Assert.That(accepted.AdoptedAt).IsEqualTo(Now.AddTicks(13));
        await Assert.That(accepted.ValidUntil).IsEqualTo(Now.AddMinutes(2).AddTicks(17));
        var age = await fixture.Context.Set<ModerationChildAgePolicySource>().SingleAsync();
        var parent = await fixture.Context.Set<ModerationChildAgePolicyAdoption>().SingleAsync();
        var procedureAdoption = await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>()
            .SingleAsync(row => row.Id == source.ProcedureAdoptionId);
        var metadata = accepted.ToAssessment(source, source.ProcedureSource, procedureAdoption,
            age, parent, source.Binding, accepted.AdoptedAt);
        await Assert.That(metadata.Required.SequenceEqual(source.Required)).IsTrue();
        await Assert.That(metadata.Reference.Id).IsEqualTo(accepted.Id);
        await Assert.That(metadata.Reference.ObservedAt).IsEqualTo(accepted.AdoptedAt);
        await Assert.That(metadata.Reference.ValidUntil).IsEqualTo(accepted.ValidUntil);
    }

    [Test]
    [Arguments(false, "ProcedureSourceId")]
    [Arguments(false, "SubjectHumanId")]
    [Arguments(false, "RecordedByHumanId")]
    [Arguments(false, "OwnerControllerHumanId")]
    [Arguments(false, "ReviewerControllerHumanId")]
    [Arguments(false, "Generation")]
    [Arguments(false, "Revision")]
    [Arguments(false, "RecordedAt")]
    [Arguments(false, "ValidUntil")]
    [Arguments(false, "ProcedureAdoptionId")]
    [Arguments(false, "ProcedureAdoptionRevision")]
    [Arguments(false, "ProcedureAdoptionWriterGeneration")]
    [Arguments(false, "ProcedureAdoptedByHumanId")]
    [Arguments(false, "ProcedureAdoptedAt")]
    [Arguments(false, "ProcedureAdoptionValidUntil")]
    [Arguments(false, "Binding")]
    [Arguments(false, "Required")]
    [Arguments(true, "SourceId")]
    [Arguments(true, "SourceRevision")]
    [Arguments(true, "SourceGeneration")]
    [Arguments(true, "AdoptedByHumanId")]
    [Arguments(true, "Generation")]
    [Arguments(true, "AdoptedAt")]
    [Arguments(true, "ValidUntil")]
    public async Task ProfileAssessment_SavedOriginalsCannotBeRebound(bool adoption, string property)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        var entry = adoption
            ? fixture.Context.Entry((object)original.Adoption)
            : fixture.Context.Entry((object)original.Source);
        var coordinate = entry.Property(property);
        var expected = coordinate.CurrentValue;
        var procedureId = original.Source.ProcedureSource.Id;
        coordinate.CurrentValue = coordinate.CurrentValue switch
        {
            Guid => fixture.OtherSubjectId,
            long value => value + 1,
            DateTimeOffset value => value.AddTicks(1),
            ModerationIndividualBinding => AssessmentBinding(fixture, original.Source.ProcedureSource,
                fixture.OtherSubjectId, false),
            IReadOnlyList<ModerationCollectionPrerequisiteKind> => Array.AsReadOnly(
                Array.Empty<ModerationCollectionPrerequisiteKind>()),
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
        await Assert.That(async () => await fixture.Context.SaveChangesAsync()).Throws<InvalidOperationException>();
        fixture.Context.ChangeTracker.Clear();
        var retained = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>()
            .AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Source.Binding.Target.HumanSubject!.Id).IsEqualTo(fixture.SubjectId);
        await Assert.That(retained.Source.Generation).IsEqualTo(2);
        await Assert.That(retained.Generation).IsEqualTo(3);
        await Assert.That(retained.Source.Required.Count).IsEqualTo(3);
        await Assert.That(retained.Source.ProcedureSource.Id).IsEqualTo(procedureId);
        var retainedEntry = adoption
            ? fixture.Context.Entry((object)retained)
            : fixture.Context.Entry((object)retained.Source);
        if (expected is IReadOnlyList<ModerationCollectionPrerequisiteKind> required)
            await Assert.That(retained.Source.Required.SequenceEqual(required)).IsTrue();
        else
            await Assert.That(retainedEntry.Property(property).CurrentValue).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileAssessment_IndependentReviewAndTerminalWithdrawalUseRevisionCas(bool ownerWithdraws)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        var source = original.Source;
        var age = await fixture.Context.Set<ModerationChildAgePolicySource>().SingleAsync();
        var parent = await fixture.Context.Set<ModerationChildAgePolicyAdoption>().SingleAsync();
        var procedure = await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>()
            .SingleAsync(row => row.Id == source.ProcedureAdoptionId);
        await Assert.That(() => ModerationProfileCollectionAssessmentAdoption.Adopt(Guid.CreateVersion7(),
            source, source.ProcedureSource, procedure, age, parent, source.Binding, fixture.OwnerId,
            3, Now.AddTicks(13), Now.AddMinutes(2))).Throws<ArgumentException>();
        fixture.Context.ChangeTracker.Clear();
        var first = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().SingleAsync();
        await using var competitor = fixture.Database.Context();
        competitor.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var stale = await competitor.Set<ModerationProfileCollectionAssessmentAdoption>().SingleAsync();
        await Assert.That(() => first.Withdraw(1, fixture.SubjectId, Now.AddMinutes(9))).Throws<ArgumentException>();
        await Assert.That(() => first.Withdraw(2, fixture.OwnerId, Now.AddMinutes(9))).Throws<InvalidOperationException>();
        var actor = ownerWithdraws ? fixture.OwnerId : fixture.ReviewerId;
        first.Withdraw(1, actor, Now.AddMinutes(9));
        await fixture.Context.SaveChangesAsync();
        stale.Withdraw(1, actor, Now.AddMinutes(10));
        await Assert.That(async () => await competitor.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        fixture.Context.ChangeTracker.Clear();
        var terminal = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>()
            .AsNoTracking().SingleAsync();
        terminal.ValidatePersistedState();
        await Assert.That(terminal.Revision).IsEqualTo(2);
        await Assert.That(terminal.WithdrawnByHumanId).IsEqualTo(actor);
        await Assert.That(terminal.WithdrawnAt).IsEqualTo(Now.AddMinutes(9));
        await Assert.That(terminal.Source.Matches(source)).IsTrue();
        await Assert.That(terminal.ValidUntil).IsEqualTo(original.Adoption.ValidUntil);
        await Assert.That(() => terminal.Withdraw(2, actor, Now.AddMinutes(11))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ProfileAssessment_PiiHardDeleteCascadesBothRowsAndRetainsUnrelatedHistory()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        await fixture.Context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentSource>().AnyAsync()).IsFalse();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().AnyAsync()).IsFalse();
        await Assert.That(await fixture.Context.Users.AnyAsync(row => row.Id == fixture.SubjectId)).IsTrue();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionProcedureSource>()
            .AnyAsync(row => row.Id == original.Source.ProcedureSource.Id)).IsTrue();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>()
            .AnyAsync(row => row.Id == original.Source.ProcedureAdoptionId)).IsTrue();
        await Assert.That(await fixture.Context.Set<ModerationChildAgePolicySource>().AnyAsync()).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileAssessment_SourceSlotAndExactSourceAdoptionAreUnique(bool duplicateAdoption)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        var source = original.Source;
        var age = await fixture.Context.Set<ModerationChildAgePolicySource>().SingleAsync();
        var parent = await fixture.Context.Set<ModerationChildAgePolicyAdoption>().SingleAsync();
        var procedure = await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>()
            .SingleAsync(row => row.Id == source.ProcedureAdoptionId);
        if (duplicateAdoption)
            fixture.Context.Add(ModerationProfileCollectionAssessmentAdoption.Adopt(Guid.CreateVersion7(),
                source, source.ProcedureSource, procedure, age, parent, source.Binding,
                fixture.ReviewerId, 4, Now.AddTicks(13), Now.AddMinutes(2)));
        else
            fixture.Context.Add(ModerationProfileCollectionAssessmentSource.Record(Guid.CreateVersion7(),
                source.ProcedureSource, procedure, age, parent, source.Binding, source.Required,
                fixture.OwnerId, 2, source.RecordedAt, source.ValidUntil));
        await Assert.That(async () => await fixture.Context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    [Arguments(false, "generation = 0")]
    [Arguments(false, "generation = 9223372036854775807")]
    [Arguments(false, "revision = 2")]
    [Arguments(false, "required = 8")]
    [Arguments(false, "required = -1")]
    [Arguments(false, "recorded_at = 0")]
    [Arguments(false, "valid_until = recorded_at")]
    [Arguments(false, "valid_until = 3155378975999999999")]
    [Arguments(false, "procedure_adoption_revision = 2")]
    [Arguments(false, "procedure_adoption_valid_until = procedure_adopted_at")]
    [Arguments(false, "recorded_by_human_id = procedure_adopted_by_human_id")]
    [Arguments(false, "owner_controller_human_id = reviewer_controller_human_id")]
    [Arguments(true, "source_revision = 2")]
    [Arguments(true, "generation = source_generation")]
    [Arguments(true, "generation = 9223372036854775807")]
    [Arguments(true, "adopted_at = 0")]
    [Arguments(true, "valid_until = adopted_at")]
    [Arguments(true, "revision = 2")]
    [Arguments(true, "withdrawn_at = adopted_at")]
    public async Task ProfileAssessment_RelationalChecksRejectMalformedLifecycle(bool adoption, string assignment)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await PersistProfileAssessmentAsync(fixture);
        var table = adoption ? "moderation_profile_collection_assessment_adoptions"
            : "moderation_profile_collection_assessment_sources";
        // Fixed damaged-storage cases, not native writers.
        await Assert.That(async () => await fixture.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET {assignment}")).Throws<Microsoft.Data.Sqlite.SqliteException>();
    }

    [Test]
    [Arguments("SubjectHumanId")]
    [Arguments("ProcedureSourceId")]
    [Arguments("ProcedureAdoptionId")]
    [Arguments("RecordedByHumanId")]
    [Arguments("OwnerControllerHumanId")]
    [Arguments("ReviewerControllerHumanId")]
    [Arguments("ProcedureAdoptedByHumanId")]
    [Arguments("AdoptedByHumanId")]
    public async Task ProfileAssessment_OriginalRelationsRequireActualPiiProcedureAndActors(string property)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        // Inserts exercise FKs rather than EF's immutable-after-save guard.
        fixture.Context.Remove(original.Adoption);
        await fixture.Context.SaveChangesAsync();
        if (property == "AdoptedByHumanId")
        {
            fixture.Context.Add(original.Adoption);
            fixture.Context.Entry(original.Adoption).Property(property).CurrentValue = Guid.CreateVersion7();
        }
        else
        {
            fixture.Context.Remove(original.Source);
            await fixture.Context.SaveChangesAsync();
            fixture.Context.Add(original.Source);
            fixture.Context.Entry(original.Source).Property(property).CurrentValue = Guid.CreateVersion7();
        }
        await Assert.That(async () => await fixture.Context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileAssessment_RetainedPrivateSourceRestrictsOriginalProcedureHistoryDeletion(bool adoption)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileAssessmentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        if (adoption)
            await Assert.That(async () => await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>()
                .Where(row => row.Id == original.Source.ProcedureAdoptionId).ExecuteDeleteAsync())
                .Throws<Microsoft.Data.Sqlite.SqliteException>();
        else
            await Assert.That(async () => await fixture.Context.Set<ModerationProfileCollectionProcedureSource>()
                .Where(row => row.Id == original.Source.ProcedureSourceId).ExecuteDeleteAsync())
                .Throws<Microsoft.Data.Sqlite.SqliteException>();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentSource>().CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("MissingOrigin")]
    [Arguments("UnknownMember")]
    [Arguments("DuplicateMember")]
    [Arguments("UnknownKind")]
    [Arguments("WrongPurpose")]
    [Arguments("ForeignSubject")]
    [Arguments("ForeignPolicy")]
    [Arguments("ForeignTenant")]
    public async Task ProfileAssessment_DamagedBindingFailsFactoryOrOriginalValidation(string damage)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await PersistProfileAssessmentAsync(fixture);
        var encoded = await fixture.Context.Database.SqlQueryRaw<string>(
            "SELECT binding AS Value FROM moderation_profile_collection_assessment_sources").SingleAsync();
        var snapshot = JsonNode.Parse(encoded)!.AsObject();
        switch (damage)
        {
            case "MissingOrigin": snapshot.Remove("Origin"); break;
            case "UnknownMember": snapshot["IndividualEvidence"] = "unsupported"; break;
            case "UnknownKind": snapshot["TargetKind"] = 99; break;
            case "WrongPurpose": snapshot["Purpose"] = (int)ModerationIndividualPurpose.ParticipantCollection; break;
            case "ForeignSubject": snapshot["SubjectId"] = fixture.OtherSubjectId.ToString(); break;
            case "ForeignPolicy": snapshot["PolicyId"] = Guid.CreateVersion7().ToString(); break;
            case "ForeignTenant": snapshot["Jurisdiction"]!["TenantId"] = Guid.CreateVersion7().ToString(); break;
        }
        var damaged = damage == "DuplicateMember" ? encoded.Insert(1, "\"TargetKind\":99,") : snapshot.ToJsonString();
        await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE moderation_profile_collection_assessment_sources SET binding = {damaged}");
        fixture.Context.ChangeTracker.Clear();
        async Task ReadAndValidate()
        {
            var accepted = await fixture.Context.Set<ModerationProfileCollectionAssessmentAdoption>()
                .AsNoTracking().SingleAsync();
            accepted.ValidatePersistedState();
        }
        if (damage is "MissingOrigin" or "UnknownMember" or "DuplicateMember")
            await Assert.That(ReadAndValidate).Throws<JsonException>();
        else
            await Assert.That(ReadAndValidate).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("MissingTrust")]
    [Arguments("ForeignTrust")]
    [Arguments("MissingTenant")]
    [Arguments("ForeignTenant")]
    public async Task ProfileAssessment_PrivateRowsRequireExactResolvedOriginalScope(string scope)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await PersistProfileAssessmentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        await using var reader = fixture.Database.Context();
        reader.ModerationTrustDomainId = scope switch
        {
            "MissingTrust" => null,
            "ForeignTrust" => Guid.CreateVersion7(),
            _ => fixture.Database.Scope.TrustDomainId
        };
        if (scope == "MissingTenant") reader.TenantContext = null;
        if (scope == "ForeignTenant") reader.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
        // Generic tenant bypass must not override this private owner's exact named filters.
        reader.EnableTenantFilterBypass("Bounded mapping isolation witness.");
        await Assert.That(await reader.Set<ModerationProfileCollectionAssessmentSource>().AnyAsync()).IsFalse();
        await Assert.That(await reader.Set<ModerationProfileCollectionAssessmentAdoption>().AnyAsync()).IsFalse();
        reader.ClearTenantFilterBypass();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        reader.TenantContext = fixture.Database;
        await Assert.That(await reader.Set<ModerationProfileCollectionAssessmentSource>().CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.Set<ModerationProfileCollectionAssessmentAdoption>().CountAsync()).IsEqualTo(1);
    }

    private static ModerationIndividualBinding AssessmentBinding(IndividualPolicyFixture fixture,
        ModerationProfileCollectionProcedureSource procedure, Guid subject, bool instanceJurisdiction) => new(
        ModerationIndividualTarget.Human(SubjectReference.Create(ModerationSubjectKind.Human,
            subject, fixture.Database.Scope.Parent!)),
        new ModerationAgePolicy(procedure.PolicyId, procedure.PolicyRevision,
            instanceJurisdiction ? fixture.Database.Scope.Parent! : fixture.Database.Scope),
        ModerationIndividualPurpose.ProfileCompletion, procedure.Id, 1, 7,
        Guid.CreateVersion7(), 17, Now.AddTicks(3), Now.AddMinutes(3).AddTicks(7));

    private static async Task<(ModerationProfileCollectionAssessmentSource Source,
        ModerationProfileCollectionAssessmentAdoption Adoption)> PersistProfileAssessmentAsync(
        IndividualPolicyFixture fixture, bool empty = false, bool instanceJurisdiction = false)
    {
        var vocabulary = new ModerationProfileCollectionProcedure(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
            [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
                ModerationProfilePreliminaryFieldKind.AccountEligibilityReference,
                ModerationProfilePreliminaryFieldKind.SubjectConsentReference,
                ModerationProfilePreliminaryFieldKind.GuardianRelationshipReference,
                ModerationProfilePreliminaryFieldKind.GuardianConsentReference],
            [ModerationProfileNormalFieldKind.DateOfBirth],
            "Independent native guardian relationship review.", "Independent native manual issuer qualification.");
        var record = fixture.Record with
        {
            OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 6, Procedure = vocabulary
        };
        await Assert.That((await fixture.Writer(fixture.OwnerId).ExecuteAsync(record)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var adopt = fixture.Adopt with
        {
            OperationId = Guid.CreateVersion7(), SourceId = record.SourceId,
            ExpectedQualificationRevision = 7, ExpectedSourceWriterGeneration = 7,
            AdoptionId = Guid.CreateVersion7()
        };
        await Assert.That((await fixture.Writer(fixture.ReviewerId).ExecuteAsync(adopt)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        fixture.Context.ChangeTracker.Clear();
        var procedure = await fixture.Context.Set<ModerationProfileCollectionProcedureSource>()
            .SingleAsync(row => row.Id == record.SourceId);
        var procedureAdoption = await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>()
            .SingleAsync(row => row.Id == adopt.AdoptionId);
        var age = await fixture.Context.Set<ModerationChildAgePolicySource>().SingleAsync();
        var parent = await fixture.Context.Set<ModerationChildAgePolicyAdoption>().SingleAsync();
        var binding = AssessmentBinding(fixture, procedure, fixture.SubjectId, instanceJurisdiction);
        var required = empty ? Array.Empty<ModerationCollectionPrerequisiteKind>()
            : new[] { ModerationCollectionPrerequisiteKind.AccountEligibility,
                ModerationCollectionPrerequisiteKind.SubjectConsent, ModerationCollectionPrerequisiteKind.GuardianConsent };
        var source = ModerationProfileCollectionAssessmentSource.Record(Guid.CreateVersion7(), procedure,
            procedureAdoption, age, parent, binding, required, fixture.OwnerId, 2,
            Now.AddTicks(11), Now.AddMinutes(3).AddTicks(5));
        var accepted = ModerationProfileCollectionAssessmentAdoption.Adopt(Guid.CreateVersion7(), source,
            procedure, procedureAdoption, age, parent, binding, fixture.ReviewerId, 3,
            Now.AddTicks(13), Now.AddMinutes(2).AddTicks(17));
        fixture.Context.Add(source);
        fixture.Context.Add(accepted);
        await fixture.Context.SaveChangesAsync();
        return (source, accepted);
    }
}
