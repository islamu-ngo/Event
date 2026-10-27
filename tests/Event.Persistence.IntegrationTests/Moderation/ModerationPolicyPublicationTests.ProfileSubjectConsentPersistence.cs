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
    /// Mapping-only witnesses: binding and applicability are explicit valid Domain-state
    /// preconditions, not independently authenticated observations or collection receipts.
    /// The fixture declares historical ACTIVE publication state; its real owner/reviewer
    /// writers independently record and adopt the new consent-reference procedure.
    /// </summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileConsent_RoundtripRetainsExactOriginalMetadata(bool instanceJurisdiction)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileConsentAsync(fixture, instanceJurisdiction);
        fixture.Context.ChangeTracker.Clear();
        var restored = await fixture.Context.Set<ModerationProfileSubjectConsent>().AsNoTracking().SingleAsync();
        restored.ValidatePersistedState();
        await Assert.That(restored.Id).IsEqualTo(original.Id);
        await Assert.That(restored.SubjectHumanId).IsEqualTo(fixture.SubjectId);
        await Assert.That(restored.Source.Matches(original.Source)).IsTrue();
        await Assert.That(restored.Binding).IsEqualTo(original.Binding);
        await Assert.That(restored.Assessment.Binding).IsEqualTo(original.Assessment.Binding);
        await Assert.That(restored.Assessment.Reference).IsEqualTo(original.Assessment.Reference);
        await Assert.That(restored.Assessment.Required.SequenceEqual(original.Assessment.Required)).IsTrue();
        await Assert.That(restored.Generation).IsEqualTo(3);
        await Assert.That(restored.Revision).IsEqualTo(1);
        await Assert.That(restored.GrantedAt).IsEqualTo(Now.AddTicks(11));
        await Assert.That(restored.ValidUntil).IsEqualTo(Now.AddMinutes(2).AddTicks(19));
        await Assert.That(restored.WithdrawnAt).IsNull();
        await Assert.That(restored.ProcedureAdoptionId).IsEqualTo(original.ProcedureAdoptionId);
        await Assert.That(restored.ProcedureAdoptionRevision).IsEqualTo(original.ProcedureAdoptionRevision);
        await Assert.That(restored.ProcedureAdoptionWriterGeneration).IsEqualTo(8);
        await Assert.That(restored.ProcedureAdoptedByHumanId).IsEqualTo(fixture.ReviewerId);
        await Assert.That(restored.ProcedureAdoptedAt).IsEqualTo(original.ProcedureAdoptedAt);
        await Assert.That(restored.ProcedureAdoptionValidUntil).IsEqualTo(original.ProcedureAdoptionValidUntil);
    }

    [Test]
    [Arguments("SourceId")]
    [Arguments("SubjectHumanId")]
    [Arguments("Generation")]
    [Arguments("GrantedAt")]
    [Arguments("ValidUntil")]
    [Arguments("ProcedureAdoptionId")]
    [Arguments("ProcedureAdoptionRevision")]
    [Arguments("ProcedureAdoptionWriterGeneration")]
    [Arguments("ProcedureAdoptedByHumanId")]
    [Arguments("ProcedureAdoptedAt")]
    [Arguments("ProcedureAdoptionValidUntil")]
    [Arguments("Binding")]
    [Arguments("Assessment")]
    public async Task ProfileConsent_SavedOriginalsCannotBeRebound(string property)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var consent = await PersistProfileConsentAsync(fixture);
        var entry = fixture.Context.Entry(consent);
        var expected = entry.Property(property).CurrentValue;
        var sourceId = consent.Source.Id;
        var assessmentId = consent.Assessment.Reference.Id;
        entry.Property(property).CurrentValue = property switch
        {
            "SourceId" => fixture.Record.SourceId,
            "SubjectHumanId" => fixture.OtherSubjectId,
            "Generation" => 4L,
            "GrantedAt" => consent.GrantedAt.AddTicks(1),
            "ValidUntil" => consent.ValidUntil.AddTicks(-1),
            "ProcedureAdoptionId" => Guid.CreateVersion7(),
            "ProcedureAdoptionRevision" => 2L,
            "ProcedureAdoptionWriterGeneration" => 9L,
            "ProcedureAdoptedByHumanId" => fixture.OwnerId,
            "ProcedureAdoptedAt" => consent.ProcedureAdoptedAt.AddTicks(1),
            "ProcedureAdoptionValidUntil" => consent.ProcedureAdoptionValidUntil.AddTicks(-1),
            "Binding" => ProfileConsentBinding(fixture, consent.Source, fixture.OtherSubjectId, false),
            "Assessment" => new ModerationCollectionPrerequisiteAssessment(consent.Binding,
                new(Guid.CreateVersion7(), 2, 29, Now, Now.AddMinutes(3)), consent.Assessment.Required),
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
        await Assert.That(async () => await fixture.Context.SaveChangesAsync()).Throws<InvalidOperationException>();
        fixture.Context.ChangeTracker.Clear();
        var retained = await fixture.Context.Set<ModerationProfileSubjectConsent>().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Source.Id).IsEqualTo(sourceId);
        await Assert.That(retained.Generation).IsEqualTo(3);
        await Assert.That(retained.SubjectHumanId).IsEqualTo(fixture.SubjectId);
        await Assert.That(retained.Assessment.Reference.Id).IsEqualTo(assessmentId);
        if (property != "Assessment")
            await Assert.That(fixture.Context.Entry(retained).Property(property).CurrentValue).IsEqualTo(expected);
    }

    [Test]
    public async Task ProfileConsent_OnlyOriginalSubjectWithdrawsWithTerminalRevisionCas()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileConsentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        var first = await fixture.Context.Set<ModerationProfileSubjectConsent>().SingleAsync();
        await using var competitor = fixture.Database.Context();
        competitor.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var stale = await competitor.Set<ModerationProfileSubjectConsent>().SingleAsync();
        await Assert.That(() => first.Withdraw(1, fixture.OtherSubjectId, Now.AddMinutes(1))).Throws<ArgumentException>();
        await Assert.That(() => first.Withdraw(2, fixture.SubjectId, Now.AddMinutes(1))).Throws<InvalidOperationException>();
        // Withdrawal remains possible after grant expiry; it does not renew the original window.
        first.Withdraw(1, fixture.SubjectId, Now.AddMinutes(9));
        await fixture.Context.SaveChangesAsync();
        stale.Withdraw(1, fixture.SubjectId, Now.AddMinutes(10));
        await Assert.That(async () => await competitor.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        fixture.Context.ChangeTracker.Clear();
        var terminal = await fixture.Context.Set<ModerationProfileSubjectConsent>().AsNoTracking().SingleAsync();
        terminal.ValidatePersistedState();
        await Assert.That(terminal.Revision).IsEqualTo(2);
        await Assert.That(terminal.WithdrawnAt).IsEqualTo(Now.AddMinutes(9));
        await Assert.That(terminal.Binding).IsEqualTo(original.Binding);
        await Assert.That(terminal.ValidUntil).IsEqualTo(original.ValidUntil);
        await Assert.That(() => terminal.Withdraw(2, fixture.SubjectId, Now.AddMinutes(11))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ProfileConsent_PiiHardDeleteRemovesMetadataButNotHumanOrProcedure()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var consent = await PersistProfileConsentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        await fixture.Context.Set<UserPii>().Where(row => row.UserId == fixture.SubjectId).ExecuteDeleteAsync();
        await Assert.That(await fixture.Context.Set<ModerationProfileSubjectConsent>().AnyAsync()).IsFalse();
        await Assert.That(await fixture.Context.Users.AnyAsync(row => row.Id == fixture.SubjectId)).IsTrue();
        await Assert.That(await fixture.Context.Set<ModerationProfileCollectionProcedureSource>()
            .AnyAsync(row => row.Id == consent.Source.Id)).IsTrue();
    }

    [Test]
    public async Task ProfileConsent_OriginalSubjectSourceSlotGenerationIsUnique()
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        var original = await PersistProfileConsentAsync(fixture);
        var adoption = await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>()
            .SingleAsync(row => row.Id == original.ProcedureAdoptionId);
        var age = await fixture.Context.Set<ModerationChildAgePolicySource>().SingleAsync();
        var parent = await fixture.Context.Set<ModerationChildAgePolicyAdoption>().SingleAsync();
        fixture.Context.Add(ModerationProfileSubjectConsent.Grant(Guid.CreateVersion7(), original.Source,
            adoption, age, parent, original.Binding, original.Assessment, fixture.SubjectId, 3,
            original.GrantedAt, original.ValidUntil));
        await Assert.That(async () => await fixture.Context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    [Arguments("generation = 0")]
    [Arguments("generation = 9223372036854775807")]
    [Arguments("revision = 2")]
    [Arguments("withdrawn_at = granted_at")]
    [Arguments("valid_until = granted_at")]
    [Arguments("granted_at = 0")]
    [Arguments("procedure_adoption_revision = 2")]
    [Arguments("procedure_adoption_valid_until = procedure_adopted_at")]
    public async Task ProfileConsent_RelationalConstraintsRejectMalformedLifecycle(string assignment)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await PersistProfileConsentAsync(fixture);
        // Fixed test-case SQL simulates damaged storage, never a supported consent writer.
        await Assert.That(async () => await fixture.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE moderation_profile_subject_consents SET {assignment}")).Throws<Microsoft.Data.Sqlite.SqliteException>();
    }

    [Test]
    [Arguments("UnknownKind")]
    [Arguments("AliasedSubject")]
    [Arguments("WrongPurpose")]
    [Arguments("MissingOrigin")]
    [Arguments("UnknownMember")]
    [Arguments("DuplicateMember")]
    [Arguments("UnknownRequired")]
    [Arguments("DuplicateRequired")]
    [Arguments("ForeignAssessmentBinding")]
    public async Task ProfileConsent_DamagedSnapshotsFailConversionOrModelValidation(string damage)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await PersistProfileConsentAsync(fixture);
        var assessment = damage is "UnknownRequired" or "DuplicateRequired" or "ForeignAssessmentBinding";
        var column = assessment ? "assessment" : "binding";
        var encoded = await fixture.Context.Database.SqlQueryRaw<string>(
            $"SELECT {column} AS Value FROM moderation_profile_subject_consents").SingleAsync();
        var snapshot = JsonNode.Parse(encoded)!.AsObject();
        switch (damage)
        {
            case "UnknownKind": snapshot["TargetKind"] = 99; break;
            case "AliasedSubject": snapshot["SubjectKind"] = (int)ModerationSubjectKind.Service; break;
            case "WrongPurpose": snapshot["Purpose"] = (int)ModerationIndividualPurpose.ParticipantCollection; break;
            case "MissingOrigin": snapshot.Remove("Origin"); break;
            case "UnknownMember": snapshot["BearerToken"] = "not-a-supported-field"; break;
            case "UnknownRequired": snapshot["Required"] = new JsonArray(2, 99); break;
            case "DuplicateRequired": snapshot["Required"] = new JsonArray(2, 2); break;
            case "ForeignAssessmentBinding":
                snapshot["Binding"]!["SubjectId"] = fixture.OtherSubjectId.ToString(); break;
        }
        var corrupt = damage == "DuplicateMember"
            ? encoded.Insert(1, "\"TargetKind\":99,") : snapshot.ToJsonString();
        if (assessment)
            await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE moderation_profile_subject_consents SET assessment = {corrupt}");
        else
            await fixture.Context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE moderation_profile_subject_consents SET binding = {corrupt}");
        fixture.Context.ChangeTracker.Clear();
        async Task ReadAndValidate()
        {
            var row = await fixture.Context.Set<ModerationProfileSubjectConsent>().AsNoTracking().SingleAsync();
            row.ValidatePersistedState();
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
    public async Task ProfileConsent_PrivateRowsRequireExactNativeTrustAndTenant(string scope)
    {
        await using var fixture = await IndividualPolicyFixture.CreateAsync();
        await PersistProfileConsentAsync(fixture);
        fixture.Context.ChangeTracker.Clear();
        await using var reader = fixture.Database.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        if (scope == "MissingTrust") reader.ModerationTrustDomainId = null;
        if (scope == "ForeignTrust") reader.ModerationTrustDomainId = Guid.CreateVersion7();
        if (scope == "MissingTenant") reader.TenantContext = null;
        if (scope == "ForeignTenant") reader.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
        await Assert.That(await reader.Set<ModerationProfileSubjectConsent>().AsNoTracking().AnyAsync()).IsFalse();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        reader.TenantContext = fixture.Database;
        await Assert.That(await reader.Set<ModerationProfileSubjectConsent>().AsNoTracking().CountAsync()).IsEqualTo(1);
    }

    private static ModerationIndividualBinding ProfileConsentBinding(IndividualPolicyFixture fixture,
        ModerationProfileCollectionProcedureSource source, Guid subject, bool instanceJurisdiction) => new(
        ModerationIndividualTarget.Human(SubjectReference.Create(ModerationSubjectKind.Human,
            subject, fixture.Database.Scope.Parent!)),
        new ModerationAgePolicy(source.PolicyId, source.PolicyRevision,
            instanceJurisdiction ? fixture.Database.Scope.Parent! : fixture.Database.Scope),
        ModerationIndividualPurpose.ProfileCompletion, source.Id, 1, 7,
        Guid.CreateVersion7(), 17, Now.AddTicks(3), Now.AddMinutes(3).AddTicks(7));

    private static async Task<ModerationProfileSubjectConsent> PersistProfileConsentAsync(
        IndividualPolicyFixture fixture, bool instanceJurisdiction = false)
    {
        var procedure = new ModerationProfileCollectionProcedure(ModerationIndividualPurpose.ProfileCompletion,
            ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
            [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration,
                ModerationProfilePreliminaryFieldKind.SubjectConsentReference],
            [ModerationProfileNormalFieldKind.DateOfBirth],
            "Independent native guardian relationship review.", "Independent native manual issuer qualification.");
        var record = fixture.Record with
        {
            OperationId = Guid.CreateVersion7(),
            SourceId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 6,
            Procedure = procedure
        };
        await Assert.That((await fixture.Writer(fixture.OwnerId).ExecuteAsync(record)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var adopt = fixture.Adopt with
        {
            OperationId = Guid.CreateVersion7(),
            SourceId = record.SourceId,
            ExpectedQualificationRevision = 7,
            ExpectedSourceWriterGeneration = 7,
            AdoptionId = Guid.CreateVersion7()
        };
        await Assert.That((await fixture.Writer(fixture.ReviewerId).ExecuteAsync(adopt)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        fixture.Context.ChangeTracker.Clear();
        var source = await fixture.Context.Set<ModerationProfileCollectionProcedureSource>()
            .SingleAsync(row => row.Id == record.SourceId);
        var adoption = await fixture.Context.Set<ModerationProfileCollectionProcedureAdoption>()
            .SingleAsync(row => row.Id == adopt.AdoptionId);
        var age = await fixture.Context.Set<ModerationChildAgePolicySource>().SingleAsync();
        var parent = await fixture.Context.Set<ModerationChildAgePolicyAdoption>().SingleAsync();
        var binding = ProfileConsentBinding(fixture, source, fixture.SubjectId, instanceJurisdiction);
        // These complete semantic snapshots are test preconditions, not accepted native evidence.
        var assessment = new ModerationCollectionPrerequisiteAssessment(binding,
            new(Guid.CreateVersion7(), 2, 29, Now.AddTicks(5), Now.AddMinutes(3).AddTicks(13)),
            [ModerationCollectionPrerequisiteKind.AccountEligibility, ModerationCollectionPrerequisiteKind.SubjectConsent,
                ModerationCollectionPrerequisiteKind.GuardianConsent]);
        var consent = ModerationProfileSubjectConsent.Grant(Guid.CreateVersion7(), source, adoption,
            age, parent, binding, assessment, fixture.SubjectId, 3,
            Now.AddTicks(11), Now.AddMinutes(2).AddTicks(19));
        fixture.Context.Add(consent);
        await fixture.Context.SaveChangesAsync();
        return consent;
    }
}
