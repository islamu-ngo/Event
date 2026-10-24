using System.Data;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Expired_linked_receipts_are_forgotten_without_authority_while_unlinkable_key_horizon_survives()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var fixture = scenario.Fixture;
        var fact = (await fixture.Retained.Repository.ReadAfterAsync(0, 1, CancellationToken.None)).Single();
        await using var context = fixture.Database.Context();
        context.PrivacyErasureReplayCheckpoints.Add(PrivacyErasureReplayCheckpoint.Start(fact, Now.UtcDateTime));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
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
        await using var context = fixture.Database.Context();
        context.PrivacyErasureReplayCheckpoints.Add(PrivacyErasureReplayCheckpoint.Start(fact, Now.UtcDateTime));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
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
        await using var context = fixture.Database.Context();
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
