using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using DotNet.Testcontainers.Images;
using Event.Benchmarks.Api;
using Event.Benchmarks.Configuration;
using Event.Testing;
using Event.Testing.PostgreSql;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
[BenchmarkCategory("PostgreSql18", "TestServer", "OutputCacheDisabled", "NativeLocalAuthority", "HostedServicesDisabled", "CanonicalMigrations", "TenPurposeRoles")]
[InvocationCount(1)]
public class ApiEndpointPostgreSqlBenchmarks : IAsyncDisposable
{
    private const string PostgreSqlImage = "postgres:18-alpine";
    private readonly DateTime _constructedAtUtc = DateTime.UtcNow;
    private readonly DateTime _constructorReturnedAtUtc;
    private readonly string _ownedRunId = Guid.CreateVersion7().ToString("N");
    private readonly string _fixtureNonce = Guid.CreateVersion7().ToString("N");
    private AssuranceOwnershipContext? _assurance;
    private string? _intendedContainerName;
    private string? _knownContainerId;
    private readonly List<FileStream> _heldWitnesses = [];
    private int _stageSequence;
    private PostgreSqlContainer? _container;
    private BenchmarkPostgreSqlAuthority? _authority;
    private PostgreSqlApiBenchmarkHostFactory? _factory;
    private HttpClient? _anonymous;
    private HttpClient? _authenticated;
    private NativeBenchmarkDataset? _dataset;
    private NativeBenchmarkSession? _session;

    public ApiEndpointPostgreSqlBenchmarks() => _constructorReturnedAtUtc = DateTime.UtcNow;

    [ParamsSource(nameof(Scenarios))]
    public ApiBenchmarkScenario Scenario { get; set; } = ApiBenchmarkScenario.Empty;

    public IEnumerable<ApiBenchmarkScenario> Scenarios => ApiBenchmarkScenario.ReadScenarios;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            _assurance = await AssuranceOwnershipContext.ValidateAssuranceContextAsync(
                Environment.GetEnvironmentVariable("ISLAMU_TEST_RUN_ID"),
                Environment.GetEnvironmentVariable("ISLAMU_TEST_ROOT"), Path.GetTempPath(),
                Environment.GetEnvironmentVariable("DOCKER_HOST"))
                ?? throw new InvalidOperationException("benchmark_native_ownership_required");
            _intendedContainerName = $"islamu-assurance-postgres-{_assurance.Owner}-{_fixtureNonce}";
            RecordStage(AssuranceStage.ConstructorReturned);
            RecordStage(AssuranceStage.InitializationEntered);
            var login = BenchmarkDatabaseConfiguration.ReadPostgreSqlMigratorLogin();
            RecordStage(AssuranceStage.CreationEntered);
            _container = new PostgreSqlBuilder(PostgreSqlImage)
                .WithDockerEndpoint(_assurance.DockerEndpoint)
                .WithName(_intendedContainerName)
                .WithLabel("islamu.assurance.owner", _assurance.Owner)
                .WithLabel("islamu.assurance.resource", "postgres")
                .WithLabel("islamu.assurance.fixture", _fixtureNonce)
                .WithLabel("islamu.assurance.kind", "benchmark")
                .WithLabel("islamu.assurance.run", _ownedRunId)
                .WithImagePullPolicy(PullPolicy.Never)
                .WithDatabase("explore_benchmark_api")
                .WithUsername(login.Username)
                .WithPassword(login.Password)
                .WithLogger(NullLogger.Instance)
                .Build();
            Console.WriteLine($"benchmark_owned_container: run_id={_ownedRunId} profile=native_ten_purpose_roles");
            await _container.StartAsync(deadline.Token);
            string actualId = _container.Id;
            if (actualId.Length != 64 || !actualId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
                throw new InvalidOperationException("benchmark_native_container_id_invalid");
            _knownContainerId = actualId;
            RecordStage(AssuranceStage.ContainerStartReturned);
            Console.WriteLine($"benchmark_owned_container_started: run_id={_ownedRunId} container_id={_container.Id}");
            _authority = BenchmarkDatabaseConfiguration.BindOwnedPostgreSql(_container.GetConnectionString());
            await PostgresTestRoleBootstrap.ProvisionAsync(_container.GetConnectionString(), _authority.Enrollment,
                _authority.CreationLogins(), deadline.Token);
            RecordStage(AssuranceStage.PurposeLoginsReturned);
            _factory = new PostgreSqlApiBenchmarkHostFactory(_authority);
            await _factory.InitializeDatabaseAsync(deadline.Token);
            RecordStage(AssuranceStage.MigrationsReturned);
            _anonymous = _factory.CreateAnonymousClient();
            _authenticated = _factory.CreateAnonymousClient();
            _anonymous.Timeout = TimeSpan.FromSeconds(10);
            _authenticated.Timeout = TimeSpan.FromSeconds(10);
            _anonymous.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            _authenticated.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            DateTimeOffset seedStarted = DateTimeOffset.UtcNow;
            _dataset = await BenchmarkApiSeedData.SeedNativeAsync(_authenticated, _factory, _ownedRunId, deadline.Token);
            _anonymous.DefaultRequestHeaders.Add("X-Tenant-Slug", _dataset.TenantSlug);
            _session = await BenchmarkApiSeedData.LoginNativeAsync(_authenticated, _factory, deadline.Token);
            await BenchmarkApiSeedData.ValidateNativeAsync(_anonymous, _authenticated, _dataset, _session, deadline.Token);
            Console.WriteLine($"benchmark_dataset: profile=native_postgresql events=24 seed_clock_start={seedStarted:O} seed_clock_end={DateTimeOffset.UtcNow:O} authentication=local_replaced token_values_retained=false");
        }
        catch (Exception setupFailure)
        {
            var failures = await CleanupCoreAsync();
            failures.Insert(0, setupFailure);
            throw new NativeBenchmarkFailure(FailurePhase.Setup, failures);
        }
    }

    [IterationSetup]
    public void RefreshOriginalHumanSession()
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _session = BenchmarkApiSeedData.LoginNativeAsync(
                _authenticated ?? throw new InvalidOperationException("benchmark_native_client_missing"),
                _factory ?? throw new InvalidOperationException("benchmark_native_host_missing"), deadline.Token)
                .GetAwaiter().GetResult();
            if (_session.UserId != _dataset?.UserId)
                throw new InvalidOperationException("benchmark_native_subject_changed");
        }
        catch (Exception failure)
        {
            throw new NativeBenchmarkFailure(FailurePhase.Session, [failure]);
        }
    }

    [Benchmark(Description = "GET + JSON body validation: native PostgreSQL 18 / TestServer / output cache disabled")]
    public async Task<ApiBenchmarkResult> Get_Endpoint_PostgreSql()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Scenario.Path);
            if (Scenario.UseMinimalResponse)
                request.Headers.Add("Prefer", "return=minimal");
            if (Scenario.RequiresAuthentication)
            {
                var session = _session ?? throw new InvalidOperationException("benchmark_native_session_missing");
                if (session.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(15))
                    throw new InvalidOperationException("benchmark_native_session_window_expired");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            }
            var client = (Scenario.RequiresAuthentication ? _authenticated : _anonymous)
                ?? throw new InvalidOperationException("benchmark_native_client_missing");
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("benchmark_native_measured_status_invalid");
            var content = await response.Content.ReadAsByteArrayAsync();
            RequireMeasuredContract(response, content);
            return new ApiBenchmarkResult(Scenario.Name, (int)response.StatusCode, content.Length);
        }
        catch (Exception failure)
        {
            throw new NativeBenchmarkFailure(FailurePhase.Request, [failure]);
        }
    }

    private void RequireMeasuredContract(HttpResponseMessage response, byte[] content)
    {
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (content.Length == 0 || content.Length > 4 * 1024 * 1024 ||
            mediaType is null || !(mediaType == "application/json" || mediaType.EndsWith("+json", StringComparison.Ordinal)))
            throw new InvalidOperationException("benchmark_native_measured_body_invalid");
        using var body = JsonDocument.Parse(content);
        JsonValueKind expected = Scenario.Name == "lookup-category-types" ? JsonValueKind.Array : JsonValueKind.Object;
        if (body.RootElement.ValueKind != expected)
            throw new InvalidOperationException("benchmark_native_measured_contract_invalid");
        bool present = Scenario.Name switch
        {
            "lookup-category-types" => body.RootElement.GetArrayLength() > 0,
            "event-list-minimal" or "event-list-hal" => body.RootElement.TryGetProperty("snapshotCount", out var count)
                && count.ValueKind == JsonValueKind.Number,
            "my-events-minimal" => body.RootElement.TryGetProperty("totalCount", out var count)
                && count.ValueKind == JsonValueKind.Number,
            "system-onboarding-status" => body.RootElement.TryGetProperty("requiresOnboarding", out var required)
                && required.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "event-creation-context" => body.RootElement.TryGetProperty("canCreate", out var allowed)
                && allowed.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => false
        };
        if (!present)
            throw new InvalidOperationException("benchmark_native_measured_contract_invalid");
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        var failures = await CleanupCoreAsync();
        if (failures.Count != 0)
            throw new NativeBenchmarkFailure(FailurePhase.Cleanup, failures);
    }

    public ValueTask DisposeAsync() => new(GlobalCleanup());

    private async Task<List<Exception>> CleanupCoreAsync()
    {
        var failures = new List<Exception>();
        try { RecordStage(AssuranceStage.DisposalEntered); }
        catch (Exception failure) { failures.Add(failure); }
        var anonymous = Interlocked.Exchange(ref _anonymous, null);
        var authenticated = Interlocked.Exchange(ref _authenticated, null);
        var factory = _factory;
        var container = _container;
        _session = null;
        try { anonymous?.Dispose(); }
        catch (Exception failure) { failures.Add(failure); }
        try { authenticated?.Dispose(); }
        catch (Exception failure) { failures.Add(failure); }
        if (factory is not null)
        {
            try
            {
                await factory.DisposeAsync();
                _factory = null;
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (container is not null)
        {
            try
            {
                await container.DisposeAsync();
                _container = null;
                RecordStage(AssuranceStage.ContainerDisposalReturned);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (_factory is null && _container is null)
        {
            try
            {
                _authority?.Dispose();
                _authority = null;
            }
            catch (Exception failure) { failures.Add(failure); }
            if (_authority is null)
                for (int index = _heldWitnesses.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        _heldWitnesses[index].Dispose();
                        _heldWitnesses.RemoveAt(index);
                    }
                    catch (Exception failure) { failures.Add(failure); }
                }
        }
        return failures;
    }

    private enum AssuranceStage
    {
        ConstructorReturned, InitializationEntered, CreationEntered, ContainerStartReturned,
        PurposeLoginsReturned, MigrationsReturned, DisposalEntered, ContainerDisposalReturned
    }

    /// <summary>Flushes private lifecycle intent outside measured requests; records never establish creator ownership.</summary>
    private void RecordStage(AssuranceStage stage)
    {
        if (!Enum.IsDefined(stage)) throw new InvalidOperationException("test_resource_witness_invalid");
        if (_assurance is not { } owner) return;
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("test_resource_witness_invalid");
        int sequence = Interlocked.Increment(ref _stageSequence);
        string file = $"postgres-{_fixtureNonce}-{sequence:D3}-{stage}.json";
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
            containerId = _knownContainerId,
            imageReference = PostgreSqlImage,
            imagePullPolicy = "never",
            dockerEndpointSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.DockerEndpoint.OriginalString))),
            intendedLabels = new Dictionary<string, string>
            {
                ["islamu.assurance.owner"] = owner.Owner,
                ["islamu.assurance.resource"] = "postgres",
                ["islamu.assurance.fixture"] = _fixtureNonce,
                ["islamu.assurance.kind"] = "benchmark",
                ["islamu.assurance.run"] = _ownedRunId
            }
        });
        FileStream? stream = null;
        var failures = new List<Exception>();
        try
        {
            stream = new FileStream(Path.Combine(owner.Root, file), new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                Options = FileOptions.WriteThrough
            });
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            _heldWitnesses.Add(stream);
            stream = null;
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { stream?.Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0)
            throw new AggregateException("Test resource witness persistence failed.", failures);
    }

    private enum FailurePhase { Setup, Session, Request, Cleanup }

    /// <summary>Retains every original cause privately without exposing credentials to BenchmarkDotNet artifacts.</summary>
    private sealed class NativeBenchmarkFailure(FailurePhase phase, IReadOnlyList<Exception> causes)
        : Exception($"benchmark_native_failed:phase={phase}:cause_count={causes.Count}")
    {
        private readonly IReadOnlyList<Exception> _causes = causes;

        public override string ToString()
        {
            GC.KeepAlive(_causes);
            return Message;
        }
    }
}
