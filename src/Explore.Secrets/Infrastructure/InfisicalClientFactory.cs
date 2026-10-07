using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Configuration;
using Infisical.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Explore.Secrets.Infrastructure;

/// <summary>
/// Thread-safe factory that lazily constructs, authenticates, and caches a single
/// HTTP boundary for the lifetime of the application. Returns
/// <c>null</c> when the Infisical integration is not configured, so callers can report
/// a clean "not configured" state. Authentication failures throw for typed translation by the source.
/// </summary>
public sealed class InfisicalClientFactory : IInfisicalClientFactory, IAsyncDisposable
{
    private readonly ILogger<InfisicalClientFactory> _logger;
    private readonly InfisicalOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private HttpClient? _client;
    private DateTimeOffset _authenticatedUntil;
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
        cancellationToken.ThrowIfCancellationRequested();

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

        if (_initialized && _facade is not null && DateTimeOffset.UtcNow < _authenticatedUntil)
        {
            return _facade;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized && _facade is not null && DateTimeOffset.UtcNow < _authenticatedUntil)
            {
                return _facade;
            }

            if (_client is null)
            {
                var uri = new Uri(_options.Url!, UriKind.Absolute);
                if (uri.Scheme is not ("https" or "http")
                    || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new InfisicalOperationException(InfisicalOperationFailureKind.Invalid);
                _client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = uri
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/universal-auth/login")
            {
                Content = JsonContent.Create(new
                {
                    clientId = _options.ClientId,
                    clientSecret = _options.ClientSecret
                })
            };
            using JsonDocument login = await SendAsync(_client, request, cancellationToken).ConfigureAwait(false);
            if (login.RootElement.ValueKind != JsonValueKind.Object
                || !login.RootElement.TryGetProperty("accessToken", out JsonElement token)
                || token.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(token.GetString())
                || !login.RootElement.TryGetProperty("expiresIn", out JsonElement expires)
                || !expires.TryGetInt32(out int seconds) || seconds <= 0)
                throw new InfisicalOperationException(InfisicalOperationFailureKind.Invalid);

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetString());
            _authenticatedUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
            _facade ??= new InfisicalClientFacade(_client, _options.ProjectId!,
                async token => { _ = await GetClientAsync(token).ConfigureAwait(false); });
            _initialized = true;

            _logger.LogInformation("secret_provider_authenticated");

            return _facade;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            // Reset so a later call can retry.
            _initialized = false;
            _logger.LogError("secret_provider_authentication_failed");
            throw Translate(exception);
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
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _initLock.Dispose();

        _client?.Dispose();

        _client = null;
        _facade = null;
        return ValueTask.CompletedTask;
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is InfisicalOperationException or HttpRequestException or InfisicalException
            or JsonException or ArgumentException or FormatException or InvalidOperationException
            or IOException or OperationCanceledException;

    private static InfisicalOperationException Translate(Exception exception) => exception switch
    {
        InfisicalOperationException bounded => bounded,
        HttpRequestException { StatusCode: { } status } => FromStatus(status, definiteRejection: false),
        ArgumentException or FormatException or JsonException or InvalidOperationException =>
            new(InfisicalOperationFailureKind.Invalid),
        _ => new(InfisicalOperationFailureKind.Unavailable)
    };

    private static InfisicalOperationException FromStatus(
        HttpStatusCode status, bool definiteRejection = true) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new(InfisicalOperationFailureKind.Unauthorized, definiteRejection),
        HttpStatusCode.NotFound => new(InfisicalOperationFailureKind.Missing, definiteRejection),
        HttpStatusCode.BadRequest => new(InfisicalOperationFailureKind.Invalid, definiteRejection),
        _ => new(InfisicalOperationFailureKind.Unavailable)
    };

    // The pinned SDK discards HTTP status and has no transport hook. Observe status
    // before reading a response body; never retain provider error payloads.
    private static async Task<JsonDocument> SendAsync(
        HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw FromStatus(response.StatusCode);
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw Translate(exception);
        }
    }

    /// <summary>
    /// Thin adapter from the library-agnostic <see cref="IInfisicalClient"/> contract to the
    /// status-preserving HTTP boundary. Keeps provider protocol details out of Application.
    /// </summary>
    internal sealed class InfisicalClientFacade : IInfisicalClient
    {
        private static readonly UTF8Encoding StrictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        private readonly HttpClient _client;
        private readonly string _projectId;
        private readonly Func<CancellationToken, Task>? _authenticateAsync;
        private readonly Func<UpdateSecretOptions, CancellationToken, Task<bool>> _writeSecretAsync;

        public InfisicalClientFacade(
            HttpClient client, string projectId, Func<CancellationToken, Task> authenticateAsync)
        {
            _client = client;
            _projectId = projectId;
            _authenticateAsync = authenticateAsync;
            _writeSecretAsync = async (options, cancellationToken) =>
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Patch, $"/api/v3/secrets/raw/{Uri.EscapeDataString(options.SecretName)}")
                {
                    Content = JsonContent.Create(new
                    {
                        secretName = options.SecretName,
                        workspaceId = options.ProjectId,
                        environment = options.EnvironmentSlug,
                        secretPath = options.SecretPath,
                        secretValue = options.NewSecretValue
                    })
                };
                using JsonDocument result = await SendAsync(client, request, cancellationToken).ConfigureAwait(false);
                if (result.RootElement.ValueKind != JsonValueKind.Object
                    || !result.RootElement.TryGetProperty("secret", out JsonElement secret)
                    || secret.ValueKind != JsonValueKind.Object)
                    throw new InfisicalOperationException(InfisicalOperationFailureKind.Invalid);
                return true;
            };
        }

        internal InfisicalClientFacade(
            string projectId,
            ILogger logger,
            Func<UpdateSecretOptions, Task<bool>> writeSecretAsync)
        {
            _client = null!;
            _projectId = projectId;
            _writeSecretAsync = (options, cancellationToken) => writeSecretAsync(options).WaitAsync(cancellationToken);
        }

        public async Task<SecretProviderValue?> GetSecretAsync(
            string environment,
            string folderPath,
            string secretName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(environment) || string.IsNullOrWhiteSpace(folderPath)
                || string.IsNullOrWhiteSpace(secretName))
                throw new InfisicalOperationException(InfisicalOperationFailureKind.Invalid);
            if (_authenticateAsync is not null)
                await _authenticateAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/v3/secrets/raw?workspaceId={Uri.EscapeDataString(_projectId)}" +
                $"&environment={Uri.EscapeDataString(environment)}&secretPath={Uri.EscapeDataString(folderPath)}" +
                "&recursive=false&expandSecretReferences=true&viewSecretValue=true&include_imports=false" +
                "&SetSecretsAsEnvironmentVariables=false");
            using JsonDocument result = await SendAsync(_client, request, cancellationToken).ConfigureAwait(false);
            if (result.RootElement.ValueKind != JsonValueKind.Object
                || !result.RootElement.TryGetProperty("secrets", out JsonElement secrets)
                || secrets.ValueKind != JsonValueKind.Array)
                throw new InfisicalOperationException(InfisicalOperationFailureKind.Invalid);
            foreach (JsonElement secret in secrets.EnumerateArray())
            {
                if (secret.ValueKind != JsonValueKind.Object
                    || !secret.TryGetProperty("secretKey", out JsonElement key)
                    || key.ValueKind != JsonValueKind.String)
                    throw new InfisicalOperationException(InfisicalOperationFailureKind.Invalid);
                if (string.Equals(key.GetString(), secretName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!secret.TryGetProperty("secretValue", out JsonElement value)
                        || value.ValueKind != JsonValueKind.String
                        || !secret.TryGetProperty("id", out JsonElement id)
                        || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())
                        || !secret.TryGetProperty("version", out JsonElement version)
                        || version.ValueKind != JsonValueKind.Number || !version.TryGetInt64(out long number))
                        throw new InfisicalOperationException(InfisicalOperationFailureKind.Invalid);
                    return new SecretProviderValue(value.GetString()!, $"{id.GetString()}:{number}");
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
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(environment) || string.IsNullOrWhiteSpace(folderPath)
                || string.IsNullOrWhiteSpace(secretName))
                throw new InfisicalOperationException(InfisicalOperationFailureKind.Invalid);

            try
            {
                string value = StrictUtf8.GetString(secretValue.Span);
                if (_authenticateAsync is not null)
                    await _authenticateAsync(cancellationToken).ConfigureAwait(false);
                return await _writeSecretAsync(new UpdateSecretOptions
                {
                    ProjectId = _projectId,
                    EnvironmentSlug = environment,
                    SecretPath = folderPath,
                    SecretName = secretName,
                    NewSecretValue = value
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception exception) when (IsProviderFailure(exception))
            {
                throw Translate(exception);
            }
        }
    }
}
