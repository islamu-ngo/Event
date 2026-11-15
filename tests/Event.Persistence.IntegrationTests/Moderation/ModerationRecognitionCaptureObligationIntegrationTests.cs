using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments("none")]
    [Arguments("unretained")]
    [Arguments("retention")]
    [Arguments("timestamp")]
    [Arguments("deadline")]
    [Arguments("deadline-during-acquire")]
    [Arguments("transaction")]
    public async Task Native_capture_binding_requires_exact_retained_fact_original_transaction_and_deadline(string boundary)
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        var acquisition = new CaptureBindingClockBoundary(fixture.Clock);
        await using var context = fixture.Database.MaintenanceContext(acquisition);
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var original = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        async Task BindAsync() => await fixture.Retained.Repository.ExecuteSerializedAsync(async token =>
        {
            await using var lease = await repository.AcquireAppendAsync(fixture.Request, token);
            await Assert.That(lease.CanAppend).IsTrue();
            var fact = boundary == "unretained" ? fixture.Fact()
                : await fixture.Retained.Repository.AppendAsync(fixture.Request, token);
            if (boundary != "unretained")
            {
                await Assert.That((await fixture.Retained.Repository.GetStateAsync(token)).HighWaterSequence).IsEqualTo(1L);
                await Assert.That(await context.PrivacyErasureReplayCheckpoints.AnyAsync(token)).IsFalse();
            }
            if (boundary is "retention" or "timestamp")
                fact = PrivacyErasureIntent.Record(fact.IntentId, fact.AuthoritySequence, fact.SubjectKind,
                    fact.SubjectId, fact.ReasonCode, fact.PolicyVersion, fact.RequestedAtUtc,
                    boundary == "timestamp" ? fact.RecordedAtUtc.AddTicks(1) : fact.RecordedAtUtc,
                    boundary == "retention" ? fact.RetentionExpiresAtUtc.AddTicks(1) : fact.RetentionExpiresAtUtc);
            if (boundary == "deadline") fixture.Clock.UtcNow = original.SettleBy;
            if (boundary == "deadline-during-acquire") acquisition.AdvanceTo = original.SettleBy;
            if (boundary == "transaction")
            {
                await context.Database.CurrentTransaction!.DisposeAsync();
                await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await lease.BindAsync(fact, token);
            }
            else await lease.BindAsync(fact, token);
            await lease.CommitAsync(token);
            return true;
        }, CancellationToken.None);

        if (boundary == "none") await BindAsync();
        else
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(BindAsync);
            await Assert.That(failure?.Message).IsEqualTo(boundary switch
            {
                "deadline" or "deadline-during-acquire" => "moderation_recognition_erasure_unsettled",
                "transaction" => "Recognition append lost its original native request and transaction.",
                _ => "moderation_recognition_disposition_authority_unavailable"
            });
            if (boundary == "deadline-during-acquire")
                await Assert.That(acquisition.AdvanceTo).IsNull();
        }
        await using var verification = fixture.Database.MaintenanceContext();
        var current = (await fixture.Repository(verification).FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(current.MaterialReceipt).IsEqualTo(original.MaterialReceipt);
        await Assert.That(current.Material.Single().Id).IsEqualTo(original.Material.Single().Id);
        await Assert.That(current.Material.Single().RetainUntil).IsEqualTo(original.Material.Single().RetainUntil);
        await Assert.That(current.AuthoritySequence).IsEqualTo(boundary == "none" ? (long?)1 : null);
        await Assert.That((await fixture.Retained.Repository.GetStateAsync()).HighWaterSequence)
            .IsEqualTo(boundary == "none" ? 1L : 0L);
    }

    private sealed class CaptureBindingClockBoundary(DeploymentClock clock) : DbCommandInterceptor
    {
        public DateTimeOffset? AdvanceTo { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (AdvanceTo is { } deadline)
            {
                clock.UtcNow = deadline;
                AdvanceTo = null;
            }
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public async Task Expired_linked_receipts_are_forgotten_without_authority_while_unlinkable_key_horizon_survives()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var fixture = scenario.Fixture;
        var fact = (await fixture.Retained.Repository.ReadAfterAsync(0, 1, CancellationToken.None)).Single();
        await using var context = fixture.Database.MaintenanceContext();
        fixture.Clock.UtcNow = Now.AddDays(2);
        var purge = (await fixture.KeyLifecycle(context).PruneAsync(null, 100, CancellationToken.None)).Purged.Single();
        var key = (await fixture.KeyLifecycle(context).FindAsync(purge.TrustDomainId, CancellationToken.None))!.Keys.Single();
        var originalHorizon = key.OriginalObligationsUntil;
        var originalHead = key.RequiredErasureHead;
        fixture.Clock.UtcNow = purge.RetainUntil;
        var unavailable = Substitute.For<IPrivacyErasureAuthority>();
        unavailable.GetStateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<PrivacyErasureAuthorityState>(new IOException("authority unavailable")));
        var primaryOnly = fixture.LifecycleFixture.Bind(context, fixture.Retained.Repository, unavailable).Repository;
        var removed = await primaryOnly.ForgetExpiredReceiptsAsync(null, 100, CancellationToken.None);
        await Assert.That(removed.ReceiptsRemoved).IsEqualTo(1);
        await Assert.That(removed.ObligationsRemoved).IsEqualTo(purge.MaterialCount);
        await Assert.That(await primaryOnly.FindPurgeAsync(fact.IntentId, CancellationToken.None)).IsNull();
        await Assert.That(await context.Set<KeyObligation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AnyAsync(row => row.IntentId == fact.IntentId)).IsFalse();
        var retained = (await primaryOnly.FindAsync(purge.TrustDomainId, CancellationToken.None))!.Keys.Single();
        await Assert.That(retained.OriginalObligationsUntil).IsEqualTo(originalHorizon);
        await Assert.That(retained.RequiredErasureHead).IsEqualTo(originalHead);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Keyless_erasure_replay_after_token_cleanup_requires_original_native_obligations(bool missingObligation)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var fixture = scenario.Fixture;
        var fact = (await fixture.Retained.Repository.ReadAfterAsync(0, 1, CancellationToken.None)).Single();
        await using var context = fixture.Database.MaintenanceContext();
        fixture.Clock.UtcNow = Now.AddDays(2);
        var removed = await fixture.KeyLifecycle(context).PruneAsync(null, 100, CancellationToken.None);
        await Assert.That(removed.Purged.Length).IsEqualTo(1);
        await Assert.That(await scenario.Repository(context).FindAsync(fact.IntentId, CancellationToken.None)).IsNull();
        if (missingObligation)
            await context.Set<KeyObligation>().IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
                .Where(row => row.IntentId == fact.IntentId).ExecuteDeleteAsync();
        scenario.Keys.Available = false;
        var repository = scenario.Repository(context);
        var service = new ModerationRecognitionErasure(repository, fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        async Task<bool> VerifyAsync() => await service.ExecutePurgeAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await repository.VerifyPurgeAsync(fact, token);
            return true;
        }, CancellationToken.None);
        if (missingObligation)
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await VerifyAsync());
        else
        {
            await Assert.That(await VerifyAsync()).IsTrue();
            var replay = await service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddDays(3), CancellationToken.None);
            await Assert.That(replay!.AuthoritySequence).IsEqualTo(fact.AuthoritySequence);
            await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.UserId == fixture.Subject)).IsFalse();
            await Assert.That(await context.Set<UserPii>().AnyAsync(row => row.UserId == fixture.Subject)).IsFalse();
        }
    }

    [Test]
    public async Task Native_capture_and_authority_binding_commit_exact_original_key_obligations_with_their_receipt()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using var context = fixture.Database.MaintenanceContext();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var computed = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        var item = computed.Material.Single();
        var obligation = await context.Set<KeyObligation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        var source = await context.Set<ModerationRecognitionPrivacySource>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        await Assert.That(obligation.Matches(item)).IsTrue();
        await Assert.That(obligation.AuthoritySequence).IsNull();
        await Assert.That(obligation.ReadableUntil).IsEqualTo(
            item.RetainUntil.AddTicks(source.MaximumBackupHorizonTicks + source.BackupSafetyMarginTicks));
        context.ChangeTracker.Clear();
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var repeated = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(repeated.MaterialReceipt).IsEqualTo(computed.MaterialReceipt);
        await Assert.That(repeated.Material.Single().Id).IsEqualTo(item.Id);
        await Assert.That((await context.Set<KeyObligation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync()).Id).IsEqualTo(obligation.Id);
        context.ChangeTracker.Clear();
        var service = new ModerationRecognitionErasure(repository, fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var fact = await service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await Assert.That(fact).IsNotNull();
        var settled = await context.Set<KeyObligation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        await Assert.That(settled.Id).IsEqualTo(obligation.Id);
        await Assert.That(settled.AuthoritySequence).IsEqualTo(fact!.AuthoritySequence);
        await Assert.That(settled.OriginalCapturedAt).IsEqualTo(item.OriginalCapturedAt);
        await Assert.That(settled.OriginalRetainUntil).IsEqualTo(item.RetainUntil);
        await Assert.That(settled.CaptureMaterialReceipt).IsEqualTo(item.ComputeMaterialReceipt());
    }
}
