// ABOUTME: Specifies domain and relational invariants for instance-owned ATProto transient authentication rows.
// ABOUTME: Covers closed purposes, payload bounds, immutability, insert-only claims, expiry, and fail-closed guards.

using System.Text;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteAtprotoTransientContract")]
public sealed class AtprotoTransientStoreContractTests
{
    private const string DigestA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(2_000_000_000_000);

    [Test]
    public async Task DomainFactories_EnforcePurposeTenantDigestPayloadAndHealthProbeInvariants()
    {
        Guid tenant = Guid.CreateVersion7();
        AtprotoTransientRecord authentication = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, DigestA, tenant, "opaque", Now.AddMinutes(1).ToUnixTimeMilliseconds());
        AtprotoTransientRecord probe = AtprotoTransientRecord.CreateHealthProbe(DigestB, "probe", Now.AddSeconds(30).ToUnixTimeMilliseconds());
        await Assert.That(authentication.TenantId).IsEqualTo(tenant);
        await Assert.That(probe.TenantId).IsNull();
        await Assert.That(probe.Purpose).IsEqualTo(AtprotoTransientPurpose.HealthProbe);
        await Assert.That(() => AtprotoTransientRecord.Create(AtprotoTransientPurpose.HealthProbe, DigestA, tenant, "opaque", 1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => AtprotoTransientRecord.Create(AtprotoTransientPurpose.TenantHandoff, DigestA, Guid.Empty, "opaque", 1)).Throws<ArgumentException>();
        await Assert.That(() => AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, DigestA.ToUpperInvariant(), tenant, "opaque", 1)).Throws<ArgumentException>();
        string oversizedUtf8 = new('é', AtprotoTransientRecord.MaximumProtectedPayloadBytes / 2 + 1);
        await Assert.That(Encoding.UTF8.GetByteCount(oversizedUtf8)).IsGreaterThan(AtprotoTransientRecord.MaximumProtectedPayloadBytes);
        await Assert.That(() => AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, DigestA, tenant, oversizedUtf8, 1)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Mapping_IsImmutableAndTrackedMutationIsRejected()
    {
        await using SqliteContractDatabase database = await SqliteContractDatabase.CreateAsync();
        Guid tenant = Guid.CreateVersion7();
        AtprotoTransientRecord record = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, DigestA, tenant, "opaque", Now.AddMinutes(1).ToUnixTimeMilliseconds());
        database.Context.Add(record);
        await database.Context.SaveChangesAsync();
        foreach (string propertyName in new[] { nameof(record.Purpose), nameof(record.TokenDigest), nameof(record.TenantId), nameof(record.ProtectedPayload), nameof(record.ExpiresAtUnixMilliseconds) })
            await Assert.That(database.Context.Model.FindEntityType(typeof(AtprotoTransientRecord))!.FindProperty(propertyName)!.GetAfterSaveBehavior()).IsEqualTo(PropertySaveBehavior.Throw);
        foreach (string propertyName in new[] { nameof(AtprotoTransientAssertionReplay.AssertionDigest), nameof(AtprotoTransientAssertionReplay.ExpiresAtUnixMilliseconds) })
            await Assert.That(database.Context.Model.FindEntityType(typeof(AtprotoTransientAssertionReplay))!.FindProperty(propertyName)!.GetAfterSaveBehavior()).IsEqualTo(PropertySaveBehavior.Throw);
        database.Context.Entry(record).Property(nameof(record.ProtectedPayload)).CurrentValue = "changed";
        await Assert.That(async () => await database.Context.SaveChangesAsync()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DuplicateCreateExpiryAndBindingMismatch_FailClosedWithoutOverwrite()
    {
        await using SqliteContractDatabase database = await SqliteContractDatabase.CreateAsync();
        Guid tenant = Guid.CreateVersion7();
        var repository = new AtprotoTransientStoreRepository(database.Context, new FixedTimeProvider(Now));
        AtprotoTransientRecord first = AtprotoTransientRecord.Create(AtprotoTransientPurpose.TenantHandoff, DigestA, tenant, "first", Now.AddMinutes(1).ToUnixTimeMilliseconds());
        AtprotoTransientRecord duplicate = AtprotoTransientRecord.Create(AtprotoTransientPurpose.TenantHandoff, DigestA, tenant, "second", Now.AddMinutes(1).ToUnixTimeMilliseconds());
        await Assert.That(await repository.TryCreateAsync(first)).IsTrue();
        await Assert.That(await repository.TryCreateAsync(duplicate)).IsFalse();
        await Assert.That((await repository.ReadAsync(first.Purpose, DigestA, tenant))!.ProtectedPayload).IsEqualTo("first");
        await Assert.That(await repository.ReadAsync(first.Purpose, DigestA, Guid.CreateVersion7())).IsNull();
        await Assert.That(await repository.ReadAsync(AtprotoTransientPurpose.OAuthState, DigestA, tenant)).IsNull();

        AtprotoTransientRecord expiresAtCutoff = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, DigestB, tenant, "expired", Now.ToUnixTimeMilliseconds());
        database.Context.Add(expiresAtCutoff);
        await database.Context.SaveChangesAsync();
        await Assert.That(await repository.ReadOAuthStateAsync(DigestB)).IsNull();
        await Assert.That(await repository.ConsumeAsync(expiresAtCutoff.Id, expiresAtCutoff.Purpose, DigestB, tenant)).IsNull();
    }

    [Test]
    public async Task ReplayClaimsAndCleanup_AreInsertOnlyAndBoundedAtCutoff()
    {
        await using SqliteContractDatabase database = await SqliteContractDatabase.CreateAsync();
        var replayRepository = new AtprotoTransientAssertionReplayRepository(
            database.Context,
            new FixedTimeProvider(Now.AddMilliseconds(-1)));
        AtprotoTransientAssertionReplay first = AtprotoTransientAssertionReplay.CreateFromAssertionId("claim-a", Now.ToUnixTimeMilliseconds());
        AtprotoTransientAssertionReplay duplicate = AtprotoTransientAssertionReplay.CreateFromAssertionId("claim-a", Now.AddMinutes(1).ToUnixTimeMilliseconds());
        AtprotoTransientAssertionReplay live = AtprotoTransientAssertionReplay.CreateFromAssertionId("claim-b", Now.AddMilliseconds(1).ToUnixTimeMilliseconds());
        await Assert.That(first.AssertionDigest.Length).IsEqualTo(AtprotoTransientRecord.Sha256DigestLength);
        await Assert.That(first.AssertionDigest).IsEqualTo(first.AssertionDigest.ToLowerInvariant());
        await Assert.That(first.AssertionDigest).DoesNotContain("claim-a");
        await Assert.That(await replayRepository.TryClaimAsync(first)).IsTrue();
        await Assert.That(await replayRepository.TryClaimAsync(duplicate)).IsFalse();
        await Assert.That(await replayRepository.TryClaimAsync(live)).IsTrue();

        database.Context.ChangeTracker.Clear();
        AtprotoTransientAssertionReplay primaryKeyConflict =
            AtprotoTransientAssertionReplay.CreateFromAssertionId("claim-c", Now.AddMinutes(1).ToUnixTimeMilliseconds());
        database.Context.Entry(primaryKeyConflict).Property(nameof(primaryKeyConflict.Id)).CurrentValue = live.Id;
        await Assert.That(async () => await replayRepository.TryClaimAsync(primaryKeyConflict)).Throws<DbUpdateException>();
        database.Context.Entry(primaryKeyConflict).State = EntityState.Detached;

        await Assert.That(await replayRepository.DeleteExpiredAsync(Now.ToUnixTimeMilliseconds(), 1)).IsEqualTo(1);
        AtprotoTransientAssertionReplay remaining = await database.Context.AtprotoTransientAssertionReplays.AsNoTracking().SingleAsync();
        await Assert.That(remaining.Id).IsEqualTo(live.Id);
        await Assert.That(remaining.AssertionDigest).IsEqualTo(live.AssertionDigest);
        await Assert.That(remaining.ExpiresAtUnixMilliseconds).IsEqualTo(live.ExpiresAtUnixMilliseconds);
        await Assert.That(async () => await replayRepository.DeleteExpiredAsync(Now.ToUnixTimeMilliseconds(), 501)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task HealthProbe_UsesOnlyDedicatedTenantlessOperations()
    {
        await using SqliteContractDatabase database = await SqliteContractDatabase.CreateAsync();
        var repository = new AtprotoTransientStoreRepository(database.Context, new FixedTimeProvider(Now));
        AtprotoTransientRecord probe = AtprotoTransientRecord.CreateHealthProbe(
            DigestA,
            "synthetic-opaque",
            Now.AddSeconds(30).ToUnixTimeMilliseconds());

        await Assert.That(async () => await repository.TryCreateAsync(probe)).Throws<ArgumentException>();
        await Assert.That(await repository.TryCreateHealthProbeAsync(probe)).IsTrue();
        await Assert.That(await repository.ConsumeHealthProbeAsync(probe.Id, probe.TokenDigest)).IsTrue();
        await Assert.That(await repository.ConsumeHealthProbeAsync(probe.Id, probe.TokenDigest)).IsFalse();
    }

    [Test]
    public async Task TransientCleanup_IsBoundedAndPreservesRowsAfterCutoff()
    {
        await using SqliteContractDatabase database = await SqliteContractDatabase.CreateAsync();
        Guid tenant = Guid.CreateVersion7();
        database.Context.Add(AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.OAuthState,
            DigestA,
            tenant,
            "at-cutoff",
            Now.ToUnixTimeMilliseconds()));
        database.Context.Add(AtprotoTransientRecord.Create(
            AtprotoTransientPurpose.TenantHandoff,
            DigestB,
            tenant,
            "live",
            Now.AddMilliseconds(1).ToUnixTimeMilliseconds()));
        await database.Context.SaveChangesAsync();
        var repository = new AtprotoTransientStoreRepository(database.Context, new FixedTimeProvider(Now));

        await Assert.That(await repository.DeleteExpiredAsync(Now.ToUnixTimeMilliseconds(), 1)).IsEqualTo(1);
        AtprotoTransientRecord remaining = await database.Context.AtprotoTransientRecords.AsNoTracking().SingleAsync();
        await Assert.That(remaining.ProtectedPayload).IsEqualTo("live");
        await Assert.That(async () => await repository.DeleteExpiredAsync(Now.ToUnixTimeMilliseconds(), 501)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Consume_RejectsAmbientTransactionsAndNonRelationalProviders()
    {
        await using SqliteContractDatabase database = await SqliteContractDatabase.CreateAsync();
        Guid tenant = Guid.CreateVersion7();
        AtprotoTransientRecord record = AtprotoTransientRecord.Create(AtprotoTransientPurpose.OAuthState, DigestA, tenant, "opaque", Now.AddMinutes(1).ToUnixTimeMilliseconds());
        database.Context.Add(record);
        await database.Context.SaveChangesAsync();
        await using var transaction = await database.Context.Database.BeginTransactionAsync();
        var repository = new AtprotoTransientStoreRepository(database.Context, new FixedTimeProvider(Now));
        await Assert.That(async () => await repository.ConsumeAsync(record.Id, record.Purpose, record.TokenDigest, tenant)).Throws<InvalidOperationException>();
        await transaction.RollbackAsync();

        var options = TestDbContextOptions.Create<ExploreDbContext>().UseTestInMemoryDatabase($"transient-{Guid.CreateVersion7():N}").Options;
        await using var nonRelational = new ExploreDbContext(options);
        var nonRelationalRepository = new AtprotoTransientStoreRepository(nonRelational, new FixedTimeProvider(Now));
        await Assert.That(async () => await nonRelationalRepository.ReadOAuthStateAsync(DigestA)).Throws<InvalidOperationException>();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class SqliteContractDatabase : ExploreDbContext
    {
        private readonly string path;

        private SqliteContractDatabase(string path)
            : base(TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = false,
                    DefaultTimeout = 30,
                }.ToString())
                .UseSnakeCaseNamingConvention()
                .Options)
        {
            this.path = path;
        }

        public ExploreDbContext Context => this;

        public static async Task<SqliteContractDatabase> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"atproto-contract-{Guid.CreateVersion7():N}.db");
            var database = new SqliteContractDatabase(path);
            await database.Database.EnsureCreatedAsync();
            await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            return database;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(path + "-shm");
            File.Delete(path + "-wal");
        }
    }
}
