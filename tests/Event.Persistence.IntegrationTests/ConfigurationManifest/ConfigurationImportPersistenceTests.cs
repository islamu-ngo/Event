namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

using System.Text;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

public sealed class ConfigurationImportPersistenceTests
{
    private static readonly DateTime OccurredAt =
        new(2026, 8, 30, 19, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ProtectedStore_EncryptsRoundTripsAndDeletesArtifactBytes()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var store = new ConfigurationImportArtifactRepository(
            database.Context,
            new EphemeralDataProtectionProvider());
        byte[] plaintext =
            Encoding.UTF8.GetBytes("repository-native-artifact-sentinel");
        var handle = new ConfigurationImportArtifactHandle(Guid.CreateVersion7());

        ConfigurationImportArtifactReference reference = await store.StoreAsync(
            handle,
            plaintext,
            OccurredAt,
            OccurredAt.AddMinutes(30),
            CancellationToken.None);
        byte[] protectedBytes = await database.ReadOnlyProtectedPayloadAsync();
        ReadOnlyMemory<byte> roundTrip = await store.ReadAsync(
            handle,
            CancellationToken.None);
        await store.DeleteAsync(handle, CancellationToken.None);

        await Assert.That(reference.Sha256Digest)
            .IsEqualTo(ConfigurationImportDigest.ComputeBytes(plaintext));
        await Assert.That(roundTrip.ToArray()).IsEquivalentTo(plaintext);
        await Assert.That(protectedBytes).IsNotEquivalentTo(plaintext);
        await Assert.That(Encoding.UTF8.GetString(protectedBytes))
            .DoesNotContain("repository-native-artifact-sentinel");
        ConfigurationImportSessionException missing =
            await Assert.That(async () => await store.ReadAsync(
                    handle,
                    CancellationToken.None))
                .Throws<ConfigurationImportSessionException>();
        await Assert.That(missing.FailureCode)
            .IsEqualTo(ConfigurationImportFailureCodes.ArtifactMissing);
    }

    [Test]
    public async Task DirectTransferRepositories_CommitSessionAndEncryptedChunk()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var sessions = new ConfigurationDirectTransferRepository(database.Context);
        var chunks = new ConfigurationDirectTransferChunkRepository(
            database.Context,
            new EphemeralDataProtectionProvider());
        var unitOfWork = new EfCoreUnitOfWork(database.Context);
        byte[] artifact = Encoding.UTF8.GetBytes("portable-transfer-artifact");
        string digest = ConfigurationImportDigest.ComputeBytes(artifact);
        var session = ConfigurationDirectTransferSession.Create(
            Guid.CreateVersion7(),
            "source-instance",
            "instance",
            targetTenantId: null,
            digest,
            digest,
            digest,
            digest,
            artifact.Length,
            OccurredAt,
            OccurredAt.AddMinutes(30));

        await unitOfWork.ExecuteInTransactionAsync(
            token => sessions.AddAsync(session, token),
            CancellationToken.None);
        database.Context.ChangeTracker.Clear();
        ConfigurationDirectTransferSession? persisted =
            await sessions.GetForUpdateAsync(
                session.Id,
                "instance",
                CancellationToken.None);

        await Assert.That(persisted?.Id).IsEqualTo(session.Id);

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                bool stored = await chunks.AddAsync(
                    session.Id,
                    0,
                    artifact,
                    digest,
                    OccurredAt.AddMinutes(30),
                    token);
                await Assert.That(stored).IsTrue();
            },
            CancellationToken.None);
        database.Context.ChangeTracker.Clear();

        ReadOnlyMemory<byte> assembled = await chunks.AssembleAsync(
            session.Id,
            artifact.Length,
            CancellationToken.None);
        await Assert.That(assembled.ToArray()).IsEquivalentTo(artifact);
    }

    [Test]
    public async Task ManagedApplySchedule_PersistsTargetAndOptimisticReviewState()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var repository = new ConfigurationManagedApplyScheduleRepository(
            database.Context);
        string digest = ConfigurationImportDigest.Compute(["managed-plan"]);
        Guid uploader = Guid.CreateVersion7();
        Guid reviewer = Guid.CreateVersion7();
        ConfigurationManagedApplySchedule schedule =
            ConfigurationManagedApplySchedule.Create(
                Guid.CreateVersion7(),
                "instance",
                digest,
                digest,
                digest,
                uploader,
                OccurredAt.AddMinutes(5),
                OccurredAt.AddHours(1),
                OccurredAt);

        await repository.AddAsync(schedule, CancellationToken.None);
        database.Context.ChangeTracker.Clear();
        ConfigurationManagedApplySchedule? persisted =
            await repository.GetForUpdateAsync(
                schedule.Id,
                "instance",
                CancellationToken.None);
        ConfigurationManagedApplySchedule? wrongTarget =
            await repository.GetForUpdateAsync(
                schedule.Id,
                "tenant:other",
                CancellationToken.None);

        await Assert.That(persisted).IsNotNull();
        await Assert.That(wrongTarget).IsNull();
        persisted!.Approve(reviewer, OccurredAt.AddMinutes(1));
        await repository.UpdateAsync(persisted, CancellationToken.None);
        database.Context.ChangeTracker.Clear();
        ConfigurationManagedApplySchedule? approved =
            await repository.GetForUpdateAsync(
                schedule.Id,
                "instance",
                CancellationToken.None);
        await Assert.That(approved?.Status)
            .IsEqualTo(ConfigurationManagedApplyScheduleStatus.Approved);
        await Assert.That(approved?.Revision).IsEqualTo(2);
    }

    [Test]
    public async Task Repository_RequiresMatchingTrustedTarget()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var repository =
            new ConfigurationImportSessionRepository(database.Context);
        ConfigurationImportTarget firstTarget =
            ConfigurationImportTarget.ForTenant(Guid.CreateVersion7());
        ConfigurationImportTarget secondTarget =
            ConfigurationImportTarget.ForTenant(Guid.CreateVersion7());
        ConfigurationImportSession session = CreateSession(firstTarget);
        await repository.AddAsync(session, CancellationToken.None);
        database.Context.ChangeTracker.Clear();

        ConfigurationImportSession? matching = await repository.GetForUpdateAsync(
            session.SessionId,
            firstTarget,
            CancellationToken.None);
        database.Context.ChangeTracker.Clear();
        ConfigurationImportSession? crossTenant = await repository.GetForUpdateAsync(
            session.SessionId,
            secondTarget,
            CancellationToken.None);

        await Assert.That(matching?.SessionId).IsEqualTo(session.SessionId);
        await Assert.That(crossTenant).IsNull();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ProviderModel_HasNoPendingImportSessionChanges(
        PrimaryDatabaseProvider provider)
    {
        await using ExploreDbContext context = CreateModelContext(provider);

        bool hasPendingChanges =
            context.Database.HasPendingModelChanges();

        await Assert.That(hasPendingChanges).IsFalse();
    }

    private static ConfigurationImportSession CreateSession(
        ConfigurationImportTarget target)
    {
        var artifact = new ConfigurationImportArtifactReference(
            new ConfigurationImportArtifactHandle(Guid.CreateVersion7()),
            ConfigurationImportDigest.Compute(["artifact"]),
            100,
            OccurredAt.AddMinutes(30));
        return ConfigurationImportSession.Create(
            Guid.CreateVersion7(),
            target,
            artifact,
            ConfigurationImportDigest.Compute(["token"]),
            OccurredAt,
            TimeSpan.FromMinutes(20));
    }

    private static ExploreDbContext CreateModelContext(
        PrimaryDatabaseProvider provider)
    {
        var optionsBuilder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            optionsBuilder,
            CreateOptions(provider));
        return new ExploreDbContext(optionsBuilder.Options);
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(
        PrimaryDatabaseProvider provider)
    {
        if (provider == PrimaryDatabaseProvider.Sqlite)
        {
            return new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = Path.Combine(
                    Path.GetTempPath(),
                    $"configuration-import-model-{Guid.CreateVersion7():N}.db")
            };
        }

        string ephemeralCredential = Guid.CreateVersion7().ToString("N");
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = "localhost",
            Database = "configuration_import_model",
            Username = ephemeralCredential,
            Password = ephemeralCredential,
            TlsMode = PrimaryDatabaseTlsMode.Prefer,
            ServerFlavor = provider switch
            {
                PrimaryDatabaseProvider.MariaDb =>
                    PrimaryDatabaseServerFlavor.MariaDb,
                PrimaryDatabaseProvider.MySql =>
                    PrimaryDatabaseServerFlavor.MySql,
                _ => null
            },
            ServerVersion = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                PrimaryDatabaseProvider.MySql => new Version(8, 4),
                _ => null
            }
        };
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(
            SqliteConnection connection,
            ExploreDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public ExploreDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = ":memory:"
                }.ToString());
            await connection.OpenAsync();
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection)
                .UseSnakeCaseNamingConvention()
                .Options;
            var context = new ExploreDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async Task<byte[]> ReadOnlyProtectedPayloadAsync()
        {
            await using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT protected_payload FROM ie_configuration_import_artifacts LIMIT 1";
            object? result = await command.ExecuteScalarAsync();
            return result as byte[]
                ?? throw new InvalidOperationException(
                    "Protected import payload was not persisted.");
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

}
