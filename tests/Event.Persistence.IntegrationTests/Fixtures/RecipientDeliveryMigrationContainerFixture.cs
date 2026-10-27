using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Images;
using Event.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;
using AssuranceStage = Event.Persistence.IntegrationTests.Fixtures.PostgreSqlContainerFixture.AssuranceStage;

namespace Event.Persistence.IntegrationTests.Fixtures;

public sealed class RecipientDeliveryMigrationContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string DefaultPostgresImage = "postgres:18-alpine";
    private const string Postgres16Image = "postgres:16.14@sha256:da8cf245a60506e50a0a8cbb0f39c559ca622d92490605b67fcadc74ca1ea8e4";
    private const string OwnerLabel = "islamu.assurance.owner";
    private const string ResourceLabel = "islamu.assurance.resource";
    private const string FixtureLabel = "islamu.assurance.fixture";
    private readonly string _postgresImage = SelectPostgresImage();
    private readonly string _fixtureNonce = Guid.CreateVersion7().ToString("N");
    private readonly DateTime _constructedAtUtc = DateTime.UtcNow;
    private readonly DateTime _constructorReturnedAtUtc;
    private readonly object _lifecycleLock = new();
    private PostgreSqlContainer? _container;
    private AssuranceOwnershipContext? _assurance;
    private string? _intendedContainerName;
    private Task? _initialization;
    private Task? _disposal;
    private bool _disposalStarted;
    private int _stageSequence;

    public RecipientDeliveryMigrationContainerFixture() => _constructorReturnedAtUtc = DateTime.UtcNow;

    private static string SelectPostgresImage() =>
        Environment.GetEnvironmentVariable("EVENT_PERSISTENCE_TEST_POSTGRES_PROFILE") switch
        {
            null or "" => DefaultPostgresImage,
            "PostgreSql16" => Postgres16Image,
            _ => throw new InvalidOperationException("recipient_migration_postgres_profile_invalid")
        };

    public string ConnectionString
    {
        get
        {
            lock (_lifecycleLock)
            {
                if (_disposalStarted || _initialization?.IsCompletedSuccessfully != true || _container is null)
                    throw new InvalidOperationException("recipient_migration_container_not_initialized");
                return _container.GetConnectionString();
            }
        }
    }

    public Task InitializeAsync()
    {
        TaskCompletionSource completion;
        lock (_lifecycleLock)
        {
            if (_disposalStarted)
                return Task.FromException(new InvalidOperationException("recipient_migration_fixture_disposed"));
            if (_initialization is not null) return _initialization;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = completion.Task;
        }
        _ = CompleteInitializationAsync(completion);
        return completion.Task;
    }

    private async Task CompleteInitializationAsync(TaskCompletionSource completion)
    {
        try
        {
            await InitializeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception failure) { completion.TrySetException(failure); }
    }

    private async Task InitializeCoreAsync()
    {
        _assurance = await AssuranceOwnershipContext.ValidateAssuranceContextAsync(
            Environment.GetEnvironmentVariable("ISLAMU_TEST_RUN_ID"),
            Environment.GetEnvironmentVariable("ISLAMU_TEST_ROOT"), Path.GetTempPath(),
            Environment.GetEnvironmentVariable("DOCKER_HOST"));
        if (_postgresImage == Postgres16Image && _assurance is null)
            throw new InvalidOperationException("test_resource_ownership_invalid");
        if (_assurance is { } owner)
            _intendedContainerName = $"islamu-assurance-postgres-{owner.Owner}-{_fixtureNonce}";
        RecordStage(AssuranceStage.ConstructorReturned);
        RecordStage(AssuranceStage.InitializationEntered);
        RecordStage(AssuranceStage.CreationEntered);
        var builder = new PostgreSqlBuilder(_postgresImage)
            .WithDatabase("recipient_delivery_migration_" + Guid.NewGuid().ToString("N"))
            .WithUsername("postgres")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
            .WithCommand("-c", "max_locks_per_transaction=512");
        if (_assurance is { } ownership)
            builder = builder.WithDockerEndpoint(ownership.DockerEndpoint)
                .WithName(_intendedContainerName!)
                .WithLabel(OwnerLabel, ownership.Owner).WithLabel(ResourceLabel, "postgres")
                .WithLabel(FixtureLabel, _fixtureNonce).WithImagePullPolicy(PullPolicy.Never);
        _container = builder.Build();
        await _container.StartAsync();
        RecordStage(AssuranceStage.ContainerStartReturned);
    }

    private void RecordStage(AssuranceStage stage)
    {
        if (!Enum.IsDefined(stage)) throw new InvalidOperationException("test_resource_witness_invalid");
        if (_assurance is not { } owner) return;
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("test_resource_witness_invalid");
        int sequence = Interlocked.Increment(ref _stageSequence);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            owner = owner.Owner,
            fixtureNonce = _fixtureNonce,
            writerPid = Environment.ProcessId,
            constructedAtUtc = _constructedAtUtc,
            constructorReturnedAtUtc = _constructorReturnedAtUtc,
            recordedAtUtc = DateTime.UtcNow,
            sequence,
            stage = stage.ToString(),
            injectFailure = (bool?)null,
            resource = "postgres",
            intendedContainerName = _intendedContainerName,
            imageReference = _postgresImage,
            imagePullPolicy = "never",
            dockerEndpointSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.DockerEndpoint.OriginalString))),
            intendedLabels = new Dictionary<string, string>
            {
                [OwnerLabel] = owner.Owner,
                [ResourceLabel] = "postgres",
                [FixtureLabel] = _fixtureNonce
            }
        });
        FileStream? stream = null;
        var failures = new List<Exception>();
        try
        {
            stream = new FileStream(Path.Combine(owner.Root, $"postgres-{_fixtureNonce}-{sequence:D3}-{stage}.json"), new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                Options = FileOptions.WriteThrough
            });
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { stream?.Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Test resource witness persistence failed.", failures);
    }

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

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        Task? initialization;
        lock (_lifecycleLock)
        {
            if (_disposal is not null) return new(_disposal);
            _disposalStarted = true;
            initialization = _initialization;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposal = completion.Task;
        }
        _ = CompleteDisposalAsync(initialization, completion);
        return new(completion.Task);
    }

    private async Task CompleteDisposalAsync(Task? initialization, TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync(initialization);
            completion.TrySetResult();
        }
        catch (Exception failure) { completion.TrySetException(failure); }
    }

    private async Task DisposeCoreAsync(Task? initialization)
    {
        var failures = new List<Exception>();
        if (initialization is not null)
        {
            try { await initialization; }
            catch (Exception failure) { failures.Add(failure); }
        }
        try { RecordStage(AssuranceStage.DisposalEntered); }
        catch (Exception failure) { failures.Add(failure); }
        if (_container is { } container)
        {
            try
            {
                await container.StopAsync();
                RecordStage(AssuranceStage.StopReturned);
            }
            catch (Exception failure) { failures.Add(failure); }
            try
            {
                await container.DisposeAsync();
                RecordStage(AssuranceStage.ContainerDisposalReturned);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Recipient migration fixture cleanup failed.", failures);
    }
}
