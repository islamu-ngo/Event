using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private RabbitMqContainer? _container;

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
        _container = new RabbitMqBuilder(RabbitMqImage)
            .WithUsername(Username)
            .WithPassword(Password)
            .WithPortBinding(ManagementContainerPort, assignRandomHostPort: true)
            .Build();

        using var startupCts = new CancellationTokenSource(StartupTimeout);
        await _container.StartAsync(startupCts.Token);
        await WaitForManagementApiAsync(startupCts.Token);
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

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
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
