using System.Collections.Concurrent;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Services;

/// <summary>
/// HTTP client factory that supports credential rotation without connection leaks.
/// Uses atomic swap pattern with configurable grace period for in-flight requests.
/// </summary>
/// <remarks>
/// Key features:
/// - Listens to IOptionsMonitor for credential changes
/// - Atomic swap: creates new client, schedules old for disposal after grace period
/// - Thread-safe client access via ConcurrentDictionary
/// - Graceful drain allows in-flight requests to complete
/// </remarks>
public sealed class RotationAwareHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, ClientEntry> _clients = new();
    private readonly IOptionsMonitor<HttpClientCredentialOptions> _credentialOptions;
    private readonly IOptionsMonitor<RotationOptions> _rotationOptions;
    private readonly ILogger<RotationAwareHttpClientFactory> _logger;
    private readonly Func<HttpClient, CancellationToken, Task<bool>> _validateCandidate;
    private readonly string _replicaId;
    private readonly IDisposable? _credentialChangeListener;
    private readonly SemaphoreSlim _rotationSemaphore;
    private readonly object _disposeLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the RotationAwareHttpClientFactory.
    /// </summary>
    public RotationAwareHttpClientFactory(
        IOptionsMonitor<HttpClientCredentialOptions> credentialOptions,
        IOptionsMonitor<RotationOptions> rotationOptions,
        ILogger<RotationAwareHttpClientFactory> logger,
        Func<HttpClient, CancellationToken, Task<bool>>? validateCandidate = null,
        string? replicaId = null)
    {
        _credentialOptions = credentialOptions ?? throw new ArgumentNullException(nameof(credentialOptions));
        _rotationOptions = rotationOptions ?? throw new ArgumentNullException(nameof(rotationOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validateCandidate = validateCandidate ?? ((_, _) => Task.FromResult(true));
        _replicaId = string.IsNullOrWhiteSpace(replicaId)
            ? Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName
            : replicaId;
        _rotationSemaphore = new SemaphoreSlim(rotationOptions.CurrentValue.MaxConcurrentRotations);

        // Subscribe to credential changes
        _credentialChangeListener = _credentialOptions.OnChange(OnCredentialsChanged);

        _logger.LogDebug("RotationAwareHttpClientFactory initialized");
    }

    /// <summary>
    /// Creates or retrieves an HttpClient for the specified name.
    /// </summary>
    /// <param name="name">The logical name of the client.</param>
    /// <returns>An HttpClient configured for the specified name.</returns>
    public HttpClient CreateClient(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = _clients.GetOrAdd(name, key => CreateClientEntry(key));
        return entry.Client;
    }

    /// <summary>
    /// Gets the number of active clients.
    /// </summary>
    public int ActiveClientCount => _clients.Count;

    /// <summary>
    /// Gets whether a client with the specified name exists.
    /// </summary>
    public bool HasClient(string name) => _clients.ContainsKey(name);

    private ClientEntry CreateClientEntry(
        string name,
        HttpClientCredentialOptions? candidateOptions = null)
    {
        var client = CreateHttpClientInternal(name, candidateOptions);
        _logger.LogDebug("secret_rotation_client_created");
        return new ClientEntry(client, DateTime.UtcNow);
    }

    private HttpClient CreateHttpClientInternal(
        string name,
        HttpClientCredentialOptions? candidateOptions)
    {
        var client = new HttpClient();
        var credentials = candidateOptions ?? _credentialOptions.CurrentValue;

        if (credentials.Clients.TryGetValue(name, out var clientCred))
        {
            ApplyCredentials(client, clientCred);
        }

        return client;
    }

    private void ApplyCredentials(HttpClient client, HttpClientCredential credential)
    {
        // Set base address if configured
        if (!string.IsNullOrEmpty(credential.BaseAddress))
        {
            client.BaseAddress = new Uri(credential.BaseAddress);
        }

        // Set timeout if configured
        if (credential.Timeout.HasValue)
        {
            client.Timeout = credential.Timeout.Value;
        }

        // Add bearer token if configured
        if (!string.IsNullOrEmpty(credential.BearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.BearerToken);
        }

        // Add API key if configured
        if (!string.IsNullOrEmpty(credential.ApiKey))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", credential.ApiKey);
        }

        // Add custom headers
        foreach (var header in credential.Headers)
        {
            if (!string.IsNullOrEmpty(header.Value))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        _logger.LogDebug("secret_rotation_candidate_configured");
    }

    private void OnCredentialsChanged(HttpClientCredentialOptions newCredentials, string? name)
    {
        if (!_rotationOptions.CurrentValue.Enabled)
        {
            _logger.LogDebug("secret_rotation_disabled");
            return;
        }

        _logger.LogInformation("secret_rotation_candidate_detected");

        // Rotate all clients that have new credentials
        foreach (var clientName in newCredentials.Clients.Keys)
        {
            if (_clients.ContainsKey(clientName))
            {
                _ = RotateClientAsync(clientName, newCredentials);
            }
        }
    }

    private async Task<SecretRotationLocalAcknowledgement> RotateClientAsync(
        string name,
        HttpClientCredentialOptions? candidateOptions = null,
        Guid? requestedAttemptId = null,
        CancellationToken cancellationToken = default)
    {
        var attemptId = requestedAttemptId is { } value && value != Guid.Empty
            ? value
            : Guid.CreateVersion7();
        try
        {
            // Limit concurrent rotations
            if (!await _rotationSemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
            {
                _logger.LogWarning("secret_rotation_capacity_exhausted");
                return Acknowledge(attemptId, SecretRotationLocalStatus.Failed);
            }

            try
            {
                return await RotateClientInternalAsync(
                    name,
                    candidateOptions,
                    attemptId,
                    cancellationToken);
            }
            finally
            {
                _rotationSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Rotation boundary returns a bounded local failure.
        catch (Exception)
#pragma warning restore CA1031
        {
            _logger.LogError("secret_rotation_failed");
            return Acknowledge(attemptId, SecretRotationLocalStatus.Failed);
        }
    }

    private async Task<SecretRotationLocalAcknowledgement> RotateClientInternalAsync(
        string name,
        HttpClientCredentialOptions? candidateOptions,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        var rotationOptions = _rotationOptions.CurrentValue;

        if (rotationOptions.LogRotationEvents)
        {
            _logger.LogInformation("secret_rotation_validating");
        }

        // Create new client with updated credentials
        var newEntry = CreateClientEntry(name, candidateOptions);
        if (!await _validateCandidate(newEntry.Client, cancellationToken).ConfigureAwait(false))
        {
            newEntry.Client.Dispose();
            _logger.LogWarning("secret_rotation_candidate_rejected");
            return Acknowledge(attemptId, SecretRotationLocalStatus.Rejected);
        }

        // Atomic swap - replace old entry with new one
        if (_clients.TryGetValue(name, out var oldEntry))
        {
            _clients[name] = newEntry;

            if (rotationOptions.LogRotationEvents)
            {
                _logger.LogDebug("secret_rotation_activated");
            }

            // Schedule disposal of old client after grace period
            _ = DisposeAfterGracePeriodAsync(oldEntry.Client, rotationOptions.GracePeriod);
        }
        else
        {
            // No existing client, just add the new one
            _clients[name] = newEntry;
        }

        return Acknowledge(attemptId, SecretRotationLocalStatus.Activated);
    }

    private async Task DisposeAfterGracePeriodAsync(HttpClient client, TimeSpan gracePeriod)
    {
        try
        {
            await Task.Delay(gracePeriod);
            client.Dispose();

            if (_rotationOptions.CurrentValue.LogRotationEvents)
            {
                _logger.LogDebug(
                    "secret_rotation_previous_client_disposed");
            }
        }
        catch (Exception)
        {
            _logger.LogError("secret_rotation_disposal_failed");
        }
    }

    /// <summary>
    /// Forces rotation of a specific client.
    /// Useful for testing or manual rotation triggers.
    /// </summary>
    public async Task<SecretRotationLocalAcknowledgement> ForceRotateAsync(
        string name,
        Guid? attemptId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_clients.ContainsKey(name))
        {
            throw new ArgumentException($"No client with name '{name}' exists", nameof(name));
        }

        return await RotateClientAsync(
            name,
            candidateOptions: null,
            requestedAttemptId: attemptId,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Forces rotation of all clients.
    /// </summary>
    public async Task<IReadOnlyList<SecretRotationLocalAcknowledgement>> ForceRotateAllAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tasks = _clients.Keys.Select(name =>
            RotateClientAsync(
                name,
                candidateOptions: null,
                requestedAttemptId: null,
                cancellationToken: cancellationToken));
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Disposes the factory and all managed clients.
    /// </summary>
    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _logger.LogDebug("Disposing RotationAwareHttpClientFactory");

        // Unsubscribe from changes
        _credentialChangeListener?.Dispose();

        // Dispose all clients
        foreach (var entry in _clients.Values)
        {
            try
            {
                entry.Client.Dispose();
            }
            catch (Exception)
            {
                _logger.LogWarning("secret_rotation_disposal_failed");
            }
        }

        _clients.Clear();
        _rotationSemaphore.Dispose();

        _logger.LogDebug("RotationAwareHttpClientFactory disposed");
    }

    /// <summary>
    /// Internal record to track client creation time.
    /// </summary>
    private sealed record ClientEntry(HttpClient Client, DateTime CreatedAt);

    private SecretRotationLocalAcknowledgement Acknowledge(
        Guid attemptId,
        SecretRotationLocalStatus status) =>
        new(attemptId, _replicaId, "http", status, DateTimeOffset.UtcNow);
}
