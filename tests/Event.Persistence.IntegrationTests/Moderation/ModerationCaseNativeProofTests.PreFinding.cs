using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task NativePreFindingQueryIsSingleUseAndDisclosesOnlyItsAuthorizedSubjectCase()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var pending = await new GetModerationPreFindingNoticeQueryHandler(
            fixture.Repository(context, fixture.Database.CallerId), new SelectedTenant(fixture.Database.TenantId))
            .QueryAsync(new(fixture.NoticeId));
        var writes = 0;
        var outcome = await pending.DiscloseAsync((view, _) =>
        {
            if (view.CaseId != fixture.Case.Id || view.CaseId == fixture.Review.RootId || view.CaseRevision != 1)
                throw new InvalidOperationException("The private occurrence cannot replace the authorized subject case.");
            return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 });
        }, (_, _) => { writes++; return Task.CompletedTask; }, default);
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(await pending.DiscloseAsync(
            (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 }),
            (_, _) => { writes++; return Task.CompletedTask; }, default))
            .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(writes).IsEqualTo(1);
        await using var inspect = fixture.Database.Context();
        await Assert.That((await inspect.Set<ModerationPreFindingNotice>().SingleAsync()).AcknowledgedAt).IsNull();
    }

    [Test]
    public async Task NativePreFindingQueryCannotRetargetItsCapturedTenantBeforeDisclosure()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var pending = await new GetModerationPreFindingNoticeQueryHandler(
            fixture.Repository(context, fixture.Database.CallerId), new SelectedTenant(Guid.CreateVersion7()))
            .QueryAsync(new(fixture.NoticeId));
        var prepared = false;
        var wrote = false;
        var outcome = await pending.DiscloseAsync((_, _) =>
        {
            prepared = true;
            return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 });
        }, (_, _) => { wrote = true; return Task.CompletedTask; }, default);
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(prepared).IsFalse();
        await Assert.That(wrote).IsFalse();
    }

    [Test]
    public async Task NativePreFindingQueryRefusesPreparedOutputAfterRecipientAuthorityChanges()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var pending = await new GetModerationPreFindingNoticeQueryHandler(
            fixture.Repository(context, fixture.Database.CallerId), new SelectedTenant(fixture.Database.TenantId))
            .QueryAsync(new(fixture.NoticeId));
        var wrote = false;
        var outcome = await pending.DiscloseAsync(async (_, token) =>
        {
            await context.EventRoleAssignments.Where(row => row.Id == fixture.Database.RoleId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Version, row => row.Version + 1), token);
            return new byte[] { 1 };
        }, (_, _) => { wrote = true; return Task.CompletedTask; }, default);
        await Assert.That(outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(wrote).IsFalse();
    }

    [Test]
    public async Task NativePreFindingContentResponseNeedsNoOriginalDecisionAndNeverCreatesHumanGuilt()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var command = fixture.Response();
        var accepted = await fixture.ExecuteAsync(fixture.Database.CallerId, command);
        var replay = await fixture.ExecuteAsync(fixture.Database.CallerId, command);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(replay).IsEqualTo(accepted);
        await using var inspect = fixture.Database.Context();
        var notice = await inspect.Set<ModerationPreFindingNotice>().SingleAsync();
        await Assert.That(notice.HasResponseAt(fixture.Database.Clock.Now)).IsTrue();
        await Assert.That(notice.SubjectKind).IsEqualTo(ModerationSubjectKind.Content);
        await Assert.That(notice.SubjectId).IsEqualTo(fixture.Database.EventId);
        await Assert.That(notice.RecipientHumanId).IsEqualTo(fixture.Database.CallerId);
        await Assert.That(notice.EvidenceRevision).IsEqualTo(1L);
        await Assert.That(await inspect.ModerationDecisions.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationSanctions.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationCases.SingleAsync(row => row.Id == fixture.Review.RootId)).Subject).IsNull();
        var disclosed = false;
        var outcome = await fixture.DiscloseAsync(fixture.Database.CallerId,
            (view, _) =>
            {
                if (view.Response != command.Statement) throw new InvalidOperationException("The original response was not retained.");
                return Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 });
            }, (_, _) => { disclosed = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(disclosed).IsTrue();
    }

    [Test]
    public async Task NativePreFindingAcknowledgementCannotQualifySilenceAsResponse()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var accepted = await fixture.ExecuteAsync(fixture.Database.CallerId,
            new AcknowledgeModerationPreFindingNotice(Guid.CreateVersion7(), fixture.Case.Id, 1, fixture.NoticeId, 1));
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        fixture.Database.Clock.Now = fixture.Database.Clock.Now.AddSeconds(10);
        await using var inspect = fixture.Database.Context();
        var notice = await inspect.Set<ModerationPreFindingNotice>().SingleAsync();
        await Assert.That(notice.AcknowledgedAt).IsNotNull();
        await Assert.That(notice.HasResponseAt(fixture.Database.Clock.Now)).IsFalse();
        await Assert.That(await inspect.ModerationQualificationEvidence
            .AnyAsync(row => row.Purpose == ModerationQualificationEvidencePurpose.Response)).IsFalse();
    }

    [Test]
    [Arguments("replacement")]
    [Arguments("attribution")]
    [Arguments("expiry")]
    [Arguments("privacy")]
    public async Task NativePreFindingResponseClosesWhenOriginalCustodyChanges(string change)
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        if (change == "expiry") fixture.Database.Clock.Now = fixture.Until;
        else if (change == "privacy")
        {
            await using var primary = fixture.Database.Context();
            await fixture.Privacy.Authority(primary, fixture.Database.Clock).AppendAsync(new(
                Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                fixture.Database.CallerId, PrivacyErasureReasonCode.AccountDeletion, 1));
        }
        else
        {
            await using var mutate = fixture.Database.Context();
            if (change == "attribution")
                await AttributionWriter(mutate, fixture.Database, fixture.Review.Caller)
                    .WithdrawAsync(fixture.Case.AttributionSourceId!.Value, Guid.CreateVersion7(), 1, default);
            else
            {
                var original = await mutate.EventRoleAssignments.SingleAsync(row => row.Id == fixture.Database.RoleId);
                original.Revoke(fixture.Assessor, fixture.Database.Clock.Now.UtcDateTime);
                mutate.EventRoleAssignments.Add(EventRoleAssignment.Create(fixture.Database.TenantId,
                    fixture.Database.EventId, fixture.Database.CallerId, (int)RoleEnum.EventManager,
                    EventRoleAssignmentStatus.Active, fixture.Database.Clock.Now.UtcDateTime,
                    fixture.Database.Clock.Now.AddMinutes(1).UtcDateTime, fixture.Assessor));
                await mutate.SaveChangesAsync();
            }
        }
        var result = await fixture.ExecuteAsync(fixture.Database.CallerId, fixture.Response());
        await Assert.That(result.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var inspect = fixture.Database.Context();
        await Assert.That((await inspect.Set<ModerationPreFindingNotice>().SingleAsync()).ResponseId).IsNull();
    }

    [Test]
    public async Task NativePreFindingOutputRechecksRoleAtActualWriteAfterPreparation()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var wrote = false;
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context, fixture.Database.CallerId);
        var outcome = await repository.DisclosePreFindingAsync(fixture.Database.TenantId, fixture.NoticeId, false, async (_, token) =>
        {
            await context.EventRoleAssignments.Where(row => row.Id == fixture.Database.RoleId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Version, row => row.Version + 1), token);
            return new byte[] { 1 };
        }, (_, _) => { wrote = true; return Task.CompletedTask; }, default);
        await Assert.That(outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(wrote).IsFalse();
    }

    [Test]
    public async Task NativePreFindingExpiryRemovesCiphertextWithoutRenewingOrReadingIt()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, fixture.Response())).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        fixture.Database.Clock.Now = fixture.Until;
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context, fixture.Assessor);
        await Assert.That(await repository.PurgeExpiredPreFindingAsync(100, default)).IsEqualTo(1);
        await using var inspect = fixture.Database.Context();
        var notice = await inspect.Set<ModerationPreFindingNotice>().SingleAsync();
        await Assert.That(notice.ProtectedNotice.IsEmpty).IsTrue();
        await Assert.That(notice.ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That(notice.HasResponseAt(fixture.Until)).IsFalse();
        await Assert.That(notice.RetainUntil).IsEqualTo(fixture.Until);
        await AssertPreFindingPointerGraphAsync(inspect, fixture, retained: false);
        await Assert.That(await repository.PurgeExpiredPreFindingAsync(100, default)).IsEqualTo(0);
    }

    [Test]
    [Arguments("recipient", false)]
    [Arguments("recipient", true)]
    [Arguments("privacy-owner", false)]
    [Arguments("privacy-owner", true)]
    [Arguments("issuer", false)]
    [Arguments("issuer", true)]
    public async Task NativePreFindingHumanErasureSettlesPrivateGraphWithoutRenewingOpportunity(string actor, bool rollback)
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var response = fixture.Response();
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, response)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var original = fixture.Database.Context();
        var custody = await original.Set<ModerationPreFindingCustody>().IgnoreQueryFilters().SingleAsync();
        var human = actor switch
        {
            "recipient" => fixture.Database.CallerId,
            "privacy-owner" => custody.PrivacyOwnerHumanId,
            "issuer" => custody.ProposedByHumanId,
            _ => throw new ArgumentException("Unknown erasure actor.", nameof(actor))
        };
        var now = fixture.Database.Clock.Now.UtcDateTime;
        var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            human, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
        await using (var context = fixture.Database.Context())
        {
            context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            if (!rollback) await transaction.CommitAsync();
        }
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.Set<ModerationPreFindingNotice>().IgnoreQueryFilters().AnyAsync())
            .IsEqualTo(rollback);
        await Assert.That(await inspect.Set<ModerationPreFindingReceipt>().IgnoreQueryFilters()
            .AnyAsync(row => row.CaseId == fixture.Case.Id)).IsEqualTo(rollback);
        await Assert.That(await inspect.Set<ModerationPreFindingErasureTombstone>().IgnoreQueryFilters()
            .AnyAsync(row => row.CaseId == fixture.Case.Id)).IsEqualTo(!rollback);
        await Assert.That(await inspect.Set<ModerationPreFindingCustody>().IgnoreQueryFilters().AnyAsync())
            .IsEqualTo(rollback || actor == "recipient");
        await AssertPreFindingPointerGraphAsync(inspect, fixture, retained: rollback);
        await Assert.That(await inspect.ModerationCases.IgnoreQueryFilters().AnyAsync(row => row.Id == fixture.Review.RootId))
            .IsTrue();
        await Assert.That(await inspect.ModerationSanctions.IgnoreQueryFilters().AnyAsync()).IsFalse();
        if (!rollback)
        {
            var wrote = false;
            await Assert.That(await fixture.DiscloseAsync(fixture.Database.CallerId,
                (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 }),
                (_, _) => { wrote = true; return Task.CompletedTask; }))
                .IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That(wrote).IsFalse();
        }
    }

    [Test]
    public async Task NativePreFindingErasureCannotReissueOpportunityWithFreshNoticeIdentity()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var original = fixture.Database.Context();
        var custody = await original.Set<ModerationPreFindingCustody>().IgnoreQueryFilters().SingleAsync();
        var now = fixture.Database.Clock.Now.UtcDateTime;
        var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            fixture.Database.CallerId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
        await using (var context = fixture.Database.Context())
        {
            context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            await transaction.CommitAsync();
        }
        var replacement = new IssueModerationPreFindingNotice(Guid.CreateVersion7(), fixture.Case.Id, 1, custody.Id,
            new(fixture.Database.RoleId, 7, fixture.Database.CallerId),
            new(new("A renewed allegation."), new("An applicable rule."), new("A new response clock.")), fixture.Until);
        await Assert.That((await fixture.ExecuteAsync(custody.ProposedByHumanId, replacement)).Outcome)
            .IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.Set<ModerationPreFindingNotice>().IgnoreQueryFilters().AnyAsync()).IsFalse();
        await Assert.That(await inspect.Set<ModerationPreFindingErasureTombstone>().IgnoreQueryFilters().CountAsync())
            .IsEqualTo(1);
    }

    [Test]
    public async Task NativePreFindingErasureDoesNotDeleteAnotherHumansCustodyOrReceipt()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var now = fixture.Database.Clock.Now.UtcDateTime;
        var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            fixture.Assessor, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
        await using (var context = fixture.Database.Context())
        {
            context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            await transaction.CommitAsync();
        }
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.Set<ModerationPreFindingNotice>().IgnoreQueryFilters().AnyAsync()).IsTrue();
        await Assert.That(await inspect.Set<ModerationPreFindingCustody>().IgnoreQueryFilters().AnyAsync()).IsTrue();
        await Assert.That(await inspect.Set<ModerationPreFindingReceipt>().IgnoreQueryFilters().AnyAsync()).IsTrue();
        await Assert.That(await inspect.Set<ModerationPreFindingErasureTombstone>().IgnoreQueryFilters().AnyAsync()).IsFalse();
        await AssertPreFindingPointerGraphAsync(inspect, fixture, retained: true);
    }

    [Test]
    public async Task NativePreFindingExpiryErasesPayloadWithoutRecipientOrReviewerAuthority()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        fixture.Database.Clock.Now = fixture.Until;
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context, Guid.Empty);
        await Assert.That(await repository.PurgeExpiredPreFindingAsync(1, default)).IsEqualTo(1);
        await using var inspect = fixture.Database.Context();
        var notice = await inspect.Set<ModerationPreFindingNotice>().SingleAsync();
        await Assert.That(notice.ProtectedNotice.IsEmpty).IsTrue();
        await Assert.That(notice.ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That(notice.PurgedAt).IsEqualTo(fixture.Until);
        await Assert.That(notice.RetainUntil).IsEqualTo(fixture.Until);
        await AssertPreFindingPointerGraphAsync(inspect, fixture, retained: false);
    }

    [Test]
    public async Task NativePreFindingInternalCleanupCannotEraseUnexpiredRecipientCustody()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        await Assert.That(await fixture.Repository(context, Guid.Empty).PurgeExpiredPreFindingAsync(1, default))
            .IsEqualTo(0);
        await using var inspect = fixture.Database.Context();
        var notice = await inspect.Set<ModerationPreFindingNotice>().SingleAsync();
        await Assert.That(notice.ProtectedNotice.IsEmpty).IsFalse();
        await Assert.That(notice.PurgedAt).IsNull();
        await Assert.That(notice.RetainUntil).IsEqualTo(fixture.Until);
    }

    [Test]
    public async Task NativePreFindingSourcesAreHiddenWithoutSelectedTrustDomain()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = null;
        await Assert.That(await context.Set<ModerationPreFindingNotice>().AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationPreFindingCustody>().AnyAsync()).IsFalse();
        await Assert.That(await context.Set<ModerationPreFindingReceipt>().AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeFindingProvenanceRejectsCopiedCoordinatesWithoutParentCustody(bool attribution)
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var response = fixture.Response();
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, response)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context, fixture.Assessor);
        var claimed = new ForgedFindingProvenanceSession(fixture.Case.Id, fixture.Database.TenantId);
        await fixture.Privacy.Authority(context, fixture.Database.Clock).ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await Assert.That(await repository.DiscoverResponseHumansAsync(fixture.Case.Id, token)).IsNotNull();
            if (attribution)
            {
                var owner = new ModerationCaseTargetProofOwner(context, new NativeCaller(fixture.Assessor),
                    new MachinePrincipalAccessor(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }),
                    fixture.Database.Clock);
                await Assert.That(await owner.AcquireCurrentAttributionAsync(claimed, fixture.Case.Id, token)).IsNull();
                await Assert.That(await owner.RecheckAttributionAsync(token)).IsFalse();
            }
            else
            {
                await Assert.That(await repository.AcquireCurrentResponseAsync(
                    claimed, fixture.Case.Id, response.OperationId, token)).IsNull();
                await Assert.That(await repository.RecheckResponseAsync(token)).IsFalse();
            }
            return true;
        }, default);
    }

    private sealed record ForgedFindingProvenanceSession(Guid CaseId, Guid TenantId)
        : IModerationFindingProvenanceSession;

    [Test]
    public async Task NativePreFindingObserverCanQualifyResponseWithoutReceivingPrivateText()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var response = fixture.Response();
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, response)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context, fixture.Assessor);
        await fixture.Privacy.Authority(context, fixture.Database.Clock).ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            var humans = await repository.DiscoverResponseHumansAsync(fixture.Case.Id, token);
            await Assert.That(humans).IsNotNull();
            await Assert.That(await repository.AcquireCurrentResponseAsync(fixture.Case.Id, response.OperationId, token)).IsNotNull();
            await Assert.That(await repository.RecheckResponseAsync(token)).IsTrue();
            return true;
        }, default);
        context.ModerationTrustDomainId = null;
        context.ChangeTracker.Clear();
        var wrote = false;
        var outcome = await repository.DisclosePreFindingAsync(fixture.Database.TenantId, fixture.NoticeId, true,
            (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 }),
            (_, _) => { wrote = true; return Task.CompletedTask; }, default);
        await Assert.That(outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(wrote).IsFalse();
    }

    [Test]
    public async Task NativePreFindingProvenanceReacquiresDirtyFactsButRequiresCleanTerminalState()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var response = fixture.Response();
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, response)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context, fixture.Assessor);
        await fixture.Privacy.Authority(context, fixture.Database.Clock).ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            await Assert.That(await repository.DiscoverResponseHumansAsync(fixture.Case.Id, token)).IsNotNull();
            var retained = await repository.AcquireCurrentResponseAsync(fixture.Case.Id, response.OperationId, token);
            await Assert.That(retained).IsNotNull();
            var epoch = await context.ModerationQualificationEpochs.SingleAsync(token);
            epoch.Advance(epoch.Revision);
            await Assert.That(await repository.AcquireCurrentResponseAsync(
                fixture.Case.Id, response.OperationId, token)).IsEqualTo(retained);
            await Assert.That(await repository.RecheckResponseAsync(token)).IsFalse();
            await context.SaveChangesAsync(token);
            await Assert.That(await repository.RecheckResponseAsync(token)).IsTrue();
            var notice = await context.Set<ModerationPreFindingNotice>().AsNoTracking().SingleAsync(token);
            await Assert.That(notice.RetainUntil).IsEqualTo(fixture.Until);
            await Assert.That(notice.ResponseId).IsEqualTo(response.OperationId);
            return true;
        }, default);
    }

    [Test]
    public async Task NativePreFindingPostFlushExpiryRollsBackResponseAndReceipt()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var boundary = new PreFindingSavedBoundary(() => fixture.Database.Clock.Now = fixture.Until);
        await using var context = fixture.Database.Context(boundary);
        var response = fixture.Response();
        var result = await fixture.Repository(context, fixture.Database.CallerId)
            .ExecutePreFindingAsync(response, default);
        await Assert.That(boundary.Observed).IsTrue();
        await Assert.That(result.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var inspect = fixture.Database.Context();
        await Assert.That((await inspect.Set<ModerationPreFindingNotice>().SingleAsync()).ResponseId).IsNull();
        await Assert.That(await inspect.Set<ModerationPreFindingReceipt>().AnyAsync(row => row.Id == response.OperationId)).IsFalse();
    }

    [Test]
    public async Task NativePreFindingWithdrawalDeletesPayloadAndCannotTurnOldResponseIntoCurrentProof()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        var response = fixture.Response();
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, response)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        Guid custodyId;
        Guid privacyOwner;
        await using (var read = fixture.Database.Context())
        {
            var custody = await read.Set<ModerationPreFindingCustody>().SingleAsync();
            custodyId = custody.Id;
            privacyOwner = custody.PrivacyOwnerHumanId;
        }
        await Assert.That((await fixture.ExecuteAsync(privacyOwner, new WithdrawModerationPreFindingCustody(
            Guid.CreateVersion7(), fixture.Case.Id, 1, custodyId, 2))).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var inspect = fixture.Database.Context();
        var notice = await inspect.Set<ModerationPreFindingNotice>().SingleAsync();
        await Assert.That(notice.Withdrawn).IsTrue();
        await Assert.That(notice.ProtectedNotice.IsEmpty).IsTrue();
        await Assert.That(notice.ProtectedResponse.IsEmpty).IsTrue();
        await Assert.That(notice.HasResponseAt(fixture.Database.Clock.Now)).IsFalse();
    }

    [Test]
    public async Task NativePreFindingDoesNotDiscloseContentToIndependentlyImplicatedManager()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using (var context = fixture.Database.Context())
        {
            var attributed = await AttributionWriter(context, fixture.Database, fixture.Review.Caller)
                .AttributeAsync(fixture.Review.Request(ModerationResponsibilityBasis.EventSubmission,
                    ModerationSubjectKind.Human, fixture.Database.CallerId), default);
            await Assert.That(attributed).IsNotNull();
        }
        var wrote = false;
        var outcome = await fixture.DiscloseAsync(fixture.Database.CallerId,
            (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 }),
            (_, _) => { wrote = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(wrote).IsFalse();
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationSanctions.AnyAsync()).IsFalse();
    }

    private sealed class PreFindingSavedBoundary(Action advance) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context!.ChangeTracker.Entries<ModerationPreFindingReceipt>().Any())
            {
                Observed = true;
                advance();
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task AssertPreFindingPointerGraphAsync(
        ExploreDbContext context, PreFindingFixture fixture, bool retained)
    {
        var key = ModerationPreFindingNoticeTemplate.DeduplicationKey(fixture.NoticeId, fixture.Database.CallerId);
        await Assert.That(await context.NotificationIntents.IgnoreQueryFilters()
            .AnyAsync(row => row.TenantId == fixture.Database.TenantId && row.DeduplicationKey == key))
            .IsEqualTo(retained);
        await Assert.That(await context.Notifications.IgnoreQueryFilters()
            .AnyAsync(row => row.TenantId == fixture.Database.TenantId && row.DeduplicationKey == key + ":in-app"))
            .IsEqualTo(retained);
        await Assert.That(await context.NotificationDeliveries.IgnoreQueryFilters()
            .AnyAsync(row => row.TenantId == fixture.Database.TenantId
                && row.NotificationIntent!.DeduplicationKey == key)).IsEqualTo(retained);
    }

    private sealed class PreFindingFixture(
        NativeDatabase database, AttributionReview review, ModerationCase owner,
        ModerationNoticePrivacyFixture privacy, Guid privacyOwner, Guid assessor) : IAsyncDisposable
    {
        public NativeDatabase Database => database;
        public AttributionReview Review => review;
        public ModerationCase Case => owner;
        public ModerationNoticePrivacyFixture Privacy => privacy;
        public Guid Assessor => assessor;
        public Guid NoticeId { get; private set; }
        public DateTimeOffset Until { get; private set; }
        public IssueModerationPreFindingNotice Issue { get; private set; } = null!;
        private static readonly ModerationCapabilityRegistry Inventory =
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());

        public RespondToModerationPreFindingNotice Response() =>
            new(Guid.CreateVersion7(), owner.Id, 1, NoticeId, 1, "The protected Content explanation is disputed.");

        public ModerationQualificationRepository Repository(ExploreDbContext context, Guid user,
            bool includeNativeNotices = true, NotificationRoutingOptions? routing = null,
            ModerationNoticePrivacyFixture? privacyAuthority = null)
        {
            context.ModerationTrustDomainId = null;
            var caller = new NativeCaller(user);
            context.CurrentUserService = caller;
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.ToString())], "pre-finding-native"));
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var machine = new MachinePrincipalAccessor(http);
            var clock = database.Clock;
            var unitOfWork = new EfCoreUnitOfWork(context);
            var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
            var materializer = new RecipientNotificationMaterializer(
                new NotificationIntentRepository(context, mutationLock, clock), unitOfWork, mutationLock,
                new PrivacyErasureStateRepository(context));
            return new(context, admin, machine, null!,
                new ModerationNativeActivationQualification(context, Inventory, null,
                    new ModerationQualificationPolicySourceOwner(context, clock), clock), clock,
                (privacyAuthority ?? privacy).Authority(context, clock), responseProtection: database.NarrativeProtection,
                caseAttribution: new ModerationCaseTargetProofOwner(context, caller, machine, clock),
                nativeNotices: includeNativeNotices ? new ModerationNoticeNativeMaterializer(
                    new DefaultNotificationOwnershipResolver(Options.Create(routing ?? new NotificationRoutingOptions())),
                    new NotificationPreferenceResolver(context), materializer) : null);
        }

        public async Task<ModerationPreFindingWriteResult> ExecuteAsync(Guid human, ModerationPreFindingOperation operation)
        {
            await using var context = database.Context();
            return await Repository(context, human).ExecutePreFindingAsync(operation, default);
        }

        public async Task<ModerationQualificationWriteOutcome> DiscloseAsync(Guid human,
            Func<ModerationPreFindingView, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task> write)
        {
            await using var context = database.Context();
            return await Repository(context, human).DisclosePreFindingAsync(database.TenantId, NoticeId, false, prepare, write, default);
        }

        private async Task QualifyAsync(Guid actor, ModerationQualificationMutation mutation)
        {
            await using var context = database.Context();
            var epoch = await context.ModerationQualificationEpochs.SingleAsync();
            context.ChangeTracker.Clear();
            var result = await Repository(context, actor).ExecuteAsync(
                mutation with { ExpectedQualificationRevision = epoch.Revision }, default);
            await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }

        public static async Task<PreFindingFixture> CreateAsync(
            ModerationCaseTargetKind kind = ModerationCaseTargetKind.EventVersion, bool issueNotice = true)
        {
            var database = await NativeDatabase.CreateAsync(kind);
            var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
            var review = await PrepareAttributionReviewAsync(database);
            ModerationCase owner;
            await using (var attribution = database.Context())
                owner = await AttributionWriter(attribution, database, review.Caller).AttributeAsync(
                    review.Request(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content, database.EventId), default)
                    ?? throw new InvalidOperationException("Native Content attribution failed.");
            var privacyOwner = Guid.CreateVersion7();
            var assessor = Guid.CreateVersion7();
            var fixture = new PreFindingFixture(database, review, owner, privacy, privacyOwner, assessor);
            await using (var seed = database.Context())
            {
                foreach (var id in new[] { privacyOwner, assessor })
                {
                    seed.Users.Add(new User { Id = id, Pii = new UserPii
                    {
                        UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Independent", LastName = "Owner"
                    }});
                    seed.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(), UserId = id, RoleId = (int)RoleEnum.Admin,
                        User = null!, Role = null!, GrantedAt = database.Clock.Now.UtcDateTime
                    });
                }
                foreach (var id in new[] { database.CallerId, review.Caller.UserId!.Value, privacyOwner, assessor })
                    if (!await seed.TenantUsers.AnyAsync(row => row.TenantId == database.TenantId && row.UserId == id))
                        seed.TenantUsers.Add(new TenantUser
                        {
                            Id = Guid.CreateVersion7(), TenantId = database.TenantId, UserId = id,
                            StatusId = (int)TenantUserStatusEnum.Active, Tenant = null!, User = null!
                        });
                await seed.SaveChangesAsync();
            }
            var policyId = Guid.CreateVersion7();
            var until = database.Clock.Now.AddSeconds(50);
            await fixture.QualifyAsync(assessor, new PublishModerationQualificationPolicySource
            {
                OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                SourceId = policyId, CaseId = owner.Id, ExpectedCaseRevision = 1, ExpectedSourceRevision = 0,
                MaximumReviewerLifetime = TimeSpan.FromMinutes(1), MaximumUrgencyLifetime = TimeSpan.FromMinutes(1), ValidUntil = until
            });
            await using (var read = database.Context())
            {
                var policy = await read.ModerationQualificationPolicySources.SingleAsync(row => row.Id == policyId);
                await fixture.QualifyAsync(privacyOwner, new AdoptModerationQualificationPolicySource
                {
                    OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                    SourceId = policyId, CaseId = owner.Id, ExpectedCaseRevision = 1, ExpectedSourceRevision = 1,
                    ExpectedWriterGeneration = policy.WriterGeneration, AdoptionId = Guid.CreateVersion7(), ValidUntil = until
                });
            }
            foreach (var reviewer in new[] { review.Caller.UserId!.Value, privacyOwner })
            {
                var assignmentId = Guid.CreateVersion7();
                await fixture.QualifyAsync(assessor, new AssignModerationReviewerSource
                {
                    OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                    AssignmentId = assignmentId, CaseId = owner.Id, ExpectedCaseRevision = 1, ReviewerHumanId = reviewer
                });
                await fixture.QualifyAsync(assessor, new QualifyModerationReviewerController
                {
                    OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                    AssignmentId = assignmentId, ExpectedAssignmentRevision = 1, ControllerHumanId = reviewer,
                    EvidenceId = Guid.CreateVersion7(), Justification = "Independently verified canonical controller.",
                    QualifiedUntil = until
                });
                await fixture.QualifyAsync(assessor, new AssessModerationReviewerConflict
                {
                    OperationId = Guid.CreateVersion7(), Target = owner.Scope, ExpectedQualificationRevision = 1,
                    AssignmentId = assignmentId, ExpectedAssignmentRevision = 2,
                    State = ModerationReviewerConflictState.Independent, EvidenceId = Guid.CreateVersion7(),
                    Justification = "Independent of this attributed Content case and its representative."
                });
            }
            var proposal = new ProposeModerationPreFindingCustody(Guid.CreateVersion7(), owner.Id, 1, privacyOwner,
                new(TimeSpan.FromSeconds(30), 8192, "Protected pre-finding response",
                    "Explicit independent purpose-limited fixture terms, not a production retention default."), until);
            await Assert.That((await fixture.ExecuteAsync(review.Caller.UserId!.Value, proposal)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await fixture.ExecuteAsync(privacyOwner, new AcceptModerationPreFindingCustody(
                Guid.CreateVersion7(), owner.Id, 1, proposal.OperationId, 1))).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            fixture.NoticeId = Guid.CreateVersion7();
            fixture.Until = database.Clock.Now.AddSeconds(20);
            fixture.Issue = new IssueModerationPreFindingNotice(
                fixture.NoticeId, owner.Id, 1, proposal.OperationId, new(database.RoleId, 7, database.CallerId),
                new(new("A specific Content concern, not a finding."), new("The applicable rule is disputed."),
                    new("Submit a protected response without contacting a reporter.")), fixture.Until);
            if (issueNotice)
                await Assert.That((await fixture.ExecuteAsync(review.Caller.UserId!.Value, fixture.Issue)).Outcome)
                    .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await privacy.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}
