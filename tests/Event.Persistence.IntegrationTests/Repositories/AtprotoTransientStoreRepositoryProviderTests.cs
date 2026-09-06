// ABOUTME: Runs the ATProto transient atomicity contract on SQL Server, MariaDB, and MySQL.
// ABOUTME: Applies generated provider migrations and verifies payload bounds, insert uniqueness, and one consume winner.

using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
[NotInParallel("AdmissionAuthorityProviderDb")]
public sealed class AtprotoTransientStoreRepositoryProviderTests(AdmissionAuthorityProviderFixture fixture)
{
    private const string Digest = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task GeneratedMigrationAndAtomicRepositoryContractPass(PrimaryDatabaseProvider provider)
    {
        await using (ExploreDbContext migrator = CreateContext(provider, PrimaryDatabaseRole.Migrator)) await migrator.Database.MigrateAsync();
        await using (ExploreDbContext cleanup = CreateContext(provider))
        {
            await cleanup.AtprotoTransientRecords.ExecuteDeleteAsync();
            await cleanup.AtprotoTransientAssertionReplays.ExecuteDeleteAsync();
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid tenant = Guid.CreateVersion7();
        AtprotoTransientRecord record = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, Digest, tenant, "opaque", now.AddMinutes(1).ToUnixTimeMilliseconds());
        await using (ExploreDbContext writer = CreateContext(provider))
        {
            var repository = new AtprotoTransientStoreRepository(writer, new FixedTimeProvider(now));
            await Assert.That(await repository.TryCreateAsync(record)).IsTrue();
            AtprotoTransientRecord duplicate = AtprotoTransientRecord.Create(record.Purpose, record.TokenDigest, tenant, "overwrite", record.ExpiresAtUnixMilliseconds);
            await Assert.That(await repository.TryCreateAsync(duplicate)).IsFalse();
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AtprotoTransientRecord?>[] attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using ExploreDbContext context = CreateContext(provider);
            await start.Task;
            return await new AtprotoTransientStoreRepository(context, new FixedTimeProvider(now)).ConsumeAsync(record.Id, record.Purpose, record.TokenDigest, tenant);
        }).ToArray();
        start.SetResult();
        AtprotoTransientRecord?[] results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(results.Count(result => result is not null)).IsEqualTo(1);
        await Assert.That(results.Single(result => result is not null)!.ProtectedPayload).IsEqualTo("opaque");

        await using ExploreDbContext replayContext = CreateContext(provider);
        var replayRepository = new AtprotoTransientAssertionReplayRepository(replayContext, new FixedTimeProvider(now));
        AtprotoTransientAssertionReplay replay = AtprotoTransientAssertionReplay.CreateFromAssertionId("provider-claim", now.AddSeconds(35).ToUnixTimeMilliseconds());
        await Assert.That(await replayRepository.TryClaimAsync(replay)).IsTrue();
        await Assert.That(await replayRepository.TryClaimAsync(AtprotoTransientAssertionReplay.CreateFromAssertionId("provider-claim", replay.ExpiresAtUnixMilliseconds))).IsFalse();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer, 'a')]
    [Arguments(PrimaryDatabaseProvider.SqlServer, '\u00e9')]
    [Arguments(PrimaryDatabaseProvider.MariaDb, 'a')]
    [Arguments(PrimaryDatabaseProvider.MariaDb, '\u00e9')]
    [Arguments(PrimaryDatabaseProvider.MySql, 'a')]
    [Arguments(PrimaryDatabaseProvider.MySql, '\u00e9')]
    public async Task MaximumProtectedPayloadRoundTripsThroughGeneratedMigration(
        PrimaryDatabaseProvider provider, char payloadCharacter)
    {
        await using (ExploreDbContext migrator = CreateContext(provider, PrimaryDatabaseRole.Migrator))
        {
            await migrator.Database.MigrateAsync();
        }
        await using (ExploreDbContext cleanup = CreateContext(provider))
        {
            await cleanup.AtprotoTransientRecords.ExecuteDeleteAsync();
        }

        string payload = new(payloadCharacter,
            AtprotoTransientRecord.MaximumProtectedPayloadBytes / Encoding.UTF8.GetByteCount(payloadCharacter.ToString()));
        await Assert.That(Encoding.UTF8.GetByteCount(payload)).IsEqualTo(AtprotoTransientRecord.MaximumProtectedPayloadBytes);
        DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(2_000_000_000_000);
        Guid tenant = Guid.CreateVersion7();
        AtprotoTransientRecord record = AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.OAuthState, Digest, tenant, payload, now.AddMinutes(1).ToUnixTimeMilliseconds());

        await using (ExploreDbContext writer = CreateContext(provider))
        {
            var repository = new AtprotoTransientStoreRepository(writer, new FixedTimeProvider(now));
            await Assert.That(await repository.TryCreateAsync(record)).IsTrue();
        }
        await using (ExploreDbContext reader = CreateContext(provider))
        {
            var repository = new AtprotoTransientStoreRepository(reader, new FixedTimeProvider(now));
            AtprotoTransientRecord? restored = await repository.ReadAsync(record.Purpose, record.TokenDigest, tenant);
            await Assert.That(restored?.ProtectedPayload).IsEqualTo(payload);
        }
        await using (ExploreDbContext consumer = CreateContext(provider))
        {
            var repository = new AtprotoTransientStoreRepository(consumer, new FixedTimeProvider(now));
            AtprotoTransientRecord? consumed = await repository.ConsumeAsync(record.Id, record.Purpose, record.TokenDigest, tenant);
            await Assert.That(consumed?.ProtectedPayload).IsEqualTo(payload);
        }
    }

    private ExploreDbContext CreateContext(PrimaryDatabaseProvider provider, PrimaryDatabaseRole role = PrimaryDatabaseRole.Runtime)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.CreateOptions(provider, role));
        return new ExploreDbContext(builder.Options);
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
