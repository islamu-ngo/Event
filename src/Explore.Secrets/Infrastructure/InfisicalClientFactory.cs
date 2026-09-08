using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Configuration;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace Explore.Secrets.Infrastructure;

/// <summary>
/// Thread-safe factory that lazily constructs, authenticates, and caches a single
/// <see cref="InfisicalClient"/> instance for the lifetime of the application. Returns
/// <c>null</c> when the Infisical integration is not configured, so callers can report
/// a clean "not configured" state. Authentication failures throw for typed translation by the source.
/// </summary>
public sealed class InfisicalClientFactory : IInfisicalClientFactory, IAsyncDisposable
{
    private readonly ILogger<InfisicalClientFactory> _logger;
    private readonly InfisicalOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private InfisicalClient? _client;
    private IInfisicalClient? _facade;
    private bool _initialized;
    private bool _notConfiguredLogged;
    private bool _disposed;

    public InfisicalClientFactory(
        ILogger<InfisicalClientFactory> logger,
        IOptions<SecretProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _options = options.Value.Infisical;
    }

    /// <inheritdoc />
    public async Task<IInfisicalClient?> GetClientAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsConfigured())
        {
            if (!_notConfiguredLogged)
            {
                _notConfiguredLogged = true;
                _logger.LogInformation(
                    "secret_provider_unconfigured");
            }

            return null;
        }

        if (_initialized && _facade is not null)
        {
            return _facade;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized && _facade is not null)
            {
                return _facade;
            }

            var settings = new InfisicalSdkSettingsBuilder()
                .WithHostUri(_options.Url!)
                .Build();

            var client = new InfisicalClient(settings);

            await client.Auth()
                .UniversalAuth()
                .LoginAsync(_options.ClientId!, _options.ClientSecret!)
                .ConfigureAwait(false);

            _client = client;
            _facade = new InfisicalClientFacade(client, _options.ProjectId!, _logger);
            _initialized = true;

            _logger.LogInformation("secret_provider_authenticated");

            return _facade;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Reset so a later call can retry.
            _client = null;
            _facade = null;
            _initialized = false;
            _logger.LogError("secret_provider_authentication_failed");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_options.Url)
            && !string.IsNullOrWhiteSpace(_options.Environment)
            && !string.IsNullOrWhiteSpace(_options.ClientId)
            && !string.IsNullOrWhiteSpace(_options.ClientSecret)
            && !string.IsNullOrWhiteSpace(_options.ProjectId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initLock.Dispose();

        if (_client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (_client is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _client = null;
        _facade = null;
    }

    /// <summary>
    /// Thin adapter from the library-agnostic <see cref="IInfisicalClient"/> contract to the
    /// concrete Infisical SDK <see cref="InfisicalClient"/>. Keeps the SDK out of Application.
    /// </summary>
    internal sealed class InfisicalClientFacade : IInfisicalClient
    {
        private static readonly UTF8Encoding StrictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        private readonly InfisicalClient _client;
        private readonly string _projectId;
        private readonly ILogger _logger;
        private readonly Func<UpdateSecretOptions, Task<bool>> _writeSecretAsync;

        public InfisicalClientFacade(InfisicalClient client, string projectId, ILogger logger)
        {
            _client = client;
            _projectId = projectId;
            _logger = logger;
            _writeSecretAsync = async options =>
                await client.Secrets().UpdateAsync(options).ConfigureAwait(false) is not null;
        }

        internal InfisicalClientFacade(
            string projectId,
            ILogger logger,
            Func<UpdateSecretOptions, Task<bool>> writeSecretAsync)
        {
            _client = null!;
            _projectId = projectId;
            _logger = logger;
            _writeSecretAsync = writeSecretAsync;
        }

        public async Task<string?> GetSecretAsync(
            string environment,
            string folderPath,
            string secretName,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environment);
            ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

            cancellationToken.ThrowIfCancellationRequested();

            var options = new ListSecretsOptions
            {
                ProjectId = _projectId,
                EnvironmentSlug = environment,
                SecretPath = folderPath,
                Recursive = false,
                ExpandSecretReferences = true,
                ViewSecretValue = true,
            };

            var secrets = await _client.Secrets().ListAsync(options).ConfigureAwait(false);
            if (secrets is null)
            {
                return null;
            }

            foreach (var secret in secrets)
            {
                if (string.Equals(secret.SecretKey, secretName, StringComparison.OrdinalIgnoreCase))
                {
                    return secret.SecretValue;
                }
            }

            return null;
        }

        public async Task<bool> WriteSecretAsync(
            string environment,
            string folderPath,
            string secretName,
            ReadOnlyMemory<byte> secretValue,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environment);
            ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
            cancellationToken.ThrowIfCancellationRequested();

            string value = StrictUtf8.GetString(secretValue.Span);
            bool written = await _writeSecretAsync(new UpdateSecretOptions
            {
                ProjectId = _projectId,
                EnvironmentSlug = environment,
                SecretPath = folderPath,
                SecretName = secretName,
                NewSecretValue = value
            }).ConfigureAwait(false);
            return written;
        }
    }
}
