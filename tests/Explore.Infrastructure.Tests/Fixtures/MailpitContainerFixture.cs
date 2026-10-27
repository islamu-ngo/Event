using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Event.Testing;
using TUnit.Core.Interfaces;

namespace Explore.Infrastructure.Tests.Fixtures;

public sealed class MailpitContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string MailpitImage = "axllent/mailpit:v1.30.0";
    private const ushort SmtpContainerPort = 1025;
    private const ushort HttpContainerPort = 8025;
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

    private IContainer? _container;

    public MailpitContainerFixture() => _constructorReturnedAtUtc = DateTime.UtcNow;

    public string SmtpHost => _container?.Hostname
        ?? throw new InvalidOperationException("Mailpit container not started.");

    public int SmtpPort => _container?.GetMappedPublicPort(SmtpContainerPort)
        ?? throw new InvalidOperationException("Mailpit container not started.");

    private string ApiBaseUrl => _container is null
        ? throw new InvalidOperationException("Mailpit container not started.")
        : $"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpContainerPort)}";

    public async Task InitializeAsync()
    {
        try
        {
            _assurance = await AssuranceOwnershipContext.ValidateAssuranceContextAsync(
                Environment.GetEnvironmentVariable("ISLAMU_TEST_RUN_ID"),
                Environment.GetEnvironmentVariable("ISLAMU_TEST_ROOT"), Path.GetTempPath(),
                Environment.GetEnvironmentVariable("DOCKER_HOST"));
            if (_assurance is { } owner)
                _intendedContainerName = $"islamu-assurance-mailpit-{owner.Owner}-{_fixtureNonce}";
            RecordStage(AssuranceStage.ConstructorReturned);
            RecordStage(AssuranceStage.InitializationEntered);
            RecordStage(AssuranceStage.CreationEntered);
            var builder = new ContainerBuilder()
                .WithImage(MailpitImage)
                .WithPortBinding(SmtpContainerPort, assignRandomHostPort: true)
                .WithPortBinding(HttpContainerPort, assignRandomHostPort: true);
            if (_assurance is { } selected)
                builder = builder.WithDockerEndpoint(selected.DockerEndpoint)
                    .WithName(_intendedContainerName!)
                    .WithLabel(OwnerLabel, selected.Owner).WithLabel(ResourceLabel, "mailpit")
                    .WithLabel(FixtureLabel, _fixtureNonce).WithImagePullPolicy(PullPolicy.Never);
            _container = builder.Build();
            using var startupCts = new CancellationTokenSource(StartupTimeout);
            await _container.StartAsync(startupCts.Token);
            RecordStage(AssuranceStage.ContainerStartReturned);
            await WaitForApiAsync(startupCts.Token);
            RecordStage(AssuranceStage.ReadinessReturned);
        }
        catch (Exception primary)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanup)
            {
                throw new AggregateException("Mailpit initialization and owned cleanup failed.", primary, cleanup);
            }
            throw;
        }
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
            throw new AggregateException("Mailpit owned disposal failed.", failures);
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
            resource = "mailpit",
            intendedContainerName = _intendedContainerName,
            imageReference = MailpitImage,
            imagePullPolicy = "never",
            dockerEndpointSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.DockerEndpoint.OriginalString))),
            intendedLabels = new Dictionary<string, string>
            {
                [OwnerLabel] = owner.Owner,
                [ResourceLabel] = "mailpit",
                [FixtureLabel] = _fixtureNonce
            }
        });
        Exception? operationFailure = null;
        try
        {
            using var stream = new FileStream(Path.Combine(owner.Root, $"mailpit-{_fixtureNonce}-{sequence:D3}-{stage}.json"),
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
                throw new AggregateException("Mailpit private witness write and disposal failed.", operationFailure, failure);
            throw;
        }
        if (operationFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(operationFailure).Throw();
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
