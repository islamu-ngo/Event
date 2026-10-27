using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments("expiry")]
    [Arguments("assessment-withdrawal")]
    [Arguments("investigation-withdrawal")]
    [Arguments("original-withdrawal")]
    public async Task AssessmentCleanupDisposesOnlyEndedCustodyWithoutRoleOrProtectorVeto(string ended)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        database.Clock.Now = source.QualifiedAt.AddTicks(1);
        await using (var setup = database.Context())
        {
            if (ended == "expiry") database.Clock.Now = source.PurgeAt;
            if (ended == "assessment-withdrawal")
            {
                var row = await setup.ModerationParticipantIncidentResponsibilitySources.SingleAsync();
                row.Withdraw(Guid.CreateVersion7(), 1, source.AssessorHumanId, database.Clock.Now);
            }
            if (ended is "investigation-withdrawal" or "original-withdrawal")
            {
                var adoptionId = ended == "investigation-withdrawal" ? source.CustodyAdoptionId
                    : (await setup.ModerationCaseNarratives.SingleAsync()).AdoptionId;
                var adoption = await setup.ModerationCaseNarrativeAdoptions.SingleAsync(row => row.Id == adoptionId);
                adoption.Withdraw(1, adoption.AdoptedByHumanId, database.Clock.Now);
            }
            await setup.SaveChangesAsync();
            await setup.PlatformUserRoles.ExecuteDeleteAsync();
        }
        await using var context = database.Context();
        context.ModerationTrustDomainId = null;
        await Assert.That(await AssessmentCleanupStore(context, database).PurgeExpiredAsync(1, default)).IsEqualTo(1);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseNarratives.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("investigation")]
    [Arguments("original")]
    public async Task ClockRegressionBeforeRecordedWithdrawalCannotDisposeAssessmentBySqliteRounding(string purpose)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        var withdrawnAt = source.QualifiedAt.AddSeconds(30);
        await using (var setup = database.Context())
        {
            var adoptionId = purpose == "investigation" ? source.CustodyAdoptionId
                : (await setup.ModerationCaseNarratives.SingleAsync()).AdoptionId;
            var adoption = await setup.ModerationCaseNarrativeAdoptions.SingleAsync(row => row.Id == adoptionId);
            adoption.Withdraw(1, adoption.AdoptedByHumanId, withdrawnAt);
            await setup.SaveChangesAsync();
        }
        await using var context = database.Context();
        context.ModerationTrustDomainId = null;
        var store = AssessmentCleanupStore(context, database);
        database.Clock.Now = withdrawnAt.AddTicks(-1);
        await Assert.That(await store.PurgeExpiredAsync(1, default)).IsEqualTo(0);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.AnyAsync()).IsTrue();
        await Assert.That(await context.ModerationCaseNarratives.CountAsync()).IsEqualTo(1);
        context.ModerationTrustDomainId = null;
        database.Clock.Now = withdrawnAt;
        await Assert.That(await store.PurgeExpiredAsync(1, default)).IsEqualTo(1);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task AssessmentCleanupHonorsExactPhysicalDeadlineWithoutPrematureDeletion()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        await using var context = database.Context();
        context.ModerationTrustDomainId = null;
        var store = AssessmentCleanupStore(context, database);
        database.Clock.Now = source.PurgeAt.AddTicks(-1);
        await Assert.That(await store.PurgeExpiredAsync(1, default)).IsEqualTo(0);
        database.Clock.Now = source.PurgeAt;
        await Assert.That(await store.PurgeExpiredAsync(1, default)).IsEqualTo(1);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseNarratives.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task OneRowCleanupBudgetDisposesAssessmentBeforeItsExpiredOriginalWithoutBreakingAncestry()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        database.Clock.Now = source.OriginalNarrativePurgeAt;
        await using var context = database.Context();
        context.ModerationTrustDomainId = null;
        var store = AssessmentCleanupStore(context, database);
        await Assert.That(await store.PurgeExpiredAsync(1, default)).IsEqualTo(1);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseNarratives.CountAsync()).IsEqualTo(1);
        context.ModerationTrustDomainId = null;
        await Assert.That(await store.PurgeExpiredAsync(1, default)).IsEqualTo(1);
        await Assert.That(await store.PurgeExpiredAsync(1, default)).IsEqualTo(0);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCaseNarratives.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    private static ModerationCaseNarrativeRepository AssessmentCleanupStore(
        ExploreDbContext context, NativeDatabase database)
    {
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, database.Caller, database.Machine, database.Clock),
            database.Owner(context), database.Clock);
        return new(context, NarrativeAdmin(context, database.Caller), database.Caller,
            database.Machine, intake, database.Clock);
    }

    [Test]
    public async Task SelectedReporterCleanupCannotDisposeAssessmentBeforeDurableErasureSaga()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        database.Clock.Now = source.QualifiedAt.AddTicks(1);
        await using var context = database.Context();
        context.ModerationTrustDomainId = null;
        var store = AssessmentCleanupStore(context, database);
        await Assert.That(await store.PurgeForErasureAsync(database.CallerId, 1, default)).IsEqualTo(0);
        var now = database.Clock.Now.UtcDateTime;
        var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            database.CallerId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
        context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await Assert.That(await store.PurgeForErasureAsync(database.CallerId, 1, default)).IsEqualTo(1);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseNarratives.CountAsync()).IsEqualTo(1);
    }
}
