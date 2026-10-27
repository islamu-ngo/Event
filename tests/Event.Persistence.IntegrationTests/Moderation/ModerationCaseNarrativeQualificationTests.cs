using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Native transaction witnesses authored before the independent custody source.</summary>
public sealed class ModerationCaseNarrativeQualificationTests
{
    [Test]
    [Arguments(ModerationCaseNarrativePurpose.ConfidentialAllegation)]
    [Arguments(ModerationCaseNarrativePurpose.ProtectedContestation)]
    public async Task IndependentNarrativeQualificationNeedsNoPublicationOrAttributedCase(ModerationCaseNarrativePurpose purpose)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database) with { Terms = new(purpose, TimeSpan.FromHours(1), 4096) };
        var recorded = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(recorded.IsSuccess).IsTrue();
        var adoption = Adopt(source, database);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsTrue();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationPolicyPublications.AnyAsync()).IsFalse();
        await Assert.That((await context.Set<ModerationCaseNarrativeAdoption>().SingleAsync()).SourceId)
            .IsEqualTo(source.SourceId);
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Service)]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    public async Task ProtectedComplaintCustodyDoesNotBorrowTheAllegationSourceOrAdoption(ModerationCaseTargetKind targetKind)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var allegation = Request(database) with { TargetKind = targetKind };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, allegation)).IsSuccess).IsTrue();
        var complaint = Request(database) with
        {
            ExpectedQualificationRevision = 2,
            TargetKind = targetKind,
            Terms = new(ModerationCaseNarrativePurpose.ProtectedContestation, TimeSpan.FromMinutes(20), 2048)
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, complaint)).IsSuccess).IsTrue();
        var allegationAdoption = Adopt(allegation, database) with { ExpectedQualificationRevision = 3 };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, allegationAdoption)).IsSuccess).IsTrue();
        await using (var pending = database.Context())
        {
            pending.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await pending.Set<ModerationCaseNarrativeAdoption>()
                .AnyAsync(row => row.SourceId == complaint.SourceId)).IsFalse();
        }
        var complaintAdoption = Adopt(complaint, database) with
        {
            ExpectedQualificationRevision = 4,
            ExpectedSourceWriterGeneration = 3
        };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, complaintAdoption)).IsSuccess).IsTrue();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var policies = await context.Set<ModerationCaseNarrativePolicySource>().AsNoTracking().ToListAsync();
        var complaintSource = policies.Single(row => row.Id == complaint.SourceId);
        var adoptions = await context.Set<ModerationCaseNarrativeAdoption>().AsNoTracking().ToListAsync();
        await Assert.That(complaintSource.Purpose).IsEqualTo(ModerationCaseNarrativePurpose.ProtectedContestation);
        await Assert.That(complaintSource.MaximumRetentionTicks).IsEqualTo(TimeSpan.FromMinutes(20).Ticks);
        await Assert.That(adoptions.Single(row => row.Id == allegationAdoption.AdoptionId)
            .MatchesCurrent(complaintSource, database.EvaluationTime)).IsFalse();
        await Assert.That(adoptions.Single(row => row.Id == complaintAdoption.AdoptionId)
            .MatchesCurrent(complaintSource, database.EvaluationTime)).IsTrue();
        await Assert.That(await context.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationSanctions.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task InvestigatorPurposeHasAnIndependentCurrentPolicyLaneAndPrivacyAdoption()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var allegation = Request(database) with { TargetKind = ModerationCaseTargetKind.ParticipantIncident };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, allegation)).IsSuccess).IsTrue();
        var investigation = Request(database) with
        {
            ExpectedQualificationRevision = 2,
            TargetKind = ModerationCaseTargetKind.ParticipantIncident,
            Terms = new(ModerationCaseNarrativePurpose.ParticipantInvestigation, TimeSpan.FromMinutes(30), 4096)
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, investigation)).IsSuccess).IsTrue();
        var allegationAdoption = Adopt(allegation, database) with { ExpectedQualificationRevision = 3 };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, allegationAdoption)).IsSuccess).IsTrue();
        var investigationAdoption = Adopt(investigation, database) with
        {
            ExpectedQualificationRevision = 4,
            ExpectedSourceWriterGeneration = 3
        };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, investigationAdoption)).IsSuccess).IsTrue();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var policies = await context.Set<ModerationCaseNarrativePolicySource>().AsNoTracking().ToListAsync();
        await Assert.That(policies.Count).IsEqualTo(2);
        await Assert.That(policies.Single(row => row.Id == allegation.SourceId).Purpose)
            .IsEqualTo(ModerationCaseNarrativePurpose.ConfidentialAllegation);
        await Assert.That(policies.Single(row => row.Id == investigation.SourceId).Purpose)
            .IsEqualTo(ModerationCaseNarrativePurpose.ParticipantInvestigation);
        await Assert.That(await context.Set<ModerationCaseNarrativeAdoption>().CountAsync()).IsEqualTo(2);
        await Assert.That(await context.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationSanctions.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task InvestigationPurposeCannotBeRecordedForAnotherNativeTarget()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var request = Request(database) with
        {
            Terms = new(ModerationCaseNarrativePurpose.ParticipantInvestigation, TimeSpan.FromMinutes(30), 4096)
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationCaseNarrativePolicySource>().AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false, ModerationCaseNarrativePurpose.ParticipantInvestigation)]
    [Arguments(true, ModerationCaseNarrativePurpose.ParticipantInvestigation)]
    [Arguments(false, ModerationCaseNarrativePurpose.ProtectedContestation)]
    [Arguments(true, ModerationCaseNarrativePurpose.ProtectedContestation)]
    public async Task PurposeSpecificCustodyStillRejectsOwnerLossOrDeadlineAtTheActualFlush(
        bool expires, ModerationCaseNarrativePurpose purpose)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var request = Request(database) with
        {
            TargetKind = ModerationCaseTargetKind.ParticipantIncident,
            Terms = new(purpose, TimeSpan.FromMinutes(30), 4096)
        };
        if (expires)
            database.QualificationSaved = () => database.EvaluationTime = request.ValidUntil;
        else
        {
            await using var writer = database.Context();
            await writer.PlatformUserRoles.Where(row => row.UserId == database.UrgencyAssessorId).ExecuteDeleteAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationCaseNarrativePolicySource>().AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationCaseNarrativeAdoption>().AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("recorder")]
    [Arguments("owner")]
    public async Task RoleRevocationRejectsExactReplayRatherThanReusingHistoricalAuthority(string revoked)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(source, database);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsTrue();
        await using (var context = database.Context())
        {
            var user = revoked == "recorder" ? database.AdministratorId : database.UrgencyAssessorId;
            await context.PlatformUserRoles.Where(row => row.UserId == user).ExecuteDeleteAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsFalse();
    }

    [Test]
    public async Task FinalFlushDeadlineRollsBackSourceReceiptAndEpoch()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        database.QualificationSaved = () => database.EvaluationTime = source.ValidUntil;
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationCaseNarrativePolicySource>().AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(ModerationCaseNarrativePurpose.ConfidentialAllegation)]
    [Arguments(ModerationCaseNarrativePurpose.ProtectedContestation)]
    public async Task WithdrawnAdoptionCannotBeReplayedIntoCurrentAuthority(ModerationCaseNarrativePurpose purpose)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database) with { Terms = new(purpose, TimeSpan.FromHours(1), 4096) };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(source, database);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsTrue();
        var withdrawal = new WithdrawModerationCaseNarrativeAdoption
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            AdoptionId = adoption.AdoptionId,
            ExpectedAdoptionRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, withdrawal)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsFalse();
    }

    internal static RecordModerationCaseNarrativePolicySource Request(SourceDatabase database) => new()
    {
        OperationId = Guid.CreateVersion7(),
        Target = database.Scope,
        ExpectedQualificationRevision = 1,
        SourceId = Guid.CreateVersion7(),
        TargetKind = ModerationCaseTargetKind.Service,
        Terms = new(ModerationCaseNarrativePurpose.ConfidentialAllegation, TimeSpan.FromHours(1), 4096),
        Ownership = new(database.UrgencyAssessorId, database.ReviewerId, database.AdministratorId,
            "Separately accountable canonical controller review.",
            "Finite allegation custody with independent ordinary erasure."),
        ValidUntil = database.EvaluationTime.AddHours(2)
    };

    private static AdoptModerationCaseNarrativePolicySource Adopt(
        RecordModerationCaseNarrativePolicySource source, SourceDatabase database) => new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            SourceId = source.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 2,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = source.ValidUntil
        };
}
