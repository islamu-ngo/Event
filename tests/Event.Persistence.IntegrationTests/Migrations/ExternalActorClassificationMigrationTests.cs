// ABOUTME: Verifies the external-unclassified Actor lookup, backfill migration, and ownership/type constraint in PostgreSQL.
// ABOUTME: Proves legacy BOT classification cannot be reintroduced for an Actor owned by ExternalActorSubject.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ExternalActorClassificationMigrationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CurrentBaselineContainsExternalUnclassifiedLookupAndConstraint()
    {
        await fixture.ResetAsync();

        await Assert.That(await ScalarAsync(
            "SELECT COUNT(*) FROM actor_types WHERE id = 6 AND master_code = 'EXTERNAL_UNCLASSIFIED'"))
            .IsEqualTo(1L);
        await Assert.That(await ScalarAsync(
            "SELECT COUNT(*) FROM pg_constraint WHERE conname = 'ck_actors_external_type_matches_owner'"))
            .IsEqualTo(1L);
        await using ExploreDbContext context = fixture.CreateDbContext();
        long availableMigrationCount = context.Database.GetMigrations().LongCount();
        await Assert.That(await ScalarAsync(
            "SELECT COUNT(*) FROM \"__EFMigrationsHistory\""))
            .IsEqualTo(availableMigrationCount);
        await Assert.That(await ScalarAsync(
            "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%_Init'"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task CurrentBaselineRejectsBotActorOwnedByExternalSubject()
    {
        await fixture.ResetAsync();
        var subjectId = Guid.CreateVersion7();

        await ExecuteAsync(
            """
            INSERT INTO external_actor_subjects
                (id, first_observed_at, last_observed_at, created_at, is_deleted, concurrency_stamp)
            VALUES (@id, NOW(), NOW(), NOW(), FALSE, @stamp)
            """,
            ("id", subjectId),
            ("stamp", Guid.CreateVersion7()));

        var exception = await Assert.That(async () => await ExecuteAsync(
                """
                INSERT INTO actors
                    (id, actor_type_id, external_actor_subject_id, is_suspended, created_at, is_deleted, concurrency_stamp)
                VALUES (@id, 3, @subject_id, FALSE, NOW(), FALSE, @stamp)
                """,
                ("id", Guid.CreateVersion7()),
                ("subject_id", subjectId),
                ("stamp", Guid.CreateVersion7())))
            .Throws<PostgresException>();

        await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
        await Assert.That(exception.ConstraintName).IsEqualTo("ck_actors_external_type_matches_owner");
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
