using Explore.Domain;
using Explore.Persistence.Privacy.ErasureAuthority;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Event.Persistence.IntegrationTests.Privacy;

public sealed class EmbeddedAuthorityMigrationRecoveryTests
{
    [Test]
    [Arguments("identity")]
    [Arguments("revision")]
    [Arguments("generation")]
    public async Task InvalidRetainedCoordinatesAbortUpgradeWithoutReplacement(string defect)
    {
        var root = Directory.CreateTempSubdirectory("authority-invalid-upgrade-");
        SqliteConnection? connection = null;
        try
        {
            await using var context = new EmbeddedPrivacyErasureAuthorityDbContextFactory()
                .CreateDbContext(["--PrivacyErasureAuthorityEmbedded:Path", Path.Combine(root.FullName, "authority.db")]);
            await context.Database.OpenConnectionAsync();
            connection = (SqliteConnection)context.Database.GetDbConnection();
            await context.Database.ExecuteSqlRawAsync("PRAGMA trusted_schema=OFF");
            await context.GetService<IMigrator>().MigrateAsync("PrepareEmbeddedAuthorityRecoveryCoordinates");
            var identity = defect == "identity" ? Guid.Empty : Guid.CreateVersion7();
            var revision = defect == "revision" ? 0L : 1L;
            var generation = defect == "generation" ? 0L : 1L;
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ie_authority_counter
                    (singleton, last_sequence, retained_floor_sequence, store_id, revision, recovery_generation)
                VALUES (1, 7, 3, {identity}, {revision}, {generation})
                """);
            var failure = await Assert.ThrowsAsync<SqliteException>(() => context.Database.MigrateAsync());
            await Assert.That(failure!.SqliteErrorCode).IsEqualTo(19);
            var retained = await context.AuthorityCounters.AsNoTracking().SingleAsync();
            await Assert.That(retained.StoreId).IsEqualTo(identity);
            await Assert.That(retained.Revision).IsEqualTo(revision);
            await Assert.That(retained.RecoveryGeneration).IsEqualTo(generation);
            await Assert.That(retained.LastSequence).IsEqualTo(7L);
            await Assert.That(retained.RetainedFloorSequence).IsEqualTo(3L);
            await Assert.That((await context.Database.GetAppliedMigrationsAsync()).Any(migration =>
                migration.EndsWith("_RestoreEmbeddedAuthorityRecoveryCoordinates", StringComparison.Ordinal))).IsFalse();
        }
        finally
        {
            if (connection is not null) SqliteConnection.ClearPool(connection);
            root.Delete(recursive: true);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MigrationPreservesRetainedCounterAndStableRecoveryIdentity(bool populated, bool preserveIdentity)
    {
        var root = Directory.CreateTempSubdirectory("authority-migration-");
        try
        {
            await VerifyUpgradeAsync(Path.Combine(root.FullName, "authority.db"), populated, preserveIdentity);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static async Task VerifyUpgradeAsync(string path, bool populated, bool preserveIdentity)
    {
        await using var originalContext = new EmbeddedPrivacyErasureAuthorityDbContextFactory()
            .CreateDbContext(["--PrivacyErasureAuthorityEmbedded:Path", path]);
        var context = originalContext;
        await context.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA trusted_schema=OFF");
            await context.GetService<IMigrator>().MigrateAsync("20261002034900_RetainedExternalIdentityFences");
            var retainedIntent = Guid.CreateVersion7();
            var retainedSubject = Guid.CreateVersion7();
            if (populated)
            {
                // The old catalog predates the recovery fields, so the current entity cannot seed it.
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO ie_authority_counter (singleton, last_sequence, retained_floor_sequence, identity_key_id, identity_key_verification_tag) VALUES (1, 12, 5, 'retained-key', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa')");
                var requested = DateTime.UtcNow.Ticks;
                var expires = DateTime.UtcNow.AddDays(1).Ticks;
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ie_erasure_intents
                    (authority_sequence, intent_id, subject_kind, subject_id, reason_code,
                     policy_version, requested_at_utc, recorded_at_utc, retention_expires_at_utc)
                VALUES (12, {retainedIntent}, 1, {retainedSubject}, 1, 1, {requested}, {requested}, {expires})
                """);
            }
            Guid? createdIdentity = null;
            if (populated)
            {
                await context.GetService<IMigrator>().MigrateAsync("PrepareEmbeddedAuthorityRecoveryCoordinates");
                if (preserveIdentity)
                {
                    createdIdentity = Guid.CreateVersion7();
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE ie_authority_counter SET store_id = {createdIdentity.Value} WHERE singleton = 1");
                }
            }
            await using var resumed = populated
                ? new EmbeddedPrivacyErasureAuthorityDbContextFactory()
                    .CreateDbContext(["--PrivacyErasureAuthorityEmbedded:Path", path])
                : null;
            if (resumed is not null)
            {
                await context.DisposeAsync();
                SqliteConnection.ClearPool(connection);
                context = resumed;
                await context.Database.OpenConnectionAsync();
                connection = (SqliteConnection)context.Database.GetDbConnection();
                await context.Database.ExecuteSqlRawAsync("PRAGMA trusted_schema=OFF");
            }
            var migrationStarted = DateTimeOffset.UtcNow.AddMilliseconds(-1);
            await context.Database.MigrateAsync();
            var migrationCompleted = DateTimeOffset.UtcNow.AddMilliseconds(1);
            if (!populated)
            {
                var created = PrivacyErasureCounter.Start();
                createdIdentity = created.StoreId;
                context.AuthorityCounters.Add(created);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }
            var counter = await context.AuthorityCounters.SingleAsync();
            var recovery = counter.GetRecoveryState();
            await Assert.That(counter.StoreId.Version).IsEqualTo(7);
            if (createdIdentity is { } explicitIdentity)
                await Assert.That(counter.StoreId).IsEqualTo(explicitIdentity);
            await Assert.That(counter.LastSequence).IsEqualTo(populated ? 12L : 0L);
            await Assert.That(counter.RetainedFloorSequence).IsEqualTo(populated ? 5L : 0L);
            await Assert.That(counter.Revision).IsEqualTo(1L);
            await Assert.That(counter.RecoveryGeneration).IsEqualTo(1L);
            if (populated)
            {
                if (!preserveIdentity)
                {
                    var timestamp = Convert.ToInt64(counter.StoreId.ToString("N")[..12], 16);
                    await Assert.That(timestamp >= migrationStarted.ToUnixTimeMilliseconds()
                        && timestamp <= migrationCompleted.ToUnixTimeMilliseconds()).IsTrue();
                }
                await Assert.That(counter.IdentityKeyId).IsEqualTo("retained-key");
                await Assert.That(counter.IdentityKeyVerificationTag).IsEqualTo(new string('a', 64));
                var retained = await context.ErasureIntents.SingleAsync();
                await Assert.That(retained.IntentId).IsEqualTo(retainedIntent);
                await Assert.That(retained.SubjectId).IsEqualTo(retainedSubject);
                await Assert.That(retained.AuthoritySequence).IsEqualTo(12L);
            }
            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();
            await Assert.That((await context.AuthorityCounters.SingleAsync()).GetRecoveryState()).IsEqualTo(recovery);
            await Assert.That(context.Database.HasPendingModelChanges()).IsFalse();
            await context.Database.CloseConnectionAsync();
            SqliteConnection.ClearPool(connection);
            await using var reopened = new EmbeddedPrivacyErasureAuthorityDbContextFactory()
                .CreateDbContext(["--PrivacyErasureAuthorityEmbedded:Path", path]);
            await reopened.Database.MigrateAsync();
            await Assert.That((await reopened.AuthorityCounters.SingleAsync()).GetRecoveryState()).IsEqualTo(recovery);
            SqliteConnection.ClearPool((SqliteConnection)reopened.Database.GetDbConnection());
            await using var raw = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await raw.OpenAsync();
            await using var integrity = raw.CreateCommand();
            integrity.CommandText = "PRAGMA quick_check";
            await Assert.That(await integrity.ExecuteScalarAsync()).IsEqualTo("ok");
        }
        finally
        {
            SqliteConnection.ClearPool(connection);
        }
    }
}
