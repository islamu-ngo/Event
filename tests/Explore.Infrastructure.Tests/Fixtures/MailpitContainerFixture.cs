using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace Explore.Infrastructure.Tests.Fixtures;

public sealed class MailpitContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string MailpitImage = "axllent/mailpit:v1.30.0";
    private const ushort SmtpContainerPort = 1025;
    private const ushort HttpContainerPort = 8025;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private IContainer? _container;

    public string SmtpHost => _container?.Hostname
        ?? throw new InvalidOperationException("Mailpit container not started.");

    public int SmtpPort => _container?.GetMappedPublicPort(SmtpContainerPort)
        ?? throw new InvalidOperationException("Mailpit container not started.");

    private string ApiBaseUrl => _container is null
        ? throw new InvalidOperationException("Mailpit container not started.")
        : $"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpContainerPort)}";

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage(MailpitImage)
            .WithPortBinding(SmtpContainerPort, assignRandomHostPort: true)
            .WithPortBinding(HttpContainerPort, assignRandomHostPort: true)
            .Build();

        using var startupCts = new CancellationTokenSource(StartupTimeout);
        await _container.StartAsync(startupCts.Token);
        await WaitForApiAsync(startupCts.Token);
    }

    public async Task ClearMessagesAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient();
        using var response = await client.DeleteAsync("/api/v1/messages", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<MailpitMessageSummary> WaitForMessageAsync(
        Func<MailpitMessageSummary, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var messages = await GetMessagesAsync(cancellationToken);
            var match = messages.FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        var finalMessages = await GetMessagesAsync(cancellationToken);
        throw new TimeoutException(
            $"Mailpit did not receive a matching message before timeout. MessageCount={finalMessages.Count}.");
    }

    public async Task<MailpitMessageDetail> GetMessageAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient();
        var messageId = string.IsNullOrWhiteSpace(id) ? "latest" : Uri.EscapeDataString(id);
        var message = await client.GetFromJsonAsync<MailpitMessageDetail>(
            $"/api/v1/message/{messageId}",
            cancellationToken);
        return message ?? throw new InvalidOperationException($"Mailpit message '{messageId}' was not returned.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private HttpClient CreateHttpClient() => new()
    {
        BaseAddress = new Uri(ApiBaseUrl)
    };

    public async Task<IReadOnlyList<MailpitMessageSummary>> GetMessagesAsync(
        CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient();
        var response = await client.GetFromJsonAsync<MailpitMessagesResponse>(
            "/api/v1/messages",
            cancellationToken);
        return response?.Messages ?? [];
    }

    private async Task WaitForApiAsync(CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync("/api/v1/messages", cancellationToken);
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

        throw new TimeoutException("Mailpit HTTP API did not become ready before startup timeout.");
    }

    private sealed class MailpitMessagesResponse
    {
        public IReadOnlyList<MailpitMessageSummary> Messages { get; init; } = [];
    }

    public sealed class MailpitMessageSummary
    {
        [JsonPropertyName("ID")]
        public string Id { get; init; } = string.Empty;

        public string Subject { get; init; } = string.Empty;

        public MailpitAddress From { get; init; } = new();

        public IReadOnlyList<MailpitAddress> To { get; init; } = [];
    }

    public sealed class MailpitMessageDetail
    {
        [JsonPropertyName("Text")]
        public string Text { get; init; } = string.Empty;

        [JsonPropertyName("HTML")]
        public string Html { get; init; } = string.Empty;
    }

    public sealed class MailpitAddress
    {
        public string Address { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;
    }
}
