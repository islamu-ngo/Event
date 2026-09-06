// ABOUTME: PostgreSQL proofs for bounded privacy-erasure receipt and locator credential cleanup.
// ABOUTME: Verifies dry-run immutability, expired-only destruction, and no executable work claiming.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class PrivacyErasureCredentialCleanupRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CredentialCleanup_DryRunDoesNotMutateAndRealPassClearsOnlyExpiredCredentials()
    {
        await fixture.ResetAsync();
        DateTime utcNow = new(2026, 7, 25, 9, 0, 0, DateTimeKind.Utc);
        PrivacyErasureIntent expiredIntent = CreateIntent(1, utcNow.AddDays(-2));
        PrivacyErasureIntent activeIntent = CreateIntent(2, utcNow);
        PrivacyErasureSaga expiredSaga = CreateSaga(expiredIntent, 1, utcNow.AddHours(-1), utcNow.AddDays(-2));
        PrivacyErasureSaga activeSaga = CreateSaga(activeIntent, 2, utcNow.AddHours(1), utcNow);
        PrivacyErasureProviderWork expiredWork = CreateWork(
            expiredIntent,
            "expired-protected-locator",
            utcNow.AddDays(-2),
            utcNow.AddHours(-1));
        PrivacyErasureProviderWork activeWork = CreateWork(
            activeIntent,
            "active-protected-locator",
            utcNow.AddHours(1),
            utcNow.AddDays(2));

        await using var context = fixture.CreateDbContext();
        context.AddRange(expiredSaga, activeSaga, expiredWork, activeWork);
        await context.SaveChangesAsync();

        var stateRepository = new PrivacyErasureStateRepository(context);
        var providerWorkRepository = new PrivacyErasureProviderWorkRepository(context);

        await Assert.That(await stateRepository.ClearExpiredReceiptHashesAsync(
                utcNow,
                10,
                true,
                CancellationToken.None))
            .IsEqualTo(1);
        await Assert.That(await providerWorkRepository.ExpireLocatorsAsync(
                utcNow,
                10,
                true,
                CancellationToken.None))
            .IsEqualTo(1);
        await Assert.That(await providerWorkRepository.ClaimDueAsync(
                "credential-cleanup-test",
                10,
                utcNow,
                utcNow.AddMinutes(1),
                CancellationToken.None))
            .IsEmpty();

        context.ChangeTracker.Clear();
        await Assert.That((await context.PrivacyErasureSagas.SingleAsync(item => item.IntentId == expiredIntent.IntentId)).ReceiptHash)
            .IsNotNull();
        await Assert.That((await context.PrivacyErasureProviderWork.SingleAsync(item => item.Id == expiredWork.Id)).ProtectedLocator)
            .IsNotNull();

        await Assert.That(await stateRepository.ClearExpiredReceiptHashesAsync(
                utcNow,
                10,
                false,
                CancellationToken.None))
            .IsEqualTo(1);
        await Assert.That(await providerWorkRepository.ExpireLocatorsAsync(
                utcNow,
                10,
                false,
                CancellationToken.None))
            .IsEqualTo(1);

        context.ChangeTracker.Clear();
        PrivacyErasureSaga persistedExpiredSaga = await context.PrivacyErasureSagas
            .SingleAsync(item => item.IntentId == expiredIntent.IntentId);
        PrivacyErasureSaga persistedActiveSaga = await context.PrivacyErasureSagas
            .SingleAsync(item => item.IntentId == activeIntent.IntentId);
        PrivacyErasureProviderWork persistedExpiredWork = await context.PrivacyErasureProviderWork
            .SingleAsync(item => item.Id == expiredWork.Id);
        PrivacyErasureProviderWork persistedActiveWork = await context.PrivacyErasureProviderWork
            .SingleAsync(item => item.Id == activeWork.Id);

        await Assert.That(persistedExpiredSaga.ReceiptHash).IsNull();
        await Assert.That(persistedExpiredWork.ProtectedLocator).IsNull();
        await Assert.That(persistedExpiredWork.Status).IsEqualTo(PrivacyErasureProviderWorkStatus.DeadLettered);
        await Assert.That(persistedActiveSaga.ReceiptHash).IsNotNull();
        await Assert.That(persistedActiveWork.ProtectedLocator).IsNotNull();
        await Assert.That(persistedActiveWork.Status).IsEqualTo(PrivacyErasureProviderWorkStatus.Pending);

        await Assert.That(await stateRepository.ClearExpiredReceiptHashesAsync(
                utcNow,
                10,
                false,
                CancellationToken.None))
            .IsEqualTo(0);
        await Assert.That(await providerWorkRepository.ExpireLocatorsAsync(
                utcNow,
                10,
                false,
                CancellationToken.None))
            .IsEqualTo(0);
    }

    private static PrivacyErasureIntent CreateIntent(long sequence, DateTime recordedAtUtc) =>
        PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            sequence,
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            recordedAtUtc,
            recordedAtUtc,
            recordedAtUtc.AddDays(30));

    private static PrivacyErasureSaga CreateSaga(
        PrivacyErasureIntent intent,
        byte receiptMarker,
        DateTime receiptExpiresAtUtc,
        DateTime fencedAtUtc)
    {
        var receiptHash = new byte[32];
        receiptHash[0] = receiptMarker;
        return PrivacyErasureSaga.Start(
            intent,
            intent.AuthoritySequence,
            receiptHash,
            receiptExpiresAtUtc,
            fencedAtUtc);
    }

    private static PrivacyErasureProviderWork CreateWork(
        PrivacyErasureIntent intent,
        string protectedLocator,
        DateTime createdAtUtc,
        DateTime locatorExpiresAtUtc) =>
        PrivacyErasureProviderWork.Create(
            Guid.CreateVersion7(),
            intent,
            PrivacyErasureProviderKind.Keycloak,
            PrivacyErasureProviderAction.DeletePlatformManagedIdentity,
            null,
            Guid.CreateVersion7(),
            PrivacyErasureProviderLocatorKind.AccountIdentifier,
            protectedLocator,
            1,
            locatorExpiresAtUtc,
            createdAtUtc);
}
