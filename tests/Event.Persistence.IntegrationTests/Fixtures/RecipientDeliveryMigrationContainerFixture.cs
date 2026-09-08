using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Fixtures;

public sealed class RecipientDeliveryMigrationContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("recipient_delivery_migration_" + Guid.NewGuid().ToString("N"))
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task ResetAsync()
    {
        var database = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (database.Database is not { } databaseName
            || !databaseName.StartsWith("recipient_delivery_migration_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to reset a non-test migration database.");
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            DO $reset$
            DECLARE schema_name text;
            BEGIN
                FOR schema_name IN
                    SELECT nspname
                    FROM pg_catalog.pg_namespace
                    WHERE nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
                      AND nspname NOT LIKE 'pg_temp_%'
                      AND nspname NOT LIKE 'pg_toast_temp_%'
                LOOP
                    EXECUTE format('DROP SCHEMA %I CASCADE', schema_name);
                END LOOP;
                CREATE SCHEMA public;
            END
            $reset$;
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }
}
