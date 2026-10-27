using System.Data;
using System.Security.Cryptography;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>
/// Native relational disposal witnesses, authored unrun before the global erasure integration.
/// Assessment construction is storage seeding, not evidence of native investigator permission.
/// </summary>
public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments("subject", false, 1)]
    [Arguments("subject", true, 1)]
    [Arguments("assessor", false, 1)]
    [Arguments("assessor", true, 1)]
    [Arguments("read-issuer", false, 1)]
    [Arguments("read-issuer", true, 1)]
    [Arguments("reporter", false, 1)]
    [Arguments("reporter", true, 1)]
    [Arguments("reporter", false, 101)]
    [Arguments("reporter", true, 101)]
    public async Task GlobalHumanErasureDisposesIncidentAssessmentsBeforeDeadlineWithoutCurrentCustody(
        string actor, bool withdraw, int count)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await SeedIndependentAssessmentErasureActorsAsync(database, count);
        var human = actor switch
        {
            "subject" => source.SubjectId,
            "assessor" => source.AssessorHumanId,
            "read-issuer" => source.ReadIssuerHumanId,
            "reporter" => database.CallerId,
            _ => throw new ArgumentOutOfRangeException(nameof(actor))
        };
        database.Clock.Now = source.QualifiedAt.AddTicks(1);
        await Assert.That(database.Clock.Now < source.PurgeAt).IsTrue();
        await using (var context = database.Context())
        {
            if (withdraw)
            {
                var adoption = await context.ModerationCaseNarrativeAdoptions
                    .SingleAsync(row => row.Id == source.CustodyAdoptionId);
                adoption.Withdraw(1, adoption.AdoptedByHumanId, database.Clock.Now);
                foreach (var assessment in await context.ModerationParticipantIncidentResponsibilitySources.ToArrayAsync())
                    assessment.Withdraw(Guid.CreateVersion7(), 1, source.AssessorHumanId, database.Clock.Now);
                await context.SaveChangesAsync();
            }
            var fact = await RetainAssessmentErasureSagaAsync(context, database, human);
            var position = await context.Set<ModerationRecoveryStoreState>().AsNoTracking()
                .Select(row => row.Position).SingleAsync();
            context.ChangeTracker.Clear();
            // No selected custody authority or protector is supplied to the global owner.
            context.ModerationTrustDomainId = null;
            context.TenantContext = null;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            await Assert.That(await context.Set<ModerationRecoveryStoreState>().AsNoTracking()
                .Select(row => row.Position).SingleAsync()).IsGreaterThan(position);
            await transaction.CommitAsync();
        }
        await using var inspect = database.Context();
        await Assert.That(await inspect.ModerationParticipantIncidentResponsibilitySources.IgnoreQueryFilters().CountAsync())
            .IsEqualTo(0);
        // Reporter erasure must actually delete the original, not merely avoid its restrictive FK.
        await Assert.That(await inspect.ModerationCaseNarratives.IgnoreQueryFilters()
            .AnyAsync(row => row.Id == source.OriginalNarrativeId)).IsEqualTo(actor != "reporter");
        await Assert.That(await inspect.ModerationCases.AnyAsync(row => row.Id == source.OccurrenceCaseId)).IsTrue();
    }

    [Test]
    [Arguments("no-transaction")]
    [Arguments("read-uncommitted")]
    [Arguments("no-saga")]
    [Arguments("intent")]
    [Arguments("subject")]
    [Arguments("sequence")]
    [Arguments("policy")]
    [Arguments("legal-hold")]
    public async Task IncidentAssessmentErasureRequiresTheExactDurableUserSaga(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await SeedIndependentAssessmentErasureActorsAsync(database);
        await using var context = database.Context();
        var fact = await RetainAssessmentErasureSagaAsync(context, database, source.SubjectId, defect != "no-saga");
        var supplied = defect switch
        {
            "intent" => PrivacyErasureIntent.Record(Guid.CreateVersion7(), fact.AuthoritySequence, fact.SubjectKind,
                fact.SubjectId, fact.ReasonCode, fact.PolicyVersion, fact.RecordedAtUtc, fact.RecordedAtUtc),
            "subject" => PrivacyErasureIntent.Record(fact.IntentId, fact.AuthoritySequence, fact.SubjectKind,
                database.CallerId, fact.ReasonCode, fact.PolicyVersion, fact.RecordedAtUtc, fact.RecordedAtUtc),
            "sequence" => PrivacyErasureIntent.Record(fact.IntentId, fact.AuthoritySequence + 1, fact.SubjectKind,
                fact.SubjectId, fact.ReasonCode, fact.PolicyVersion, fact.RecordedAtUtc, fact.RecordedAtUtc),
            "policy" => PrivacyErasureIntent.Record(fact.IntentId, fact.AuthoritySequence, fact.SubjectKind,
                fact.SubjectId, fact.ReasonCode, fact.PolicyVersion + 1, fact.RecordedAtUtc, fact.RecordedAtUtc),
            "legal-hold" => PrivacyErasureIntent.Record(fact.IntentId, fact.AuthoritySequence, fact.SubjectKind,
                fact.SubjectId, fact.ReasonCode, fact.PolicyVersion, fact.RecordedAtUtc, fact.RecordedAtUtc,
                isLegalHoldPseudonymized: true),
            _ => fact
        };
        context.ChangeTracker.Clear();
        await using var transaction = defect == "no-transaction" ? null
            : await context.Database.BeginTransactionAsync(
                defect == "read-uncommitted" ? IsolationLevel.ReadUncommitted : IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(supplied, default));
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationCaseNarratives.AnyAsync(row => row.Id == source.OriginalNarrativeId)).IsTrue();
    }

    [Test]
    public async Task SubjectErasurePreservesAnotherHumansAssessmentOfTheSameIncident()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await SeedIndependentAssessmentErasureActorsAsync(database, 2, separateLastSubject: true);
        Guid survivor;
        await using (var context = database.Context())
        {
            survivor = await context.ModerationParticipantIncidentResponsibilitySources
                .Where(row => row.SubjectId != source.SubjectId).Select(row => row.Id).SingleAsync();
            var fact = await RetainAssessmentErasureSagaAsync(context, database, source.SubjectId);
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            await transaction.CommitAsync();
        }
        await using var inspect = database.Context();
        await Assert.That(await inspect.ModerationParticipantIncidentResponsibilitySources.CountAsync()).IsEqualTo(1);
        await Assert.That(await inspect.ModerationParticipantIncidentResponsibilitySources
            .AnyAsync(row => row.Id == survivor)).IsTrue();
        await Assert.That(await inspect.ModerationCaseNarratives.AnyAsync(row => row.Id == source.OriginalNarrativeId)).IsTrue();
    }

    [Test]
    [Arguments("purchaser")]
    [Arguments("organizer")]
    [Arguments("submitter")]
    [Arguments("linked-user")]
    [Arguments("policy-owner")]
    public async Task GlobalErasurePreservesAssessmentsForUnrelatedHumanRelationships(string relationship)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await SeedIndependentAssessmentErasureActorsAsync(database);
        await using (var context = database.Context())
        {
            Guid unrelated;
            if (relationship == "policy-owner")
                unrelated = await context.ModerationCaseNarrativePolicySources
                    .Where(row => row.Id == source.CustodySourceId).Select(row => row.PrivacyOwnerHumanId).SingleAsync();
            else
            {
                unrelated = Guid.CreateVersion7();
                context.Users.Add(AssessmentErasureHuman(unrelated));
                await context.SaveChangesAsync();
                if (relationship == "purchaser")
                    await context.RegistrationOrders.Where(row => row.Id == source.OrderId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.AccountUserId, unrelated));
                if (relationship == "submitter")
                    await context.Events.Where(row => row.Id == source.EventId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SubmittedByUserId, unrelated));
                if (relationship == "organizer")
                {
                    var organizerId = Guid.CreateVersion7();
                    var organizer = new Actor
                    {
                        Id = organizerId,
                        ActorTypeId = (int)ActorTypeEnum.User,
                        ActorType = null!,
                        UserId = unrelated,
                        Pii = new ActorPii { ActorId = organizerId, DisplayName = "Unrelated organizer" }
                    };
                    context.Actors.Add(organizer);
                    await context.SaveChangesAsync();
                    await context.Events.Where(row => row.Id == source.EventId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.OrganizerActorId, organizer.Id));
                }
                if (relationship == "linked-user")
                    await context.RegistrationParticipants.Where(row => row.Id == source.ParticipantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.LinkedUserId, unrelated));
            }
            var fact = await RetainAssessmentErasureSagaAsync(context, database, unrelated);
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            await transaction.CommitAsync();
        }
        await using var inspect = database.Context();
        await Assert.That(await inspect.ModerationParticipantIncidentResponsibilitySources
            .AnyAsync(row => row.Id == source.Id)).IsTrue();
        await Assert.That(await inspect.ModerationCaseNarratives.AnyAsync(row => row.Id == source.OriginalNarrativeId)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GlobalAssessmentErasureCrossesSelectedTenantAndRollsBackWithItsCaller(bool rollback)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await SeedIndependentAssessmentErasureActorsAsync(database);
        long position;
        await using (var context = database.Context())
        {
            var selected = Guid.CreateVersion7();
            context.Tenants.Add(new Tenant
            {
                Id = selected,
                Slug = $"assessment-erasure-{selected:N}",
                FullName = "Other selected tenant",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await context.SaveChangesAsync();
            await context.Tenants.Where(row => row.Id == database.TenantId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.TenantStatusId, (int)TenantStatusEnum.Archived));
            // The fixture has no membership in this archived original tenant.
            var fact = await RetainAssessmentErasureSagaAsync(context, database, database.CallerId);
            position = await context.Set<ModerationRecoveryStoreState>().AsNoTracking()
                .Select(row => row.Position).SingleAsync();
            context.ChangeTracker.Clear();
            context.TenantContext = new SelectedTenant(selected);
            context.ModerationTrustDomainId = null;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.IgnoreQueryFilters().AnyAsync())
                .IsFalse();
            await Assert.That(await context.ModerationCaseNarratives.IgnoreQueryFilters()
                .AnyAsync(row => row.Id == source.OriginalNarrativeId)).IsFalse();
            if (!rollback) await transaction.CommitAsync();
        }
        await using var inspect = database.Context();
        await Assert.That(await inspect.ModerationParticipantIncidentResponsibilitySources
            .AnyAsync(row => row.Id == source.Id)).IsEqualTo(rollback);
        await Assert.That(await inspect.ModerationCaseNarratives.AnyAsync(row => row.Id == source.OriginalNarrativeId))
            .IsEqualTo(rollback);
        var after = await inspect.Set<ModerationRecoveryStoreState>().AsNoTracking()
            .Select(row => row.Position).SingleAsync();
        if (rollback) await Assert.That(after).IsEqualTo(position);
        else await Assert.That(after).IsGreaterThan(position);
    }

    private static async Task<PrivacyErasureIntent> RetainAssessmentErasureSagaAsync(
        Explore.Persistence.ExploreDbContext context, NativeDatabase database, Guid human, bool retain = true)
    {
        var now = database.Clock.Now.UtcDateTime;
        var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            human, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
        if (retain)
        {
            context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
            await context.SaveChangesAsync();
        }
        return fact;
    }

    private static User AssessmentErasureHuman(Guid id) => new()
    {
        Id = id,
        Pii = new UserPii
        {
            UserId = id,
            Email = $"{id:N}@example.invalid",
            FirstName = "Erasure",
            LastName = "Witness"
        }
    };

    private static async Task<ModerationParticipantIncidentResponsibilitySource> SeedIndependentAssessmentErasureActorsAsync(
        NativeDatabase database, int count = 1, bool separateLastSubject = false)
    {
        var seed = await PersistIncidentAssessmentAsync(database);
        await using var context = database.Context();
        var assessor = Guid.CreateVersion7();
        var issuer = Guid.CreateVersion7();
        var otherSubject = Guid.CreateVersion7();
        context.AddRange(AssessmentErasureHuman(assessor), AssessmentErasureHuman(issuer));
        if (separateLastSubject) context.Users.Add(AssessmentErasureHuman(otherSubject));
        var grant = ModerationCaseReadGrant.Grant(Guid.CreateVersion7(), seed.TenantId, seed.TrustDomainId,
            seed.OccurrenceCaseId, seed.OccurrenceCaseRevision, seed.EvidenceSourceId, assessor, issuer,
            database.AdminId, true, seed.QualifiedAt, seed.ReadGrantExpiresAt);
        context.Add(grant);
        await context.SaveChangesAsync();
        var host = await context.ModerationHostBindings.SingleAsync();
        var occurrence = await context.ModerationCases.Include(row => row.Evidence)
            .SingleAsync(row => row.Id == seed.OccurrenceCaseId);
        var material = await context.ModerationCaseTargetSources.SingleAsync(row => row.Id == seed.EvidenceSourceId);
        var original = await context.ModerationCaseNarratives.SingleAsync(row => row.Id == seed.OriginalNarrativeId);
        var session = await context.EventSessions.Include(row => row.Event).SingleAsync(row => row.Id == seed.SessionId);
        var order = await context.RegistrationOrders.SingleAsync(row => row.Id == seed.OrderId);
        var participant = await context.RegistrationParticipants.SingleAsync(row => row.Id == seed.ParticipantId);
        var policy = await context.Set<ModerationCaseTargetPolicy>().SingleAsync(row => row.Id == seed.QualificationPolicyId);
        var custody = await context.ModerationCaseNarrativePolicySources.SingleAsync(row => row.Id == seed.CustodySourceId);
        var adoption = await context.ModerationCaseNarrativeAdoptions.SingleAsync(row => row.Id == seed.CustodyAdoptionId);
        await context.ModerationParticipantIncidentResponsibilitySources.Where(row => row.Id == seed.Id).ExecuteDeleteAsync();
        ModerationParticipantIncidentResponsibilitySource? first = null;
        for (int index = 0; index < count; index++)
        {
            var source = ModerationParticipantIncidentResponsibilitySource.Record(Guid.CreateVersion7(),
                host, occurrence, seed.EvidenceCaseRevision, material, original, session, order, participant,
                SubjectReference.Create(ModerationSubjectKind.Human,
                    separateLastSubject && index == count - 1 ? otherSubject : seed.SubjectId, occurrence.Scope.Parent!),
                seed.AssessorRoleGrantId, policy, custody, adoption, grant, seed.IncidentWindowFrom,
                seed.IncidentWindowUntil, seed.QualifiedAt, seed.ValidUntil, seed.PurgeAt,
                seed.ProtectedAssessment.Span, seed.AssessmentDigest);
            context.Add(source);
            first ??= source;
        }
        await context.SaveChangesAsync();
        return first!;
    }
}
