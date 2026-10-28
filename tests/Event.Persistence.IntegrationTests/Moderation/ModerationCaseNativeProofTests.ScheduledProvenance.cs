using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task InstalledMaintenanceBorrowsNativeHumanResponseWithoutHumanCallerOrDisclosure()
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var responses = fixture.Responses(context);
        var attribution = fixture.Attribution(context);
        var recorder = new RecordingScheduledResponseOwner(responses, async (session, reference, token) =>
        {
            await Assert.That(context.CurrentUserService!.UserId).IsNull();
            await Assert.That(reference).IsEqualTo(ModerationFindingEvidenceReference.Response(
                fixture.ResponseId, fixture.Case.Id, 1, fixture.Case.CreatedAt));
            await Assert.That(await attribution.AcquireCurrentAttributionAsync(session, fixture.Case.Id, token))
                .IsNotNull();
            await Assert.That(await attribution.RecheckAttributionAsync(token)).IsTrue();
            await Assert.That(await responses.RecheckResponseAsync(token)).IsTrue();
            // The old human-native observation overload must not turn a service into a reviewer.
            var unbound = fixture.Responses(context);
            await Assert.That(await unbound.DiscoverResponseHumansAsync(fixture.Case.Id, token)).IsNotNull();
            await Assert.That(await unbound.AcquireCurrentResponseAsync(fixture.Case.Id, fixture.ResponseId, token))
                .IsNull();
        });
        await Assert.That((await fixture.ObserveAsync(context, recorder)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(recorder.Session).IsNotNull();
        await Assert.That(recorder.Humans!.Contains(fixture.Database.CallerId)).IsTrue();
        await Assert.That(recorder.Humans!.Contains(fixture.Original.Review.Caller.UserId!.Value)).IsTrue();
        await Assert.That(recorder.Humans!.Contains(fixture.PrivacyOwner)).IsTrue();
        await Assert.That(recorder.Humans!.Contains(fixture.KeyOwnerId)).IsFalse();
        var wrote = false;
        await Assert.That(await responses.DisclosePreFindingAsync(fixture.Database.TenantId, fixture.NoticeId,
            false, (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 }),
            (_, _) => { wrote = true; return Task.CompletedTask; }, default))
            .IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(wrote).IsFalse();
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationDecisions.SingleAsync(row => row.Id == fixture.Decision.Id)).State)
            .IsEqualTo(ModerationDecisionState.Scheduled);
    }

    [Test]
    [Arguments("service")]
    [Arguments("credential")]
    [Arguments("purpose")]
    public async Task ScheduledResponseBorrowingRejectsWrongInstalledServiceCredentialOrPurpose(string defect)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using (var mutation = fixture.Database.Context())
        {
            if (defect == "service")
                await mutation.ModerationMachineSubjectBindings
                    .Where(row => row.KeyId == fixture.Principal.KeyId).ExecuteUpdateAsync(setters =>
                        setters.SetProperty(row => row.ServicePrincipalId, fixture.Database.ServiceId));
            else if (defect == "credential")
                await mutation.ExternalApiKeys.Where(row => row.KeyId == fixture.Principal.KeyId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked));
        }
        var operation = defect == "purpose"
            ? ModerationOperation.Transition(ModerationOperationKind.Approve, fixture.Case.Subject!, fixture.Case.Scope,
                Guid.CreateVersion7(), Guid.CreateVersion7(), fixture.Decision.Id, fixture.Decision.Revision, 1, Guid.Empty)
            : fixture.Reconcile();
        await using var context = fixture.Database.Context();
        var recorder = new RecordingScheduledResponseOwner(fixture.Responses(context));
        await Assert.That((await fixture.ObserveAsync(context, recorder, operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(recorder.Session).IsNull();
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationSanctions.SingleAsync(row => row.Id == fixture.Sanction.Id)).State)
            .IsEqualTo(ModerationSanctionState.Scheduled);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualParentSessionCannotBeCopiedOrBorrowedByForeignContentCaseOrContext(bool attribution)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var responses = fixture.Responses(context);
        var recorder = new RecordingScheduledResponseOwner(responses, async (session, reference, token) =>
        {
            await Assert.That(reference).IsNotNull();
            var copied = new ForgedFindingProvenanceSession(fixture.Case.Id, fixture.Database.TenantId);
            if (attribution)
            {
                var owner = fixture.Attribution(context);
                await Assert.That(await owner.AcquireCurrentAttributionAsync(copied, fixture.Case.Id, token)).IsNull();
                await Assert.That(await owner.AcquireCurrentAttributionAsync(
                    session, fixture.Original.Case.Id, token)).IsNull();
            }
            else
            {
                await Assert.That(await responses.AcquireCurrentResponseAsync(
                    copied, fixture.Case.Id, fixture.ResponseId, token)).IsNull();
                await Assert.That(await responses.AcquireCurrentResponseAsync(
                    session, fixture.Original.Case.Id, fixture.ResponseId, token)).IsNull();
            }
            await using var foreign = fixture.Database.Context();
            foreign.Database.SetDbConnection(context.Database.GetDbConnection());
            await using var transaction = await foreign.Database.UseTransactionAsync(
                context.Database.CurrentTransaction!.GetDbTransaction(), token);
            if (attribution)
                await Assert.That(await fixture.Attribution(foreign)
                    .AcquireCurrentAttributionAsync(session, fixture.Case.Id, token)).IsNull();
            else
            {
                var foreignResponses = fixture.Responses(foreign);
                await Assert.That(await foreignResponses.DiscoverResponseHumansAsync(fixture.Case.Id, token)).IsNotNull();
                await Assert.That(await foreignResponses.AcquireCurrentResponseAsync(
                    session, fixture.Case.Id, fixture.ResponseId, token)).IsNull();
                await Assert.That(await foreignResponses.RecheckResponseAsync(token)).IsFalse();
            }
        });
        await Assert.That((await fixture.ObserveAsync(context, recorder)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(recorder.Session).IsNotNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedParentAcquisitionInvalidatesCapturedSessionOutsideOrInReplacementTransaction(bool replaceTransaction)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var responses = fixture.Responses(context);
        var recorder = new RecordingScheduledResponseOwner(responses);
        await Assert.That((await fixture.ObserveAsync(context, recorder)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(recorder.Reference).IsNotNull();
        await Assert.That(recorder.Session).IsNotNull();
        // The real store has completed its failed transaction. No metadata copy revives custody.
        if (replaceTransaction)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await responses.DiscoverResponseHumansAsync(fixture.Case.Id, default)).IsNotNull();
            await Assert.That(await responses.AcquireCurrentResponseAsync(
                recorder.Session!, fixture.Case.Id, fixture.ResponseId, default)).IsNull();
            await Assert.That(await fixture.Attribution(context).AcquireCurrentAttributionAsync(
                recorder.Session!, fixture.Case.Id, default)).IsNull();
        }
        else
            await Assert.That(await responses.AcquireCurrentResponseAsync(
                recorder.Session!, fixture.Case.Id, fixture.ResponseId, default)).IsNull();
        await Assert.That(await responses.RecheckResponseAsync(default)).IsFalse();
    }

    [Test]
    public async Task NativeScheduledBorrowingReacquiresDirtyObservationButRefusesDirtyTerminalRecheck()
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var responses = fixture.Responses(context);
        var recorder = new RecordingScheduledResponseOwner(responses, async (session, reference, token) =>
        {
            await Assert.That(reference).IsNotNull();
            await Assert.That(await responses.RecheckResponseAsync(token)).IsTrue();
            var epoch = await context.ModerationQualificationEpochs.SingleAsync(token);
            epoch.Advance(epoch.Revision);
            await Assert.That(await responses.AcquireCurrentResponseAsync(
                session, fixture.Case.Id, fixture.ResponseId, token)).IsEqualTo(reference);
            await Assert.That(await responses.RecheckResponseAsync(token)).IsFalse();
        });
        await Assert.That((await fixture.ObserveAsync(context, recorder)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(recorder.Session).IsNotNull();
        await using var inspect = fixture.Database.Context();
        await Assert.That((await inspect.Set<ModerationPreFindingNotice>()
            .SingleAsync(row => row.Id == fixture.NoticeId)).RetainUntil).IsEqualTo(fixture.Until);
        await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("recipient")]
    [Arguments("issuer")]
    [Arguments("privacy-owner")]
    [Arguments("attribution")]
    [Arguments("withdrawal")]
    [Arguments("expiry")]
    public async Task OriginalHumanCustodyRemainsRequiredAfterServiceResponseObservation(string change)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using (var baseline = fixture.Database.Context())
        {
            var recorder = new RecordingScheduledResponseOwner(fixture.Responses(baseline));
            await Assert.That((await fixture.ObserveAsync(baseline, recorder)).Outcome)
                .IsEqualTo(ModerationOperationOutcome.Unavailable);
            await Assert.That(recorder.Reference).IsNotNull();
        }
        if (change == "expiry") fixture.Database.Clock.Now = fixture.Until;
        else if (change == "attribution")
        {
            await using var mutation = fixture.Database.Context();
            await Assert.That(await AttributionWriter(mutation, fixture.Database, fixture.Original.Review.Caller)
                .WithdrawAsync(fixture.Case.AttributionSourceId!.Value, Guid.CreateVersion7(), 1, default)).IsNotNull();
        }
        else if (change == "withdrawal")
            await Assert.That((await fixture.Original.ExecuteAsync(fixture.PrivacyOwner,
                new WithdrawModerationPreFindingCustody(Guid.CreateVersion7(), fixture.Case.Id, 1,
                    fixture.CustodyId, 2))).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        else
        {
            await using var mutation = fixture.Database.Context();
            var human = change == "recipient" ? fixture.Database.CallerId
                : change == "issuer" ? fixture.Original.Review.Caller.UserId!.Value : fixture.PrivacyOwner;
            if (change == "recipient")
                await mutation.Users.Where(row => row.Id == human)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            else
                await mutation.PlatformUserRoles.Where(row => row.UserId == human).ExecuteDeleteAsync();
        }
        await using var context = fixture.Database.Context();
        var after = new RecordingScheduledResponseOwner(fixture.Responses(context));
        await Assert.That((await fixture.ObserveAsync(context, after)).Outcome)
            .IsNotEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(after.Reference).IsNull();
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationSanctions.SingleAsync(row => row.Id == fixture.Sanction.Id)).State)
            .IsEqualTo(ModerationSanctionState.Scheduled);
    }

    [Test]
    [Arguments("recipient")]
    [Arguments("issuer")]
    [Arguments("privacy-owner")]
    public async Task RetainedHumanErasureClosesServiceObservationBeforeApplicationSaga(string actor)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using (var baseline = fixture.Database.Context())
        {
            var recorder = new RecordingScheduledResponseOwner(fixture.Responses(baseline));
            await fixture.ObserveAsync(baseline, recorder);
            await Assert.That(recorder.Reference).IsNotNull();
        }
        var human = actor == "recipient" ? fixture.Database.CallerId
            : actor == "issuer" ? fixture.Original.Review.Caller.UserId!.Value : fixture.PrivacyOwner;
        await using (var append = fixture.Database.Context())
            await fixture.Original.Privacy.Authority(append, fixture.Database.Clock).AppendAsync(new(
                Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, human,
                PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        await using var context = fixture.Database.Context();
        var after = new RecordingScheduledResponseOwner(fixture.Responses(context));
        await Assert.That((await fixture.ObserveAsync(context, after)).Outcome)
            .IsNotEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(after.Reference).IsNull();
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.PrivacyErasureSagas.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ResponseExpiryDuringActualNativePermissionQualificationClosesAlreadyBorrowedEvidence()
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var responses = fixture.Responses(context);
        var recorder = new RecordingScheduledResponseOwner(responses, async (session, reference, token) =>
        {
            await Assert.That(reference).IsNotNull();
            fixture.Database.Clock.Now = fixture.Until;
            await Assert.That(await responses.AcquireCurrentResponseAsync(
                session, fixture.Case.Id, fixture.ResponseId, token)).IsNull();
            await Assert.That(await responses.RecheckResponseAsync(token)).IsFalse();
        });
        await Assert.That((await fixture.ObserveAsync(context, recorder)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(recorder.Reference).IsNotNull();
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationDecisions.SingleAsync(row => row.Id == fixture.Decision.Id)).State)
            .IsEqualTo(ModerationDecisionState.Scheduled);
    }
}
