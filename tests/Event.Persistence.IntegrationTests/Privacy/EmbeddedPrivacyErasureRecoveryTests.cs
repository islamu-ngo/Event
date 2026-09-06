// ABOUTME: Rehearses a primary-only SQLite restore while the embedded erasure authority remains untouched.
// ABOUTME: Proves a restarted authority replays retained intent into restored primary state exactly once.

using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Privacy;

[NotInParallel("EmbeddedPrivacyErasureRecovery")]
public sealed class EmbeddedPrivacyErasureRecoveryTests
{
    [Test]
    [Arguments(2L)]
    [Arguments(3L)]
    public async Task RetentionMaintenance_SequenceGapFailsAtomicallyWithoutAdvancingFloor(
        long missingSequence)
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("orea-gap-");
        string authorityPath = Path.Combine(root.FullName, "privacy_erasure_authority.db");
        try
        {
            await using ServiceProvider provider = await CreateAuthorityProviderAsync(authorityPath);
            IPrivacyErasureAuthority authority = provider.GetRequiredService<IPrivacyErasureAuthority>();
            IPrivacyErasureAuthorityMaintenance maintenance =
                provider.GetRequiredService<IPrivacyErasureAuthorityMaintenance>();
            PrivacyErasureIntent[] facts = new PrivacyErasureIntent[3];
            for (var index = 0; index < facts.Length; index++)
            {
                facts[index] = await authority.AppendAsync(new PrivacyErasureRequest(
                    Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User,
                    Guid.CreateVersion7(),
                    PrivacyErasureReasonCode.AccountDeletion,
                    1));
            }

            IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext> factory = provider
                .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>();
            await using (EmbeddedPrivacyErasureAuthorityDbContext corruption =
                await factory.CreateDbContextAsync())
            {
                await ExpireFactsAsync(corruption);
                await corruption.ErasureIntents
                    .Where(fact => fact.AuthoritySequence == missingSequence)
                    .ExecuteDeleteAsync();
            }

            var request = new PrivacyErasureRetentionRequest(
                DateTime.UtcNow,
                100,
                []);
            await Assert.ThrowsAsync<Explore.Application.Exceptions.PrivacyErasureSequenceGapException>(() =>
                maintenance.EvaluateRetentionAsync(request));
            await Assert.ThrowsAsync<Explore.Application.Exceptions.PrivacyErasureSequenceGapException>(() =>
                maintenance.CompactExpiredIntentsAsync(request));

            await Assert.That(await authority.GetStateAsync())
                .IsEqualTo(new PrivacyErasureAuthorityState(3, 0));
            await using EmbeddedPrivacyErasureAuthorityDbContext verification =
                await factory.CreateDbContextAsync();
            long[] retained = await verification.ErasureIntents
                .OrderBy(fact => fact.AuthoritySequence)
                .Select(fact => fact.AuthoritySequence)
                .ToArrayAsync();
            await Assert.That(retained)
                .IsEquivalentTo(facts
                    .Where(fact => fact.AuthoritySequence != missingSequence)
                    .Select(fact => fact.AuthoritySequence));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RetentionMaintenance_DryRunHoldCompactionReleaseAndFloorAreAtomic()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("orea-maintenance-");
        string authorityPath = Path.Combine(root.FullName, "privacy_erasure_authority.db");
        try
        {
            await using ServiceProvider provider = await CreateAuthorityProviderAsync(authorityPath);
            IPrivacyErasureAuthority authority = provider.GetRequiredService<IPrivacyErasureAuthority>();
            IPrivacyErasureAuthorityMaintenance maintenance =
                provider.GetRequiredService<IPrivacyErasureAuthorityMaintenance>();
            PrivacyErasureIntent[] facts = new PrivacyErasureIntent[3];
            for (var index = 0; index < facts.Length; index++)
            {
                facts[index] = await authority.AppendAsync(new PrivacyErasureRequest(
                    Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User,
                    Guid.CreateVersion7(),
                    PrivacyErasureReasonCode.AccountDeletion,
                    1));
            }
            await ExpireFactsAsync(provider);

            var heldRequest = new PrivacyErasureRetentionRequest(
                DateTime.UtcNow,
                100,
                [facts[1].AuthoritySequence]);
            PrivacyErasureRetentionEvaluation evaluation =
                await maintenance.EvaluateRetentionAsync(heldRequest);

            await Assert.That(evaluation.EligibleCount).IsEqualTo(1);
            await Assert.That(evaluation.HeldCount).IsEqualTo(1);
            await Assert.That(evaluation.ProjectedFloorSequence)
                .IsEqualTo(facts[1].AuthoritySequence);
            await Assert.That(await authority.GetStateAsync())
                .IsEqualTo(new PrivacyErasureAuthorityState(3, 0));

            PrivacyErasureCompactionResult compacted =
                await maintenance.CompactExpiredIntentsAsync(heldRequest);

            await Assert.That(compacted.DeletedCount).IsEqualTo(1);
            await Assert.That(compacted.PseudonymizedCount).IsEqualTo(1);
            await Assert.That(compacted.State).IsEqualTo(new PrivacyErasureAuthorityState(3, 2));
            IReadOnlyList<PrivacyErasureIntent> replayable = await authority.ReadAfterAsync(2, 100);
            await Assert.That(replayable.Select(fact => fact.AuthoritySequence)).IsEquivalentTo([3L]);

            IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext> factory = provider
                .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>();
            await using (EmbeddedPrivacyErasureAuthorityDbContext verification =
                await factory.CreateDbContextAsync())
            {
                PrivacyErasureIntent held = await verification.ErasureIntents
                    .AsNoTracking()
                    .SingleAsync(fact => fact.AuthoritySequence == 2);
                await Assert.That(held.IsLegalHoldPseudonymized).IsTrue();
                await Assert.That(held.IntentId).IsNotEqualTo(facts[1].IntentId);
                await Assert.That(held.SubjectId).IsNotEqualTo(facts[1].SubjectId);
            }

            var releasedRequest = new PrivacyErasureRetentionRequest(
                heldRequest.AsOfUtc,
                100,
                []);
            PrivacyErasureCompactionResult released =
                await maintenance.CompactExpiredIntentsAsync(releasedRequest);

            await Assert.That(released.DeletedCount).IsEqualTo(2);
            await Assert.That(released.PseudonymizedCount).IsEqualTo(0);
            await Assert.That(released.State).IsEqualTo(new PrivacyErasureAuthorityState(3, 3));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ConcurrentAppendAndCompaction_KeepUniqueSequencesAndValidFloor()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("orea-concurrency-");
        string authorityPath = Path.Combine(root.FullName, "privacy_erasure_authority.db");
        try
        {
            await using ServiceProvider provider = await CreateAuthorityProviderAsync(authorityPath);
            IPrivacyErasureAuthority authority = provider.GetRequiredService<IPrivacyErasureAuthority>();
            IPrivacyErasureAuthorityMaintenance maintenance =
                provider.GetRequiredService<IPrivacyErasureAuthorityMaintenance>();
            await authority.AppendAsync(new PrivacyErasureRequest(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1));
            await ExpireFactsAsync(provider);
            var request = new PrivacyErasureRetentionRequest(
                DateTime.UtcNow,
                100,
                []);

            await Task.WhenAll(
                authority.AppendAsync(new PrivacyErasureRequest(
                    Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User,
                    Guid.CreateVersion7(),
                    PrivacyErasureReasonCode.AccountDeletion,
                    1)),
                maintenance.CompactExpiredIntentsAsync(request));

            PrivacyErasureAuthorityState state = await authority.GetStateAsync();
            IReadOnlyList<PrivacyErasureIntent> remaining =
                await authority.ReadAfterAsync(state.RetainedFloorSequence, 100);
            await Assert.That(state.HighWaterSequence).IsEqualTo(2);
            await Assert.That(state.RetainedFloorSequence <= state.HighWaterSequence).IsTrue();
            await Assert.That(remaining.Select(fact => fact.AuthoritySequence).Distinct().Count())
                .IsEqualTo(remaining.Count);
            await Assert.That(remaining.All(fact =>
                fact.AuthoritySequence > state.RetainedFloorSequence)).IsTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            root.Delete(recursive: true);
        }
    }

    [Test]
    [Timeout(300_000)]
    public async Task PrimaryOnlyRestore_RetainsEmbeddedAuthorityAndReplayConvergesExactlyOnce()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("mdb-erasure-recovery-");
        string primaryDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "primary")).FullName;
        string authorityDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "authority")).FullName;
        string backupDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "backup")).FullName;
        string primaryPath = Path.Combine(primaryDirectory, "event.db");
        string authorityPath = Path.Combine(authorityDirectory, "privacy_erasure_authority.db");
        string backupPath = Path.Combine(backupDirectory, "event-before-erasure.db");
        string restoredPath = Path.Combine(primaryDirectory, "event-restored.db");

        try
        {
            GlobalLocationPrivacyErasureTests.ErasureGraph graph;
            await using (ExploreDbContext primary = CreatePrimaryContext(primaryPath))
            {
                await primary.Database.EnsureCreatedAsync();
                await LookupTableSeeder.SeedAsync(primary);
                graph = await GlobalLocationPrivacyErasureTests.SeedErasureGraphAsync(primary);
            }
            await CopyDatabaseAsync(primaryPath, backupPath);

            PrivacyErasureIntent retained;
            AuthorityFactSnapshot[] authorityBeforeRestore;
            await using (ServiceProvider firstProcess = await CreateAuthorityProviderAsync(authorityPath))
            {
                IPrivacyErasureAuthority authority =
                    firstProcess.GetRequiredService<IPrivacyErasureAuthority>();
                retained = await authority.AppendAsync(new PrivacyErasureRequest(
                    Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User,
                    graph.OwnerUserId,
                    PrivacyErasureReasonCode.AccountDeletion,
                    1));
                authorityBeforeRestore = await ReadAuthoritySnapshotAsync(authority);
            }

            await CopyDatabaseAsync(backupPath, restoredPath);
            await using (ExploreDbContext restored = CreatePrimaryContext(restoredPath))
            {
                await Assert.That(await restored.UserPii
                    .AnyAsync(pii => pii.UserId == graph.OwnerUserId)).IsTrue();
                await Assert.That(await restored.PrivacyErasureReplayCheckpoints.CountAsync())
                    .IsEqualTo(0);
                await AssertPrimaryContainsNoAuthorityTablesAsync(restored);
            }

            int checkpointCount;
            int outboxCount;
            await using (ServiceProvider restartedProcess = await CreateAuthorityProviderAsync(authorityPath))
            {
                IPrivacyErasureAuthority authority =
                    restartedProcess.GetRequiredService<IPrivacyErasureAuthority>();
                await Assert.That(await ReadAuthoritySnapshotAsync(authority))
                    .IsEquivalentTo(authorityBeforeRestore);

                await using (ExploreDbContext replayContext = CreatePrimaryContext(restoredPath))
                await using (GlobalLocationPrivacyErasureTests.ErasureRuntime runtime =
                    GlobalLocationPrivacyErasureTests.CreateRuntime(replayContext, authority))
                {
                    await runtime.ReplayService.ReplayAsync(CancellationToken.None);
                }

                await using (ExploreDbContext verified = CreatePrimaryContext(restoredPath))
                {
                    await AssertErasedAsync(verified, graph);
                    PrivacyErasureReplayCheckpoint checkpoint = await verified
                        .PrivacyErasureReplayCheckpoints.SingleAsync();
                    checkpointCount = await verified.PrivacyErasureReplayCheckpoints.CountAsync();
                    outboxCount = await CountPrivacyOutboxAsync(verified);
                    await Assert.That(checkpoint.AuthoritySequence)
                        .IsEqualTo(retained.AuthoritySequence);
                    await Assert.That(checkpoint.IntentId).IsEqualTo(retained.IntentId);
                    await Assert.That(outboxCount).IsEqualTo(4);
                }

                await using (ExploreDbContext repeatedContext = CreatePrimaryContext(restoredPath))
                await using (GlobalLocationPrivacyErasureTests.ErasureRuntime runtime =
                    GlobalLocationPrivacyErasureTests.CreateRuntime(repeatedContext, authority))
                {
                    await runtime.ReplayService.ReplayAsync(CancellationToken.None);
                }

                await using (ExploreDbContext final = CreatePrimaryContext(restoredPath))
                {
                    await AssertErasedAsync(final, graph);
                    await Assert.That(await final.PrivacyErasureReplayCheckpoints.CountAsync())
                        .IsEqualTo(checkpointCount);
                    await Assert.That(await CountPrivacyOutboxAsync(final)).IsEqualTo(outboxCount);
                }

                await Assert.That(await ReadAuthoritySnapshotAsync(authority))
                    .IsEquivalentTo(authorityBeforeRestore);
            }

            await Assert.That(Path.GetDirectoryName(primaryPath))
                .IsNotEqualTo(Path.GetDirectoryName(authorityPath));
            await Assert.That(File.Exists(authorityPath)).IsTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            root.Delete(recursive: true);
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("-wal")]
    [Arguments("-shm")]
    public async Task ExistingAuthorityFiles_WithUnsafeUnixPermissions_FailClosed(string suffix)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        DirectoryInfo root = Directory.CreateTempSubdirectory("mdb-erasure-permissions-");
        string authorityPath = Path.Combine(root.FullName, "privacy_erasure_authority.db");
        string unsafePath = authorityPath + suffix;
        try
        {
            await File.WriteAllBytesAsync(unsafePath, [0]);
            File.SetUnixFileMode(
                unsafePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            using var storage = new EmbeddedPrivacyErasureAuthorityStorage(
                EmbeddedOptions(authorityPath));

            InvalidOperationException? exception = await Assert.That(() =>
                    storage.EnsureReadyAsync())
                .Throws<InvalidOperationException>();

            await Assert.That(exception!.Message).Contains("permissions are unsafe");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ExistingAuthoritySymbolicLink_FailsClosed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        DirectoryInfo root = Directory.CreateTempSubdirectory("mdb-erasure-symlink-");
        string targetPath = Path.Combine(root.FullName, "target.db");
        string authorityPath = Path.Combine(root.FullName, "privacy_erasure_authority.db");
        try
        {
            await File.WriteAllBytesAsync(targetPath, [0]);
            File.SetUnixFileMode(targetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.CreateSymbolicLink(authorityPath, targetPath);
            using var storage = new EmbeddedPrivacyErasureAuthorityStorage(
                EmbeddedOptions(authorityPath));

            InvalidOperationException? exception = await Assert.That(() =>
                    storage.EnsureReadyAsync())
                .Throws<InvalidOperationException>();

            await Assert.That(exception!.Message).Contains("symbolic-link");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            root.Delete(recursive: true);
        }
    }

    private static ExploreDbContext CreatePrimaryContext(string path)
    {
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = path,
        };
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        var context = new ExploreDbContext(builder.Options);
        context.EnableTenantFilterBypass("Embedded authority primary-only recovery rehearsal.");
        return context;
    }

    private static async Task<ServiceProvider> CreateAuthorityProviderAsync(string authorityPath)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite",
                ["PrivacyErasureAuthorityEmbedded:Path"] = authorityPath,
                ["PrivacyErasureAuthorityEmbedded:WriterReplicaCount"] = "1",
                ["PrivacyErasureAuthorityEmbedded:BusyTimeoutSeconds"] = "30",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<PrivacyErasureOptions>();
        services.ConfigurePersistenceServices(
            configuration,
            skipDbContextRegistration: true,
            skipLookupCacheInitializer: true);
        services.ConfigureDbContext<EmbeddedPrivacyErasureAuthorityDbContext>(
            TestDbContextOptions.Apply,
            ServiceLifetime.Singleton);
        ServiceProvider provider = services.BuildIsolatedServiceProvider(validateScopes: true);
        try
        {
            await provider.GetRequiredService<EmbeddedPrivacyErasureAuthorityStorage>()
                .EnsureReadyAsync();
            IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext> factory = provider
                .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>();
            await using EmbeddedPrivacyErasureAuthorityDbContext context =
                await factory.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();
            return provider;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private static async Task ExpireFactsAsync(ServiceProvider provider)
    {
        IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext> factory = provider
            .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>();
        await using EmbeddedPrivacyErasureAuthorityDbContext context =
            await factory.CreateDbContextAsync();
        await ExpireFactsAsync(context);
    }

    private static Task<int> ExpireFactsAsync(
        EmbeddedPrivacyErasureAuthorityDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            "UPDATE ie_erasure_intents "
            + "SET requested_at_utc = {0}, recorded_at_utc = {0}, retention_expires_at_utc = {1}",
            DateTime.UtcNow.AddDays(-2),
            DateTime.UtcNow.AddDays(-1));

    private static EmbeddedPrivacyErasureAuthorityOptions EmbeddedOptions(string path) => new()
    {
        Path = path,
        WriterReplicaCount = 1,
        BusyTimeoutSeconds = 30,
    };

    private static async Task CopyDatabaseAsync(string sourcePath, string destinationPath)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString);
        await source.OpenAsync();
        await destination.OpenAsync();
        source.BackupDatabase(destination);
    }

    private static async Task AssertPrimaryContainsNoAuthorityTablesAsync(ExploreDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        try
        {
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('ie_authority_counter', 'ie_erasure_intents', 'ie___EFPrivacyErasureAuthorityMigrationsHistory')";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsFalse();
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task AssertErasedAsync(
        ExploreDbContext context,
        GlobalLocationPrivacyErasureTests.ErasureGraph graph)
    {
        await Assert.That(await context.UserPii
            .AnyAsync(pii => pii.UserId == graph.OwnerUserId)).IsFalse();
        await Assert.That(await context.UserPii
            .AnyAsync(pii => pii.UserId == graph.UnrelatedUserId)).IsTrue();
        Location[] homes = await context.Locations
            .IgnoreQueryFilters()
            .Include(location => location.Pii)
            .Where(location => graph.LocationIds.Contains(location.Id))
            .ToArrayAsync();
        await Assert.That(homes.All(home =>
            home.OwnerUserId is null
            && home.Pii is null
            && home.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased)).IsTrue();
    }

    private static Task<int> CountPrivacyOutboxAsync(ExploreDbContext context) =>
        context.OutboxMessages.CountAsync(message =>
            message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
            || message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType);

    private static async Task<AuthorityFactSnapshot[]> ReadAuthoritySnapshotAsync(
        IPrivacyErasureAuthority authority)
    {
        IReadOnlyList<PrivacyErasureIntent> facts = await authority.ReadAfterAsync(0, 500);
        return facts.Select(fact => new AuthorityFactSnapshot(
            fact.AuthoritySequence,
            fact.IntentId,
            fact.SubjectKind,
            fact.SubjectId,
            fact.ReasonCode,
            fact.PolicyVersion,
            fact.RequestedAtUtc,
            fact.RecordedAtUtc,
            fact.RetentionExpiresAtUtc)).ToArray();
    }

    private sealed record AuthorityFactSnapshot(
        long AuthoritySequence,
        Guid IntentId,
        PrivacyErasureSubjectKind SubjectKind,
        Guid SubjectId,
        PrivacyErasureReasonCode ReasonCode,
        int PolicyVersion,
        DateTime RequestedAtUtc,
        DateTime RecordedAtUtc,
        DateTime RetentionExpiresAtUtc);
}
