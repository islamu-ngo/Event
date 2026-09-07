using System.Globalization;
using Explore.API.Scheduling;
using Explore.Secrets.Database;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Exceptions;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// PostgreSQL is the Tier 2/3 default database, so it is the engine whose scheduler DDL and clustering
/// behaviour actually need proving. Both proofs need a live engine, and both are cheap once a container
/// exists, so they share one.
/// <para>
/// Container startup is deliberately non-fatal. An environment without a Docker-compatible runtime would
/// otherwise turn this fixture into a suite-wide failure that means nothing more than "no Docker", and a
/// red suite that contributors learn to ignore is worse than a skip that states its reason.
/// </para>
/// </summary>
public sealed class QuartzPostgreSqlSchedulerFixture : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>Matches the platform default so the proof exercises the prefix deployments actually run.</summary>
    public const string TablePrefix = "QRTZ_";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("quartz_scheduler_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>
    /// PostgreSQL's <c>CREATE TABLE IF NOT EXISTS</c> is not safe against a concurrent creator — two sessions
    /// racing it collide on the system catalog's unique index. TUnit runs these classes in parallel over one
    /// shared database, so schema application is serialized here rather than left to each test.
    /// </summary>
    private readonly SemaphoreSlim _schemaGate = new(1, 1);

    private bool _started;
    private bool _schemaApplied;

    /// <summary>Non-null when the container could not be started; carries the reason for the skip message.</summary>
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            _started = true;
        }
        catch (Exception exception)
        {
            // The exception type is the useful signal (DockerUnavailableException and friends); the message
            // carries host paths and daemon detail that add noise to a skip reason.
            UnavailableReason = exception.GetType().Name;
        }
    }

    /// <summary>Throws a TUnit skip rather than a failure when no container runtime answered.</summary>
    public void SkipWhenContainerRuntimeUnavailable()
    {
        if (_started)
        {
            return;
        }

        throw new SkipTestException(
            $"No Docker-compatible container runtime is available for the PostgreSQL scheduler proof ({UnavailableReason ?? "unknown"}).");
    }

    /// <summary>Connection string for the running container; only valid after a passing availability check.</summary>
    public string ConnectionString => _started
        ? _container.GetConnectionString()
        : throw new InvalidOperationException("The PostgreSQL scheduler container is not running.");

    /// <summary>Applies the shipped DDL once per fixture; safe to call from every test.</summary>
    public async Task EnsureSchedulerSchemaAsync()
    {
        await _schemaGate.WaitAsync();
        try
        {
            if (_schemaApplied)
            {
                return;
            }

            await ExecuteSchedulerSchemaAsync();
            _schemaApplied = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    /// <summary>Re-runs the DDL deliberately, for the proof that a second startup is a no-op.</summary>
    public async Task ReapplySchedulerSchemaAsync()
    {
        await _schemaGate.WaitAsync();
        try
        {
            await ExecuteSchedulerSchemaAsync();
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    /// <summary>
    /// Executes the embedded provider DDL through a raw connection rather than the API host, so the proof
    /// covers the shipped script itself instead of an EF Core-mediated approximation of it.
    /// </summary>
    private async Task ExecuteSchedulerSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var statement in QuartzSchemaInitializer.BuildStatements(
                     PrimaryDatabaseProvider.PostgreSql,
                     TablePrefix))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<IReadOnlyList<string>> QuerySchedulerTableNamesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT upper(table_name)
            FROM information_schema.tables
            WHERE table_schema = 'public' AND upper(table_name) LIKE @prefix || '%'
            """;
        command.Parameters.AddWithValue("prefix", TablePrefix.ToUpperInvariant());

        List<string> names = [];
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async Task<long> CountRowsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        _schemaGate.Dispose();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
