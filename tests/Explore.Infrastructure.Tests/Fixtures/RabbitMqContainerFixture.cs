using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Images;
using Event.Testing;
using Testcontainers.RabbitMq;
using TUnit.Core.Interfaces;

namespace Explore.Infrastructure.Tests.Fixtures;

public sealed class RabbitMqContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string RabbitMqImage = "rabbitmq:4-management";
    private const string Username = "rabbitmq";
    private const string Password = "rabbitmq";
    private const ushort ManagementContainerPort = 15672;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private const string OwnerLabel = "islamu.assurance.owner";
    private const string ResourceLabel = "islamu.assurance.resource";
    private const string FixtureLabel = "islamu.assurance.fixture";
    private readonly string _fixtureNonce = Guid.CreateVersion7().ToString("N");
    private readonly DateTime _constructedAtUtc = DateTime.UtcNow;
    private readonly DateTime _constructorReturnedAtUtc;
    private readonly object _disposalLock = new();
    private AssuranceOwnershipContext? _assurance;
    private string? _intendedContainerName;
    private int _stageSequence;
    private Task? _disposal;

    private RabbitMqContainer? _container;

    public RabbitMqContainerFixture() => _constructorReturnedAtUtc = DateTime.UtcNow;

    public string AmqpConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("RabbitMQ container not started.");

    public string Host => _container?.Hostname
        ?? throw new InvalidOperationException("RabbitMQ container not started.");

    public int AmqpPort => _container?.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort)
        ?? throw new InvalidOperationException("RabbitMQ container not started.");

    public string ManagementBaseUrl => _container is null
        ? throw new InvalidOperationException("RabbitMQ container not started.")
        : $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ManagementContainerPort)}";

    public async Task InitializeAsync()
    {
        try
        {
            _assurance = await AssuranceOwnershipContext.ValidateAssuranceContextAsync(
                Environment.GetEnvironmentVariable("ISLAMU_TEST_RUN_ID"),
                Environment.GetEnvironmentVariable("ISLAMU_TEST_ROOT"), Path.GetTempPath(),
                Environment.GetEnvironmentVariable("DOCKER_HOST"));
            if (_assurance is { } owner)
                _intendedContainerName = $"islamu-assurance-rabbitmq-{owner.Owner}-{_fixtureNonce}";
            RecordStage(AssuranceStage.ConstructorReturned);
            RecordStage(AssuranceStage.InitializationEntered);
            RecordStage(AssuranceStage.CreationEntered);
            var builder = new RabbitMqBuilder(RabbitMqImage)
                .WithUsername(Username)
                .WithPassword(Password)
                .WithPortBinding(ManagementContainerPort, assignRandomHostPort: true);
            if (_assurance is { } selected)
                builder = builder.WithDockerEndpoint(selected.DockerEndpoint)
                    .WithName(_intendedContainerName!)
                    .WithLabel(OwnerLabel, selected.Owner).WithLabel(ResourceLabel, "rabbitmq")
                    .WithLabel(FixtureLabel, _fixtureNonce).WithImagePullPolicy(PullPolicy.Never);
            _container = builder.Build();
            using var startupCts = new CancellationTokenSource(StartupTimeout);
            await _container.StartAsync(startupCts.Token);
            RecordStage(AssuranceStage.ContainerStartReturned);
            await WaitForManagementApiAsync(startupCts.Token);
            RecordStage(AssuranceStage.ReadinessReturned);
        }
        catch (Exception primary)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanup)
            {
                throw new AggregateException("RabbitMQ initialization and owned cleanup failed.", primary, cleanup);
            }
            throw;
        }
    }

    public async Task<RabbitMqManagementOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = CreateManagementHttpClient();
        var overview = await client.GetFromJsonAsync<RabbitMqManagementOverview>(
            "/api/overview",
            cancellationToken);
        return overview ?? throw new InvalidOperationException("RabbitMQ management overview was not returned.");
    }

    public async Task<RabbitMqExchangeDetail> GetExchangeAsync(
        string exchangeName,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateManagementHttpClient();
        var exchange = await client.GetFromJsonAsync<RabbitMqExchangeDetail>(
            $"/api/exchanges/{EncodeVhost()}/{Uri.EscapeDataString(exchangeName)}",
            cancellationToken);
        return exchange ?? throw new InvalidOperationException("RabbitMQ exchange detail was not returned.");
    }

    public async Task<RabbitMqQueueDetail> GetQueueAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateManagementHttpClient();
        var queue = await client.GetFromJsonAsync<RabbitMqQueueDetail>(
            $"/api/queues/{EncodeVhost()}/{Uri.EscapeDataString(queueName)}",
            cancellationToken);
        return queue ?? throw new InvalidOperationException("RabbitMQ queue detail was not returned.");
    }

    public async Task<IReadOnlyList<RabbitMqBindingDetail>> GetQueueBindingsAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateManagementHttpClient();
        var bindings = await client.GetFromJsonAsync<IReadOnlyList<RabbitMqBindingDetail>>(
            $"/api/queues/{EncodeVhost()}/{Uri.EscapeDataString(queueName)}/bindings",
            cancellationToken);
        return bindings ?? [];
    }

    public async Task<IReadOnlyList<RabbitMqQueueMessage>> GetQueueMessagesAsync(
        string queueName,
        int count,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateManagementHttpClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/queues/{EncodeVhost()}/{Uri.EscapeDataString(queueName)}/get",
            new RabbitMqGetMessagesRequest(count),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var messages = await response.Content.ReadFromJsonAsync<IReadOnlyList<RabbitMqQueueMessage>>(
            cancellationToken);
        return messages ?? [];
    }

    public async Task<bool> PublishStringAsync(
        string exchangeName,
        string routingKey,
        string payload,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateManagementHttpClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/exchanges/{EncodeVhost()}/{Uri.EscapeDataString(exchangeName)}/publish",
            new RabbitMqPublishStringRequest(routingKey, payload),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RabbitMqPublishStringResponse>(
            cancellationToken);
        return result?.Routed ?? false;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposalLock)
            return new ValueTask(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        try { RecordStage(AssuranceStage.DisposalEntered); }
        catch (Exception failure) { failures.Add(failure); }
        bool disposed = false;
        try
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
                disposed = true;
            }
        }
        catch (Exception failure) { failures.Add(failure); }
        if (disposed)
        {
            try { RecordStage(AssuranceStage.ContainerDisposalReturned); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0)
            throw new AggregateException("RabbitMQ owned disposal failed.", failures);
    }

    private enum AssuranceStage
    {
        ConstructorReturned, InitializationEntered, CreationEntered, ContainerStartReturned,
        ReadinessReturned, DisposalEntered, ContainerDisposalReturned
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
            resource = "rabbitmq",
            intendedContainerName = _intendedContainerName,
            imageReference = RabbitMqImage,
            imagePullPolicy = "never",
            dockerEndpointSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.DockerEndpoint.OriginalString))),
            intendedLabels = new Dictionary<string, string>
            {
                [OwnerLabel] = owner.Owner,
                [ResourceLabel] = "rabbitmq",
                [FixtureLabel] = _fixtureNonce
            }
        });
        Exception? operationFailure = null;
        try
        {
            using var stream = new FileStream(Path.Combine(owner.Root, $"rabbitmq-{_fixtureNonce}-{sequence:D3}-{stage}.json"),
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    Options = FileOptions.WriteThrough
                });
            try
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            catch (Exception failure) { operationFailure = failure; }
        }
        catch (Exception failure)
        {
            if (operationFailure is not null)
                throw new AggregateException("RabbitMQ private witness write and disposal failed.", operationFailure, failure);
            throw;
        }
        if (operationFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(operationFailure).Throw();
    }

    private HttpClient CreateManagementHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(ManagementBaseUrl)
        };
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Username}:{Password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return client;
    }

    private static string EncodeVhost() => Uri.EscapeDataString("/");

    private async Task WaitForManagementApiAsync(CancellationToken cancellationToken)
    {
        using var client = CreateManagementHttpClient();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync("/api/overview", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        throw new TimeoutException("RabbitMQ management API did not become ready before startup timeout.");
    }

    public sealed class RabbitMqManagementOverview
    {
        [JsonPropertyName("rabbitmq_version")]
        public string RabbitMqVersion { get; init; } = string.Empty;

        [JsonPropertyName("object_totals")]
        public RabbitMqObjectTotals ObjectTotals { get; init; } = new();

        [JsonPropertyName("queue_totals")]
        public RabbitMqQueueTotals QueueTotals { get; init; } = new();
    }

    public sealed class RabbitMqObjectTotals
    {
        [JsonPropertyName("queues")]
        public int Queues { get; init; }

        [JsonPropertyName("exchanges")]
        public int Exchanges { get; init; }
    }

    public sealed class RabbitMqQueueTotals
    {
        [JsonPropertyName("messages")]
        public int Messages { get; init; }
    }

    public sealed class RabbitMqExchangeDetail
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("durable")]
        public bool Durable { get; init; }
    }

    public sealed class RabbitMqQueueDetail
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("durable")]
        public bool Durable { get; init; }

        [JsonPropertyName("messages")]
        public int Messages { get; init; }

        [JsonPropertyName("messages_ready")]
        public int MessagesReady { get; init; }

        [JsonPropertyName("messages_unacknowledged")]
        public int MessagesUnacknowledged { get; init; }

        [JsonPropertyName("arguments")]
        public IReadOnlyDictionary<string, JsonElement> Arguments { get; init; } =
            new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }

    public sealed class RabbitMqBindingDetail
    {
        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("destination")]
        public string Destination { get; init; } = string.Empty;

        [JsonPropertyName("destination_type")]
        public string DestinationType { get; init; } = string.Empty;

        [JsonPropertyName("routing_key")]
        public string RoutingKey { get; init; } = string.Empty;
    }

    private sealed class RabbitMqGetMessagesRequest(int count)
    {
        [JsonPropertyName("count")]
        public int Count { get; } = count;

        [JsonPropertyName("ackmode")]
        public string AckMode { get; } = "ack_requeue_false";

        [JsonPropertyName("encoding")]
        public string Encoding { get; } = "auto";

        [JsonPropertyName("truncate")]
        public int Truncate { get; } = 50_000;
    }

    public sealed class RabbitMqQueueMessage
    {
        [JsonPropertyName("payload")]
        public string Payload { get; init; } = string.Empty;
    }

    private sealed class RabbitMqPublishStringRequest(string routingKey, string payload)
    {
        [JsonPropertyName("properties")]
        public IReadOnlyDictionary<string, object> Properties { get; } =
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["delivery_mode"] = 2
            };

        [JsonPropertyName("routing_key")]
        public string RoutingKey { get; } = routingKey;

        [JsonPropertyName("payload")]
        public string Payload { get; } = payload;

        [JsonPropertyName("payload_encoding")]
        public string PayloadEncoding { get; } = "string";
    }

    private sealed class RabbitMqPublishStringResponse
    {
        [JsonPropertyName("routed")]
        public bool Routed { get; init; }
    }
}
