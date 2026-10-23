using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task NativeInvestigationAttributionOpensOnlyHumanResponsibilityUnderReview()
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        var request = InvestigationAttribution(fixture);
        await using var context = fixture.Database.Context();
        var owner = InvestigationAttributionOwner(context, fixture);
        var writer = new ModerationCaseAttributionRepository(context, owner,
            fixture.Review.Caller, fixture.Database.Machine);
        var subjectCase = await writer.AttributeAsync(request, default);
        await Assert.That(subjectCase).IsNotNull();
        var replay = await writer.AttributeAsync(request, default);
        await Assert.That(replay!.Id).IsEqualTo(subjectCase!.Id);
        await using var verify = fixture.Database.Context();
        var source = await verify.ModerationCaseAttributionSources.SingleAsync();
        await Assert.That(source.InvestigationSourceId).IsEqualTo(fixture.Request.SourceId);
        await Assert.That(source.InvestigationSourceRevision).IsEqualTo(1L);
        await Assert.That(source.ValidUntil).IsLessThanOrEqualTo(fixture.Request.ValidUntil);
        await Assert.That(subjectCase.Subject!.Id).IsEqualTo(fixture.Request.SubjectHumanId);
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(2);
        await Assert.That(await verify.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationDecisions.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationSanctions.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("revision")]
    [Arguments("subject")]
    [Arguments("basis")]
    [Arguments("expiry")]
    [Arguments("key")]
    [Arguments("ciphertext")]
    [Arguments("assessment")]
    [Arguments("digest")]
    [Arguments("role")]
    [Arguments("purpose")]
    [Arguments("session")]
    [Arguments("participant")]
    [Arguments("order")]
    [Arguments("event")]
    public async Task NativeInvestigationAttributionCannotReplaceAuthenticatedCurrentAssessment(string defect)
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        var request = InvestigationAttribution(fixture);
        IDataProtectionProvider protection = fixture.Database.NarrativeProtection;
        switch (defect)
        {
            case "missing": request = request with { InvestigationSourceId = Guid.CreateVersion7() }; break;
            case "revision": request = request with { InvestigationSourceRevision = 2 }; break;
            case "subject": request = request with { SubjectId = fixture.Database.CallerId }; break;
            case "basis": request = request with { Basis = ModerationResponsibilityBasis.EventSubmission }; break;
            case "key": protection = new EphemeralDataProtectionProvider(); break;
            default: await fixture.SubstituteAsync(defect); break;
        }
        await using var context = fixture.Database.Context();
        var owner = InvestigationAttributionOwner(context, fixture, protection);
        var result = await new ModerationCaseAttributionRepository(context, owner,
            fixture.Review.Caller, fixture.Database.Machine).AttributeAsync(request, default);
        await Assert.That(result).IsNull();
        await using var verify = fixture.Database.Context();
        await Assert.That(await verify.ModerationCaseAttributionSources.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("assessment")]
    [Arguments("digest")]
    [Arguments("session")]
    [Arguments("role")]
    [Arguments("expiry")]
    public async Task NativeInvestigationAttributionFinalFlushLossRollsBackNewSubjectCase(string defect)
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        var checkpoint = new InvestigationAttributionCheckpoint(context => fixture.SubstituteAsync(defect, context));
        await using var context = fixture.Database.Context(checkpoint);
        var owner = InvestigationAttributionOwner(context, fixture);
        var result = await new ModerationCaseAttributionRepository(context, owner,
            fixture.Review.Caller, fixture.Database.Machine)
            .AttributeAsync(InvestigationAttribution(fixture), default);
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(result).IsNull();
        await using var verify = fixture.Database.Context();
        await Assert.That(await verify.ModerationCaseAttributionSources.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task DisposedInvestigationLeavesHistoricalAttributionButCannotQualifyCurrentResponsibility()
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        var request = InvestigationAttribution(fixture);
        await using var context = fixture.Database.Context();
        var owner = InvestigationAttributionOwner(context, fixture);
        var writer = new ModerationCaseAttributionRepository(context, owner,
            fixture.Review.Caller, fixture.Database.Machine);
        var subjectCase = await writer.AttributeAsync(request, default);
        await Assert.That(subjectCase).IsNotNull();
        await using (var disposal = fixture.Database.Context())
            await disposal.ModerationParticipantIncidentResponsibilitySources.ExecuteDeleteAsync();
        await Assert.That(await writer.AttributeAsync(request, default)).IsNull();
        await using var verify = fixture.Database.Context();
        await Assert.That(await verify.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(2);
    }

    private static ModerationCaseAttributionRequest InvestigationAttribution(InvestigationFixture fixture) =>
        new(Guid.CreateVersion7(), fixture.Request.OccurrenceCaseId, fixture.Request.ExpectedCaseRevision,
            fixture.Request.EvidenceCaseRevision, fixture.Request.ReadGrantId,
            ModerationResponsibilityBasis.ParticipantInvestigation, ModerationSubjectKind.Human,
            fixture.Request.SubjectHumanId, fixture.Request.ValidUntil)
        {
            InvestigationSourceId = fixture.Request.SourceId,
            InvestigationSourceRevision = 1
        };

    [Test]
    [Arguments("assessment")]
    [Arguments("digest")]
    [Arguments("session")]
    [Arguments("role")]
    [Arguments("expiry")]
    public async Task NativeInvestigationCurrentAttributionRechecksHeldAssessmentBeforeCommit(string defect)
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        await using var writerContext = fixture.Database.Context();
        var writerOwner = InvestigationAttributionOwner(writerContext, fixture);
        var subjectCase = await new ModerationCaseAttributionRepository(writerContext, writerOwner,
            fixture.Review.Caller, fixture.Database.Machine)
            .AttributeAsync(InvestigationAttribution(fixture), default);
        await Assert.That(subjectCase).IsNotNull();
        await using var context = fixture.Database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = InvestigationAttributionOwner(context, fixture);
        await Assert.That(await owner.AcquireCurrentAttributionAsync(subjectCase!.Id, default)).IsNotNull();
        await Assert.That(await owner.RecheckAttributionAsync(default)).IsTrue();
        await fixture.SubstituteAsync(defect, context);
        await Assert.That(await owner.RecheckAttributionAsync(default)).IsFalse();
    }

    [Test]
    public async Task FreshInvestigationRenewsSameUnderReviewCaseWithoutRenewingOriginalAssessment()
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        await using var context = fixture.Database.Context();
        var owner = InvestigationAttributionOwner(context, fixture);
        var writer = new ModerationCaseAttributionRepository(context, owner,
            fixture.Review.Caller, fixture.Database.Machine);
        var originalRequest = InvestigationAttribution(fixture);
        var subjectCase = await writer.AttributeAsync(originalRequest, default);
        await Assert.That(subjectCase).IsNotNull();
        fixture.Database.Clock.Now = InvestigationFixture.CapturedAt.AddSeconds(10);
        var updated = fixture.Request with
        {
            OperationId = Guid.CreateVersion7(),
            SourceId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = fixture.Request.ExpectedQualificationRevision + 1,
            ValidUntil = fixture.Request.ValidUntil.AddSeconds(10),
            PurgeAt = fixture.Request.PurgeAt.AddSeconds(10),
            Grounds = fixture.Request.Grounds with
            {
                EvidenceProvenance = "Additional first-hand incident-specific observation, independently assessed."
            }
        };
        await Assert.That((await fixture.ExecuteAsync(updated)).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        var renewal = new ModerationCaseAttributionRenewalRequest(subjectCase!.Id, subjectCase.Revision,
            originalRequest.OperationId, 1, 1, originalRequest with
            {
                OperationId = Guid.CreateVersion7(),
                InvestigationSourceId = updated.SourceId,
                ValidUntil = updated.ValidUntil
            });
        var renewed = await writer.RenewAsync(renewal, default);
        await Assert.That(renewed).IsNotNull();
        await Assert.That(renewed!.InvestigationSourceId).IsEqualTo(updated.SourceId);
        await Assert.That(renewed.CaseId).IsEqualTo(subjectCase.Id);
        await Assert.That(renewed.Generation).IsEqualTo(2L);
        var replay = await writer.RenewAsync(renewal, default);
        await Assert.That(replay!.Id).IsEqualTo(renewed.Id);
        await Assert.That(replay.QualifiedAt).IsEqualTo(renewed.QualifiedAt);
        await using var verify = fixture.Database.Context();
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(2);
        var original = await verify.ModerationParticipantIncidentResponsibilitySources
            .SingleAsync(row => row.Id == fixture.Request.SourceId);
        await Assert.That(original.QualifiedAt).IsEqualTo(InvestigationFixture.CapturedAt);
        await Assert.That(original.ValidUntil).IsEqualTo(fixture.Request.ValidUntil);
        await Assert.That(await verify.ModerationFindings.AnyAsync()).IsFalse();
    }

    private sealed class InvestigationAttributionCheckpoint(Func<ExploreDbContext, Task> mutate)
        : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationCaseAttributionSource>().Any())
            {
                Observed = true;
                await mutate(context);
            }
            return result;
        }
    }

    [Test]
    public async Task AnotherNativeReviewerCanRequalifyAssessmentWithoutImpersonatingOriginalInvestigator()
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        await using var write = fixture.Database.Context();
        var original = await new ModerationCaseAttributionRepository(write,
            InvestigationAttributionOwner(write, fixture), fixture.Review.Caller, fixture.Database.Machine)
            .AttributeAsync(InvestigationAttribution(fixture), default);
        await Assert.That(original).IsNotNull();
        var reviewer = new NativeCaller(Guid.CreateVersion7());
        await using (var setup = fixture.Database.Context())
        {
            setup.Users.Add(new User
            {
                Id = reviewer.UserId!.Value,
                Pii = new UserPii
                {
                    UserId = reviewer.UserId.Value,
                    Email = $"{reviewer.UserId:N}@example.invalid",
                    FirstName = "Independent",
                    LastName = "Observer"
                }
            });
            setup.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = reviewer.UserId.Value,
                RoleId = (int)RoleEnum.Admin,
                User = null!,
                Role = null!,
                GrantedAt = fixture.Database.Clock.Now.UtcDateTime
            });
            await setup.SaveChangesAsync();
        }
        await using var context = fixture.Database.Context();
        context.CurrentUserService = reviewer;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = new ModerationCaseTargetProofOwner(context, reviewer,
            fixture.Database.Machine, fixture.Database.Clock, fixture.Database.NarrativeProtection);
        var qualified = await owner.AcquireCurrentAttributionAsync(original!.Id, default);
        await Assert.That(qualified).IsNotNull();
        await Assert.That(qualified!.QualifiedByHumanId).IsEqualTo(fixture.Review.Caller.UserId!.Value);
        await Assert.That(await owner.RecheckAttributionAsync(default)).IsTrue();
        await Assert.That(context.CurrentUserService).IsEqualTo(reviewer);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    private static ModerationCaseTargetProofOwner InvestigationAttributionOwner(
        ExploreDbContext context, InvestigationFixture fixture, IDataProtectionProvider? protection = null)
    {
        context.CurrentUserService = fixture.Review.Caller;
        return new(context, fixture.Review.Caller, fixture.Database.Machine, fixture.Database.Clock,
            protection ?? fixture.Database.NarrativeProtection);
    }

    [Test]
    public async Task InvestigationAuthorizationOnlyReplacementCannotRenewLiveResponsibility()
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        await using var context = fixture.Database.Context();
        var writer = new ModerationCaseAttributionRepository(context,
            InvestigationAttributionOwner(context, fixture), fixture.Review.Caller, fixture.Database.Machine);
        var original = InvestigationAttribution(fixture);
        var subjectCase = await writer.AttributeAsync(original, default);
        await Assert.That(subjectCase).IsNotNull();
        fixture.Database.Clock.Now = InvestigationFixture.CapturedAt.AddSeconds(10);
        var replacement = fixture.Request with
        {
            OperationId = Guid.CreateVersion7(),
            SourceId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = fixture.Request.ExpectedQualificationRevision + 1,
            ValidUntil = fixture.Request.ValidUntil.AddSeconds(10),
            PurgeAt = fixture.Request.PurgeAt.AddSeconds(10)
        };
        await Assert.That((await fixture.ExecuteAsync(replacement)).Outcome)
            .IsEqualTo(Explore.Application.Contracts.Persistence.ModerationQualificationWriteOutcome.Known);
        var result = await writer.RenewAsync(new(subjectCase!.Id, subjectCase.Revision,
            original.OperationId, 1, 1, original with
            {
                OperationId = Guid.CreateVersion7(),
                InvestigationSourceId = replacement.SourceId,
                ValidUntil = replacement.ValidUntil
            }), default);
        await Assert.That(result).IsNull();
        await using var verify = fixture.Database.Context();
        var head = await verify.ModerationCaseAttributionHeads.SingleAsync();
        await Assert.That(head.Generation).IsEqualTo(1L);
        await Assert.That(head.SourceId).IsEqualTo(original.OperationId);
        await Assert.That(await verify.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
    }
}
