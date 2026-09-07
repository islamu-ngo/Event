using Event.Persistence.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ActorPrivacyTombstoneMigrationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CurrentBaseline_AllowsOwnerlessDeletedActorTombstone()
    {
        await fixture.ResetAsync();
        await ExecuteAsync(
            """
            INSERT INTO actors
                (id, actor_type_id, is_suspended, created_at, is_deleted, deleted_at, concurrency_stamp)
            VALUES
                (@id, 1, FALSE, NOW(), TRUE, NOW(), @stamp)
            """,
            ("id", Guid.CreateVersion7()),
            ("stamp", Guid.CreateVersion7()));

        string definition = await ReadConstraintDefinitionAsync();
        await Assert.That(definition).Contains("num_nonnulls");
        await Assert.That(definition).Contains("is_deleted");
    }

    [Test]
    public async Task CurrentBaseline_RejectsOwnerlessLiveActor()
    {
        await fixture.ResetAsync();
        var exception = await Assert.That(async () => await ExecuteAsync(
                """
                INSERT INTO actors
                    (id, actor_type_id, is_suspended, created_at, is_deleted, concurrency_stamp)
                VALUES
                    (@id, 1, FALSE, NOW(), FALSE, @stamp)
                """,
                ("id", Guid.CreateVersion7()),
                ("stamp", Guid.CreateVersion7())))
            .Throws<PostgresException>();

        await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
        await Assert.That(exception.ConstraintName).IsEqualTo("ck_actors_exactly_one_owner");
    }

    private async Task<string> ReadConstraintDefinitionAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conname = 'ck_actors_exactly_one_owner'
            """,
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
