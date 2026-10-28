using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

// Reuses the genuine native receipt, independently adopted custody and protected intake
// fixture. Storage factories establish entities, never positive assessment permission.
public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task ParticipantInvestigationCapturesEncryptedIndependentAssessmentAndExactReplay()
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        var accepted = await fixture.ExecuteAsync();
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        fixture.Database.Clock.Now = InvestigationFixture.CapturedAt.AddSeconds(10);
        var replay = await fixture.ExecuteAsync();
        await Assert.That(replay.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(replay.Receipt!.Id).IsEqualTo(accepted.Receipt!.Id);
        await using var context = fixture.Database.Context();
        var row = await context.ModerationParticipantIncidentResponsibilitySources.SingleAsync();
        await Assert.That(row.SubjectId).IsEqualTo(fixture.Request.SubjectHumanId);
        await Assert.That(row.AssessorHumanId).IsEqualTo(fixture.Review.Caller.UserId!.Value);
        await Assert.That(row.ReadIssuerHumanId).IsEqualTo(row.AssessorHumanId);
        await Assert.That(row.PurgeAt).IsEqualTo(fixture.Request.PurgeAt);
        await Assert.That(row.QualifiedAt).IsEqualTo(InvestigationFixture.CapturedAt);
        await Assert.That(row.ProtectedAssessment.Length).IsGreaterThan(32);
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationDecisions.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("session")]
    [Arguments("material")]
    [Arguments("subject")]
    [Arguments("window")]
    [Arguments("grounds")]
    [Arguments("deadline")]
    public async Task ParticipantInvestigationAcceptedOperationRejectsChangedIntent(string substitution)
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var changed = substitution switch
        {
            "session" => fixture.Request with { ExpectedSessionGeneration = Guid.NewGuid() },
            "material" => fixture.Request with { EvidenceSourceVersion = Guid.NewGuid() },
            "subject" => fixture.Request with { SubjectHumanId = fixture.Database.CallerId },
            "window" => fixture.Request with { IncidentWindowFrom = fixture.Request.IncidentWindowFrom.AddSeconds(1) },
            "grounds" => fixture.Request with { Grounds = fixture.Request.Grounds with { AllegedConduct = "A different incident-specific assertion." } },
            "deadline" => fixture.Request with { PurgeAt = fixture.Request.PurgeAt.AddSeconds(-1) },
            _ => throw new ArgumentOutOfRangeException(nameof(substitution))
        };
        await Assert.That((await fixture.ExecuteAsync(changed)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        await using var context = fixture.Database.Context();
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("role")]
    [Arguments("issuer")]
    [Arguments("caller")]
    [Arguments("key")]
    [Arguments("policy")]
    [Arguments("purpose")]
    [Arguments("session")]
    [Arguments("participant")]
    [Arguments("order")]
    [Arguments("event")]
    [Arguments("material")]
    [Arguments("ciphertext")]
    [Arguments("assessment")]
    [Arguments("digest")]
    [Arguments("expiry")]
    [Arguments("reporter")]
    public async Task ParticipantInvestigationRequalifiesNativeFactsAfterPositiveControl(string defect)
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await fixture.SubstituteAsync(defect);
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var context = fixture.Database.Context();
        var retained = await context.ModerationParticipantIncidentResponsibilitySources.SingleAsync();
        await Assert.That(retained.QualifiedAt).IsEqualTo(InvestigationFixture.CapturedAt);
        await Assert.That(retained.PurgeAt).IsEqualTo(fixture.Request.PurgeAt);
    }

    [Test]
    [Arguments("role")]
    [Arguments("session")]
    [Arguments("participant")]
    [Arguments("order")]
    [Arguments("event")]
    [Arguments("ciphertext")]
    [Arguments("assessment")]
    [Arguments("digest")]
    [Arguments("expiry")]
    [Arguments("caller")]
    public async Task ParticipantInvestigationFinalFlushSubstitutionRollsBackAssessmentAndReceipt(string defect)
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        var checkpoint = new InvestigationSavedCheckpoint(context => fixture.SubstituteAsync(defect, context));
        await Assert.That((await fixture.ExecuteAsync(interceptor: checkpoint)).Outcome)
            .IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(checkpoint.Observed).IsTrue();
        await using var context = fixture.Database.Context();
        await Assert.That(await context.ModerationParticipantIncidentResponsibilitySources.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(
            row => row.Id == fixture.Request.OperationId)).IsFalse();
    }

    [Test]
    public async Task ParticipantRosterAndReporterIdentityCannotReplaceIndependentAssessmentAuthority()
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        var reporter = fixture.Request with { SubjectHumanId = fixture.Review.Caller.UserId!.Value };
        await Assert.That((await fixture.ExecuteAsync(reporter)).Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var setup = fixture.Database.Context();
        await setup.PlatformUserRoles.Where(row => row.Id == fixture.Review.AdminGrantId).ExecuteDeleteAsync();
        await setup.RegistrationParticipants.Where(row => row.Id == fixture.Database.ParticipantId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.LinkedUserId, fixture.Review.Caller.UserId));
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
    }

    private sealed class InvestigationFixture(NativeDatabase database, AttributionReview review,
        ModerationNoticePrivacyFixture privacy) : IAsyncDisposable
    {
        public static readonly DateTimeOffset CapturedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public NativeDatabase Database => database;
        public AttributionReview Review => review;
        public ModerationNoticePrivacyFixture Privacy => privacy;
        public RecordModerationParticipantIncidentResponsibilitySource Request { get; private set; } = null!;
        private IDataProtectionProvider protection = database.NarrativeProtection;

        public static async Task<InvestigationFixture> CreateAsync()
        {
            var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
            var review = await PrepareAttributionReviewAsync(database);
            var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
            var fixture = new InvestigationFixture(database, review, privacy);
            await using var context = database.Context();
            var subject = Guid.CreateVersion7();
            context.Users.Add(new User
            {
                Id = subject,
                Pii = new UserPii
                {
                    UserId = subject,
                    Email = $"{subject:N}@example.invalid",
                    FirstName = "Alleged",
                    LastName = "Subject"
                }
            });
            var session = new EventSession(EventSessionStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                EventId = database.EventId,
                Event = null!,
                ConcurrencyStamp = Guid.NewGuid(),
                StartTime = database.Clock.Now.AddMinutes(-10),
                EndTime = database.Clock.Now
            };
            context.EventSessions.Add(session);
            await context.SaveChangesAsync();
            var host = await context.ModerationHostBindings.SingleAsync();
            var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId), database.TenantId);
            var originalPolicy = await context.ModerationCaseNarrativePolicySources.SingleAsync();
            var epoch = await context.ModerationQualificationEpochs.SingleAsync();
            var source = new RecordModerationCaseNarrativePolicySource
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = epoch.Revision,
                SourceId = Guid.CreateVersion7(),
                TargetKind = ModerationCaseTargetKind.ParticipantIncident,
                Terms = new(ModerationCaseNarrativePurpose.ParticipantInvestigation, TimeSpan.FromMinutes(1), 4096),
                Ownership = originalPolicy.Ownership,
                ValidUntil = database.Clock.Now.AddHours(1)
            };
            await Assert.That((await fixture.ExecuteMutationAsync(database.Caller, source)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await fixture.ExecuteMutationAsync(new NativeCaller(source.Ownership.PrivacyOwnerHumanId),
                new AdoptModerationCaseNarrativePolicySource
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = scope,
                    ExpectedQualificationRevision = source.ExpectedQualificationRevision + 1,
                    SourceId = source.SourceId,
                    ExpectedSourceRevision = 1,
                    ExpectedSourceWriterGeneration = source.ExpectedQualificationRevision + 1,
                    AdoptionId = Guid.CreateVersion7(),
                    ValidUntil = source.ValidUntil
                })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            var material = await context.ModerationCaseTargetSources.SingleAsync();
            fixture.Request = new()
            {
                OperationId = Guid.CreateVersion7(),
                Target = scope,
                ExpectedQualificationRevision = source.ExpectedQualificationRevision + 2,
                SourceId = Guid.CreateVersion7(),
                OccurrenceCaseId = review.RootId,
                ExpectedCaseRevision = 1,
                EvidenceCaseRevision = 1,
                ReadGrantId = review.GrantId,
                EvidenceSourceId = material.Id,
                EvidenceSourceVersion = material.Version,
                SubjectHumanId = subject,
                SessionId = session.Id,
                ExpectedSessionGeneration = session.ConcurrencyStamp,
                IncidentWindowFrom = database.Clock.Now.AddMinutes(-5),
                IncidentWindowUntil = database.Clock.Now.AddMinutes(-1),
                ValidUntil = database.Clock.Now.AddSeconds(30),
                PurgeAt = database.Clock.Now.AddSeconds(45),
                Grounds = new("Direct recognition during the named session, not the roster link.",
                    "Observed incident-specific interference during that window.",
                    "First-hand original intake and identified session observation.",
                    "Alternative identification and possible credential misuse remain unresolved.")
            };
            return fixture;
        }

        public Task<ModerationQualificationWriteResult> ExecuteAsync(
            RecordModerationParticipantIncidentResponsibilitySource? request = null, IInterceptor? interceptor = null) =>
            ExecuteMutationAsync(review.Caller, request ?? Request, interceptor);

        private async Task<ModerationQualificationWriteResult> ExecuteMutationAsync(
            NativeCaller caller, ModerationQualificationMutation mutation, IInterceptor? interceptor = null)
        {
            await using var context = interceptor is null ? database.Context() : database.Context(interceptor);
            context.CurrentUserService = caller;
            var admin = NarrativeAdmin(context, caller);
            var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
            var publications = new ModerationPolicyPublicationRepository(context, admin, database.Machine, inventory, null, database.Clock);
            var activation = new ModerationNativeActivationQualification(context, inventory, null,
                new ModerationQualificationPolicySourceOwner(context, database.Clock), database.Clock);
            var owner = new ModerationCaseTargetProofOwner(context, caller, database.Machine, database.Clock);
            return await new ModerationQualificationRepository(context, admin, database.Machine,
                publications, activation, database.Clock, privacy.Authority(context, database.Clock),
                responseProtection: protection, caseAttribution: owner).ExecuteAsync(mutation);
        }

        public async Task SubstituteAsync(string defect, ExploreDbContext? supplied = null)
        {
            await using var owned = supplied is null ? database.Context() : null;
            var context = supplied ?? owned!;
            switch (defect)
            {
                case "role":
                    await context.PlatformUserRoles.Where(row => row.Id == review.AdminGrantId).ExecuteDeleteAsync(); break;
                case "issuer":
                    await context.ModerationCaseReadGrants.Where(row => row.Id == review.GrantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IssuerRoleGrantId, database.AdminId)); break;
                case "caller": review.Caller.IsAuthenticated = false; break;
                case "key": protection = new EphemeralDataProtectionProvider(); break;
                case "policy":
                    await context.ModerationCaseTargetPolicies.ExecuteUpdateAsync(update => update.SetProperty(row => row.Enabled, false)); break;
                case "purpose":
                    await context.ModerationCaseNarrativeAdoptions.Where(row => row.SourceId ==
                        context.ModerationCaseNarrativePolicySources.Where(source => source.Purpose ==
                            ModerationCaseNarrativePurpose.ParticipantInvestigation).Select(source => source.Id).Single())
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.WithdrawnAt, database.Clock.Now)
                            .SetProperty(row => row.WithdrawnByHumanId, database.CallerId)
                            .SetProperty(row => row.Revision, 2)); break;
                case "session":
                    await context.EventSessions.Where(row => row.Id == Request.SessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid())); break;
                case "participant":
                    await context.RegistrationParticipants.Where(row => row.Id == database.ParticipantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid())); break;
                case "order":
                    await context.RegistrationOrders.Where(row => row.Id == database.OrderId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid())); break;
                case "event":
                    await context.Events.Where(row => row.Id == database.EventId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid())); break;
                case "material":
                    await context.ModerationCaseTargetSources.Where(row => row.Id == Request.EvidenceSourceId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Version, Guid.NewGuid())); break;
                case "ciphertext":
                    await context.ModerationCaseNarratives.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => EF.Property<byte[]>(row, "protectedMaterial"), new byte[64])); break;
                case "assessment":
                    await context.ModerationParticipantIncidentResponsibilitySources.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => EF.Property<byte[]>(row, "protectedAssessment"), new byte[64])); break;
                case "digest":
                    await context.ModerationParticipantIncidentResponsibilitySources.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.AssessmentDigest, new string('a', 64))); break;
                case "expiry": database.Clock.Now = Request.ValidUntil; break;
                case "reporter":
                    await context.ModerationCaseNarratives.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.ReporterUserId, review.Caller.UserId!.Value)); break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await privacy.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    private sealed class InvestigationSavedCheckpoint(Func<ExploreDbContext, Task> mutate) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationParticipantIncidentResponsibilitySource>().Any())
            {
                Observed = true;
                await mutate(context);
            }
            return result;
        }
    }
}
