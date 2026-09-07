// ABOUTME: Factory for isolated in-memory SQLite database connections and connection strings.
// ABOUTME: Employs unique URI filenames (mode=memory&cache=shared) to guarantee parallel test run isolation.

using Microsoft.Data.Sqlite;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>
/// Provides isolated in-memory SQLite connection strings and open connection instances.
/// Using URI file names prevents parallel test classes from sharing the same unnamed in-memory database.
/// </summary>
public static class SqliteTestDatabaseFactory
{
    /// <summary>
    /// Creates a unique isolated in-memory SQLite connection string using URI mode.
    /// Format: Data Source=file:memdb_{guid}?mode=memory&cache=shared
    /// </summary>
    public static string CreateIsolatedMemoryConnectionString()
    {
        return $"Data Source=file:memdb_{Guid.NewGuid():N}?mode=memory&cache=shared";
    }

    /// <summary>
    /// Creates and opens a unique isolated in-memory SQLite connection.
    /// The returned connection must remain open for the duration of the test.
    /// </summary>
    public static async Task<SqliteConnection> CreateOpenIsolatedConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(CreateIsolatedMemoryConnectionString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
