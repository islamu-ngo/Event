using Event.Persistence.IntegrationTests.Fixtures;
using Npgsql;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Shares the persistence fixture's native DELETE reset, including lookup and source metadata
/// preservation. Row triggers invalidate observations of the deleted transactional data.
/// </summary>
public sealed class TestDatabaseReset
{
    private const string DefaultApplicationSchema = "islamu_event";
    private readonly PostgreSqlDatabaseReset _reset;
    private readonly string _connectionString;

    private TestDatabaseReset(PostgreSqlDatabaseReset reset, string connectionString)
    {
        _reset = reset;
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates a new <see cref="TestDatabaseReset"/> initialized against the target database.
    /// Must be called after migrations and lookup table seeding are complete.
    /// </summary>
    public static async Task<TestDatabaseReset> CreateAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        string applicationSchema = GetApplicationSchema(connectionString);

        var reset = await PostgreSqlDatabaseReset.CreateAsync(connection, applicationSchema);
        return new TestDatabaseReset(reset, connectionString);
    }

    /// <summary>
    /// Resets the database to its post-migration, post-seed state.
    /// Call at the start of each test for deterministic isolation.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _reset.ResetAsync(connection);
    }

    /// <summary>
    /// Uses the same application schema selected by the connection.
    /// </summary>
    private static string GetApplicationSchema(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).SearchPath?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
        ?? DefaultApplicationSchema;

}
