// ABOUTME: PostgreSQL proofs for fenced privacy-erasure provider-work reconciliation.
// ABOUTME: Verifies durable idempotency, atomic saga progress, and unrelated-saga preservation.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class PrivacyErasureProviderWorkRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ReconcileUnknown_PersistsFencedIdempotentSettlementsAndAdvancesOnlyCompletedSagaProgress()
    {
        await fixture.ResetAsync();
        DateTime now = new(2026, 7, 24, 8, 0, 0, DateTimeKind.Utc);
        PrivacyErasureIntent primaryIntent = CreateIntent(1, now);
        PrivacyErasureIntent unrelatedIntent = CreateIntent(2, now);
        PrivacyErasureSaga primarySaga = CreateSettledSaga(primaryIntent, 2, now);
        PrivacyErasureSaga unrelatedSaga = CreateSettledSaga(unrelatedIntent, 1, now);
        PrivacyErasureProviderWork completedWork = CreateWork(primaryIntent, now);
        PrivacyErasureProviderWork notCompletedWork = CreateWork(primaryIntent, now);
        PrivacyErasureProviderWork unrelatedWork = CreateWork(unrelatedIntent, now);

        await using var context = fixture.CreateDbContext();
        context.AddRange(
            primarySaga,
            unrelatedSaga,
            completedWork,
            notCompletedWork,
            unrelatedWork);
        await context.SaveChangesAsync();

        PrivacyErasureProviderClaim completedClaim = completedWork.Claim(
            "reconciliation-test",
            Guid.CreateVersion7(),
            now.AddMinutes(2),
            now.AddMinutes(3));
        PrivacyErasureProviderClaim notCompletedClaim = notCompletedWork.Claim(
            "reconciliation-test",
            Guid.CreateVersion7(),
            now.AddMinutes(2),
            now.AddMinutes(3));
        await context.SaveChangesAsync();

        var repository = new PrivacyErasureProviderWorkRepository(context);
        await repository.TryMarkUnknownAsync(
            completedWork.Id,
            completedClaim.FenceToken,
            completedClaim.LeaseToken,
            now.AddMinutes(2),
            "ambiguous_acknowledgement",
            CancellationToken.None);
        await repository.TryMarkUnknownAsync(
            notCompletedWork.Id,
            notCompletedClaim.FenceToken,
            notCompletedClaim.LeaseToken,
            now.AddMinutes(2),
            "ambiguous_acknowledgement",
            CancellationToken.None);

        await Assert.That(await repository.TryReconcileUnknownAsync(
                completedWork.Id,
                completedClaim.FenceToken - 1,
                PrivacyErasureProviderReconciliation.Completed,
                now.AddMinutes(4),
                CancellationToken.None))
            .IsFalse();
        await Assert.That(await repository.TryReconcileUnknownAsync(
                completedWork.Id,
                completedClaim.FenceToken,
                PrivacyErasureProviderReconciliation.Completed,
                now.AddMinutes(4),
                CancellationToken.None))
            .IsTrue();
        await Assert.That(await repository.TryReconcileUnknownAsync(
                completedWork.Id,
                completedClaim.FenceToken,
                PrivacyErasureProviderReconciliation.Completed,
                now.AddMinutes(4),
                CancellationToken.None))
            .IsFalse();
        await Assert.That(await repository.TryReconcileUnknownAsync(
                notCompletedWork.Id,
                notCompletedClaim.FenceToken,
                PrivacyErasureProviderReconciliation.NotCompleted,
                now.AddMinutes(5),
                CancellationToken.None))
            .IsTrue();

        context.ChangeTracker.Clear();
        PrivacyErasureSaga persistedPrimarySaga = await context.PrivacyErasureSagas
            .SingleAsync(item => item.IntentId == primaryIntent.IntentId);
        PrivacyErasureSaga persistedUnrelatedSaga = await context.PrivacyErasureSagas
            .SingleAsync(item => item.IntentId == unrelatedIntent.IntentId);
        PrivacyErasureProviderWork persistedCompletedWork = await context.PrivacyErasureProviderWork
            .SingleAsync(item => item.Id == completedWork.Id);
        PrivacyErasureProviderWork persistedNotCompletedWork = await context.PrivacyErasureProviderWork
            .SingleAsync(item => item.Id == notCompletedWork.Id);
        PrivacyErasureProviderWork persistedUnrelatedWork = await context.PrivacyErasureProviderWork
            .SingleAsync(item => item.Id == unrelatedWork.Id);

        await Assert.That(persistedCompletedWork.Status)
            .IsEqualTo(PrivacyErasureProviderWorkStatus.Completed);
        await Assert.That(persistedNotCompletedWork.Status)
            .IsEqualTo(PrivacyErasureProviderWorkStatus.RetryScheduled);
        await Assert.That(persistedPrimarySaga.CompletedProviderWorkCount).IsEqualTo(1);
        await Assert.That(persistedPrimarySaga.Status).IsEqualTo(PrivacyErasureSagaStatus.ProviderPending);
        await Assert.That(persistedUnrelatedSaga.CompletedProviderWorkCount).IsEqualTo(0);
        await Assert.That(persistedUnrelatedWork.Status).IsEqualTo(PrivacyErasureProviderWorkStatus.Pending);
    }

    private static PrivacyErasureIntent CreateIntent(long authoritySequence, DateTime now) =>
        PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            authoritySequence,
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            now,
            now,
            now.AddDays(30));

    private static PrivacyErasureSaga CreateSettledSaga(
        PrivacyErasureIntent intent,
        int providerWorkCount,
        DateTime now)
    {
        var receiptHash = new byte[32];
        receiptHash[0] = (byte)intent.AuthoritySequence;
        PrivacyErasureSaga saga = PrivacyErasureSaga.Start(
            intent,
            intent.AuthoritySequence,
            receiptHash,
            now.AddHours(1),
            now);
        saga.MarkLocalSettled(now.AddMinutes(1), providerWorkCount, saga.ConcurrencyToken);
        return saga;
    }

    private static PrivacyErasureProviderWork CreateWork(PrivacyErasureIntent intent, DateTime now) =>
        PrivacyErasureProviderWork.Create(
            Guid.CreateVersion7(),
            intent,
            PrivacyErasureProviderKind.Keycloak,
            PrivacyErasureProviderAction.DeletePlatformManagedIdentity,
            null,
            Guid.CreateVersion7(),
            PrivacyErasureProviderLocatorKind.AccountIdentifier,
            "protected-locator",
            1,
            now.AddDays(7),
            now);
}
