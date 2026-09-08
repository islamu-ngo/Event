namespace ISLAMU.Event.SetupAssistant.SetupLive;

using System.Net.Http.Headers;
using Generated = Explore.Blazor.Client.Clients;
using Wire = ISLAMU.Wire.Contracts.SetupLive;

public sealed record SetupLiveEnrollmentSnapshot(
    Uri TargetBaseAddress,
    Guid TenantId,
    Guid EnrollmentId,
    Wire.SetupEnrollmentState State,
    long Generation,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Wire.SetupEnrollmentScope> Scopes);

public sealed record SetupLiveSecretBindingReadiness(
    string BindingKey,
    Wire.SetupSecretBindingReadinessState State,
    bool CanWrite);

public sealed record SetupLiveSecretBindingOperation(
    Guid OperationId,
    Wire.SetupSecretBindingOperationState State,
    Wire.SetupSecretBindingOperationOutcome Outcome,
    long EnrollmentGeneration,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SettledAt);

public sealed class SetupLiveAuthorityUnavailableException()
    : InvalidOperationException("Setup live authority is unavailable.");

public sealed class SetupLiveAffordanceUnavailableException(string relation)
    : InvalidOperationException($"Setup live affordance is unavailable: {relation}.");

public sealed class SetupLiveContractViolationException()
    : InvalidOperationException("Setup live response violated the public contract.");

public sealed class SetupLiveAuthenticationUnavailableException()
    : InvalidOperationException("Setup live authentication is unavailable.");

[System.Diagnostics.DebuggerDisplay("SetupLiveAccessToken(<redacted>)")]
public sealed class SetupLiveAccessToken
{
    private SetupLiveAccessToken(string value, DateTimeOffset expiresAt)
    {
        Value = value;
        ExpiresAt = expiresAt;
    }

    internal string Value { get; }

    public DateTimeOffset ExpiresAt { get; }

    public static SetupLiveAccessToken Create(
        string value,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 8_192 || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '.' and not '_' and not '~'
                    and not '+' and not '/' and not '='))
        {
            throw new ArgumentException(
                "Setup live access token is invalid.",
                nameof(value));
        }

        return new SetupLiveAccessToken(value, expiresAt);
    }

    public override string ToString() => "SetupLiveAccessToken(<redacted>)";
}

public delegate ValueTask<SetupLiveAccessToken?> SetupLiveAccessTokenProvider(
    CancellationToken cancellationToken);

public sealed class SetupLiveAdapter : IDisposable
{
    private readonly SetupLiveAccessTokenProvider _accessTokenProvider;
    private readonly Generated.ISetup_LiveClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly HttpClient? _ownedHttpClient;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, string?> _enrollmentAffordances =
        new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _readableOperations = [];
    private readonly HashSet<string> _writableBindings =
        new(StringComparer.Ordinal);
    private Wire.SetupEnrollmentCapability? _capability;
    private Guid _enrollmentId;
    private DateTimeOffset _expiresAt;
    private long _generation;
    private Wire.SetupEnrollmentScope[] _scopes = [];
    private bool _disposed;

    public SetupLiveAdapter(
        Uri targetBaseAddress,
        Guid tenantId,
        SetupLiveAccessTokenProvider accessTokenProvider,
        TimeProvider? timeProvider = null)
    {
        TargetBaseAddress = NormalizeTarget(targetBaseAddress);
        ValidateTenant(tenantId);
        ArgumentNullException.ThrowIfNull(accessTokenProvider);

        TenantId = tenantId;
        _accessTokenProvider = accessTokenProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ownedHttpClient = CreateOwnedHttpClient();
        _ownedHttpClient.BaseAddress = TargetBaseAddress;
        _httpClient = _ownedHttpClient;
        _client = new Generated.Setup_LiveClient(_httpClient);
    }

    internal SetupLiveAdapter(
        Uri targetBaseAddress,
        Guid tenantId,
        HttpClient httpClient,
        SetupLiveAccessTokenProvider accessTokenProvider,
        TimeProvider? timeProvider = null)
    {
        TargetBaseAddress = NormalizeTarget(targetBaseAddress);
        ValidateTenant(tenantId);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(accessTokenProvider);
        if (httpClient.BaseAddress is null)
            httpClient.BaseAddress = TargetBaseAddress;
        else if (NormalizeTarget(httpClient.BaseAddress) != TargetBaseAddress)
            throw new ArgumentException(
                "HTTP client base address must match the Setup live target.",
                nameof(httpClient));

        TenantId = tenantId;
        _accessTokenProvider = accessTokenProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _httpClient = httpClient;
        _client = new Generated.Setup_LiveClient(_httpClient);
    }

    public Uri TargetBaseAddress { get; }

    public Guid TenantId { get; }

    public bool HasAuthority
    {
        get
        {
            _gate.Wait();
            try
            {
                return IsAuthorityAvailable();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public Task<SetupLiveEnrollmentSnapshot> EnrollAsync(
        Wire.SetupClientChallenge challenge,
        IReadOnlyList<Wire.SetupEnrollmentScope> requestedScopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        Wire.SetupEnrollmentScope[] scopes = SnapshotScopes(requestedScopes);
        return ExecuteLockedAsync(async () =>
        {
            if (_disposed)
                throw new SetupLiveAuthorityUnavailableException();
            ClearState();
            var request = new Generated.CreateSetupTargetEnrollmentRequest
            {
                ClientChallenge = challenge.ToWireValue(),
                RequestedScopes = scopes.Select(ToGeneratedScope).ToArray()
            };
            Generated.SwaggerResponse<Generated.HalResourceOfSetupTargetEnrollmentData>
                response = await SendAuthenticatedAsync(
                    () => _client.CreateSetupTargetEnrollmentAsync(
                        TenantId,
                        NewIdempotencyKey(),
                        request,
                        cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            SetupLiveEnrollmentSnapshot snapshot = MapEnrollment(
                response.Result,
                scopes);
            if (snapshot.Generation != 1)
                throw ContractViolation();

            if (response.StatusCode == 200)
            {
                if (response.Result.Issuance
                        != Generated.SetupEnrollmentIssuance.Already_issued
                    || HeaderValues(response.Headers,
                            Wire.SetupLiveContractMetadata.CapabilityHeader).Length != 0)
                {
                    throw ContractViolation();
                }

                ClearState();
                return snapshot;
            }

            if (response.StatusCode != 201
                || response.Result.Issuance != Generated.SetupEnrollmentIssuance.Issued
                || snapshot.State != Wire.SetupEnrollmentState.Active)
            {
                throw ContractViolation();
            }

            Wire.SetupEnrollmentCapability capability = RequireCapability(
                response.Headers);
            ApplyAuthority(snapshot, capability, response.Result._links);
            return snapshot;
        }, cancellationToken);
    }

    public Task<SetupLiveEnrollmentSnapshot> RefreshAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(async () =>
        {
            string capability = RequireAuthority();
            RequireEnrollmentAffordance(
                Wire.SetupLiveHalRelations.Self,
                HttpMethod.Get);
            Generated.HalResourceOfSetupTargetEnrollmentData response =
                await SendAuthenticatedAsync(
                    () => _client.GetSetupTargetEnrollmentAsync(
                        TenantId,
                        _enrollmentId,
                        capability,
                        cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            SetupLiveEnrollmentSnapshot snapshot = MapCurrentEnrollment(response);
            if (snapshot.State != Wire.SetupEnrollmentState.Active)
            {
                ClearState();
                return snapshot;
            }
            if (snapshot.Generation != _generation)
                throw ContractViolation();

            _expiresAt = snapshot.ExpiresAt;
            CaptureEnrollmentAffordances(response._links);
            if (_expiresAt <= _timeProvider.GetUtcNow())
                ClearState();
            return snapshot;
        }, cancellationToken);

    public Task<SetupLiveEnrollmentSnapshot> RevokeAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(async () =>
        {
            string capability = RequireAuthority();
            RequireEnrollmentAffordance(
                Wire.SetupLiveHalRelations.Revoke,
                HttpMethod.Delete);
            Generated.HalResourceOfSetupTargetEnrollmentData response =
                await SendAuthenticatedAsync(
                    () => _client.RevokeSetupTargetEnrollmentAsync(
                        TenantId,
                        _enrollmentId,
                        capability,
                        NewIdempotencyKey(),
                        cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            SetupLiveEnrollmentSnapshot snapshot = MapCurrentEnrollment(response);
            if (snapshot.State != Wire.SetupEnrollmentState.Revoked
                || snapshot.Generation != _generation)
                throw ContractViolation();
            ClearState();
            return snapshot;
        }, cancellationToken);

    public Task<SetupLiveEnrollmentSnapshot> RotateCapabilityAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(async () =>
        {
            string oldCapability = RequireAuthority();
            long oldGeneration = _generation;
            DateTimeOffset oldExpiry = _expiresAt;
            RequireEnrollmentAffordance(
                Wire.SetupLiveHalRelations.RotateCapability,
                HttpMethod.Post);
            Generated.SwaggerResponse<Generated.HalResourceOfSetupTargetEnrollmentData>
                response = await SendAuthenticatedAsync(
                    () => _client.RotateSetupTargetEnrollmentCapabilityAsync(
                        TenantId,
                        _enrollmentId,
                        oldCapability,
                        NewIdempotencyKey(),
                        cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            SetupLiveEnrollmentSnapshot snapshot = MapCurrentEnrollment(response.Result);
            if (response.StatusCode != 200
                || response.Result.Issuance != Generated.SetupEnrollmentIssuance.Issued
                || snapshot.State != Wire.SetupEnrollmentState.Active
                || oldGeneration == long.MaxValue
                || snapshot.Generation != oldGeneration + 1
                || snapshot.ExpiresAt <= oldExpiry)
            {
                throw ContractViolation();
            }

            Wire.SetupEnrollmentCapability capability = RequireCapability(
                response.Headers);
            if (string.Equals(
                    capability.ToHeaderValue(),
                    oldCapability,
                    StringComparison.Ordinal))
            {
                throw ContractViolation();
            }

            ApplyAuthority(snapshot, capability, response.Result._links);
            return snapshot;
        }, cancellationToken);

    public Task<IReadOnlyList<SetupLiveSecretBindingReadiness>>
        GetSecretBindingReadinessAsync(
            CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync<IReadOnlyList<SetupLiveSecretBindingReadiness>>(
            async () =>
            {
                string capability = RequireAuthority();
                RequireEnrollmentAffordance(
                    Wire.SetupLiveHalRelations.SecretBindingReadiness,
                    HttpMethod.Get);
                Generated.HalResourceOfSetupSecretBindingReadinessDocument response =
                    await SendAuthenticatedAsync(
                        () => _client.GetSetupSecretBindingReadinessAsync(
                            TenantId,
                            _enrollmentId,
                            capability,
                            cancellationToken: cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                Generated.HalResourceOfSetupSecretBindingReadinessItem[] items =
                    response._embedded?.Items?.ToArray()
                    ?? throw ContractViolation();
                if (items.Any(item => item is null
                        || string.IsNullOrWhiteSpace(item.BindingKey))
                    || items.Select(item => item.BindingKey)
                        .Distinct(StringComparer.Ordinal).Count() != items.Length)
                {
                    throw ContractViolation();
                }

                _writableBindings.Clear();
                var readiness = new SetupLiveSecretBindingReadiness[items.Length];
                for (int index = 0; index < items.Length; index++)
                {
                    Generated.HalResourceOfSetupSecretBindingReadinessItem item =
                        items[index];
                    Wire.SetupSecretBindingReadinessState state =
                        ToWireReadinessState(item.State);
                    bool canWrite = state == Wire.SetupSecretBindingReadinessState.Ready
                        && HasRelation(
                            item._links,
                            Wire.SetupLiveHalRelations.WriteSecretBinding,
                            HttpMethod.Put);
                    if (canWrite)
                        _writableBindings.Add(item.BindingKey);
                    readiness[index] = new SetupLiveSecretBindingReadiness(
                        item.BindingKey,
                        state,
                        canWrite);
                }

                return Array.AsReadOnly(readiness);
            },
            cancellationToken);

    public Task<SetupLiveSecretBindingOperation> WriteSecretBindingAsync(
        string bindingKey,
        Stream secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingKey);
        ArgumentNullException.ThrowIfNull(secret);
        return ExecuteLockedAsync(async () =>
        {
            string capability = RequireAuthority();
            if (!_writableBindings.Contains(bindingKey))
            {
                throw new SetupLiveAffordanceUnavailableException(
                    Wire.SetupLiveHalRelations.WriteSecretBinding);
            }
            if (!secret.CanRead)
                throw new ArgumentException("Secret stream must be readable.", nameof(secret));

            Generated.HalResourceOfSetupSecretBindingOperationData response =
                await SendAuthenticatedAsync(
                    () => _client.WriteSetupSecretBindingAsync(
                        TenantId,
                        _enrollmentId,
                        bindingKey,
                        capability,
                        NewIdempotencyKey(),
                        secret,
                        cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            SetupLiveSecretBindingOperation operation = MapOperation(response);
            CaptureOperationAffordance(operation.OperationId, response._links);
            return operation;
        }, cancellationToken);
    }

    public Task<SetupLiveSecretBindingOperation> GetSecretBindingOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(async () =>
        {
            string capability = RequireAuthority();
            if (!_readableOperations.Contains(operationId))
            {
                throw new SetupLiveAffordanceUnavailableException(
                    Wire.SetupLiveHalRelations.SecretBindingOperation);
            }

            Generated.HalResourceOfSetupSecretBindingOperationData response =
                await SendAuthenticatedAsync(
                    () => _client.GetSetupSecretBindingOperationAsync(
                        TenantId,
                        _enrollmentId,
                        operationId,
                        capability,
                        cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            SetupLiveSecretBindingOperation operation = MapOperation(response);
            if (operation.OperationId != operationId)
                throw ContractViolation();
            CaptureOperationAffordance(operation.OperationId, response._links);
            return operation;
        }, cancellationToken);

    public void ClearAuthority()
    {
        _gate.Wait();
        try
        {
            ClearState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            ClearState();
            _ownedHttpClient?.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static SocketsHttpHandler CreateTransport() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false
    };

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "HttpClient owns and disposes the transport handler.")]
    private static HttpClient CreateOwnedHttpClient()
    {
        SocketsHttpHandler transport = CreateTransport();
        try
        {
            return new HttpClient(transport, disposeHandler: true);
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    private async Task<T> ExecuteLockedAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> SendAuthenticatedAsync<T>(
        Func<Task<T>> send,
        CancellationToken cancellationToken)
    {
        SetupLiveAccessToken accessToken;
        try
        {
            accessToken = await _accessTokenProvider(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new SetupLiveAuthenticationUnavailableException();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SetupLiveAuthenticationUnavailableException)
        {
            ClearState();
            throw;
        }
        catch
        {
            ClearState();
            throw new SetupLiveAuthenticationUnavailableException();
        }

        if (accessToken.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            ClearState();
            throw new SetupLiveAuthenticationUnavailableException();
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken.Value);
            return await send().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClearState();
            throw;
        }
        catch (OperationCanceledException)
        {
            ClearState();
            throw new SetupLiveAuthorityUnavailableException();
        }
        catch (Generated.ApiException)
        {
            ClearState();
            throw new SetupLiveAuthorityUnavailableException();
        }
        catch (HttpRequestException)
        {
            ClearState();
            throw new SetupLiveAuthorityUnavailableException();
        }
        finally
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    private SetupLiveEnrollmentSnapshot MapCurrentEnrollment(
        Generated.HalResourceOfSetupTargetEnrollmentData response)
    {
        SetupLiveEnrollmentSnapshot snapshot = MapEnrollment(response, _scopes);
        if (snapshot.EnrollmentId != _enrollmentId)
            throw ContractViolation();
        return snapshot;
    }

    private SetupLiveEnrollmentSnapshot MapEnrollment(
        Generated.HalResourceOfSetupTargetEnrollmentData response,
        Wire.SetupEnrollmentScope[] expectedScopes)
    {
        if (response is null
            || response.EnrollmentId == Guid.Empty
            || response.Generation <= 0
            || response.ExpiresAt == default
            || response.Scopes is null)
        {
            throw ContractViolation();
        }

        Wire.SetupEnrollmentScope[] scopes = response.Scopes
            .Select(ToWireScope)
            .ToArray();
        if (scopes.Length != expectedScopes.Length
            || scopes.Distinct().Count() != scopes.Length
            || !scopes.ToHashSet().SetEquals(expectedScopes))
        {
            throw ContractViolation();
        }

        return new SetupLiveEnrollmentSnapshot(
            TargetBaseAddress,
            TenantId,
            response.EnrollmentId,
            ToWireEnrollmentState(response.State),
            response.Generation,
            response.ExpiresAt,
            Array.AsReadOnly(scopes));
    }

    private SetupLiveSecretBindingOperation MapOperation(
        Generated.HalResourceOfSetupSecretBindingOperationData response)
    {
        if (response is null
            || response.OperationId == Guid.Empty
            || response.EnrollmentGeneration != _generation
            || response.CreatedAt == default)
        {
            throw ContractViolation();
        }

        return new SetupLiveSecretBindingOperation(
            response.OperationId,
            ToWireOperationState(response.State),
            ToWireOperationOutcome(response.Outcome),
            response.EnrollmentGeneration,
            response.CreatedAt,
            response.SettledAt);
    }

    private void ApplyAuthority(
        SetupLiveEnrollmentSnapshot snapshot,
        Wire.SetupEnrollmentCapability capability,
        IDictionary<string, Generated.HalLink>? links)
    {
        _capability = capability;
        _enrollmentId = snapshot.EnrollmentId;
        _generation = snapshot.Generation;
        _expiresAt = snapshot.ExpiresAt;
        _scopes = snapshot.Scopes.ToArray();
        _writableBindings.Clear();
        _readableOperations.Clear();
        CaptureEnrollmentAffordances(links);
        if (!IsAuthorityAvailable())
            throw new SetupLiveAuthorityUnavailableException();
    }

    private void CaptureEnrollmentAffordances(
        IDictionary<string, Generated.HalLink>? links)
    {
        _enrollmentAffordances.Clear();
        _writableBindings.Clear();
        _readableOperations.Clear();
        if (links is null)
            return;
        foreach ((string relation, Generated.HalLink link) in links)
        {
            if (link is not null && !string.IsNullOrWhiteSpace(link.Href))
                _enrollmentAffordances[relation] = link.Method;
        }
    }

    private void CaptureOperationAffordance(
        Guid operationId,
        IDictionary<string, Generated.HalLink>? links)
    {
        if (HasRelation(
                links,
                Wire.SetupLiveHalRelations.SecretBindingOperation,
                HttpMethod.Get))
        {
            _readableOperations.Add(operationId);
        }
        else
        {
            _readableOperations.Remove(operationId);
        }
    }

    private string RequireAuthority()
    {
        if (!IsAuthorityAvailable())
            throw new SetupLiveAuthorityUnavailableException();
        return _capability!.ToHeaderValue();
    }

    private bool IsAuthorityAvailable()
    {
        if (_disposed
            || _capability is null
            || _expiresAt <= _timeProvider.GetUtcNow())
        {
            ClearState();
            return false;
        }

        return true;
    }

    private void RequireEnrollmentAffordance(
        string relation,
        HttpMethod expectedMethod)
    {
        if (!_enrollmentAffordances.TryGetValue(relation, out string? method)
            || !MethodMatches(method, expectedMethod))
        {
            throw new SetupLiveAffordanceUnavailableException(relation);
        }
    }

    private SetupLiveContractViolationException ContractViolation()
    {
        ClearState();
        return new SetupLiveContractViolationException();
    }

    private void ClearState()
    {
        _capability = null;
        _enrollmentId = Guid.Empty;
        _generation = 0;
        _expiresAt = default;
        _scopes = [];
        _enrollmentAffordances.Clear();
        _writableBindings.Clear();
        _readableOperations.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private Wire.SetupEnrollmentCapability RequireCapability(
        IReadOnlyDictionary<string, IEnumerable<string>> headers)
    {
        string[] values = HeaderValues(
            headers,
            Wire.SetupLiveContractMetadata.CapabilityHeader);
        if (values.Length != 1
            || !Wire.SetupEnrollmentCapability.TryCreate(
                values[0],
                out Wire.SetupEnrollmentCapability? capability)
            || capability is null)
        {
            throw ContractViolation();
        }

        return capability;
    }

    private static string[] HeaderValues(
        IReadOnlyDictionary<string, IEnumerable<string>> headers,
        string name) => headers
        .Where(header => string.Equals(
            header.Key,
            name,
            StringComparison.OrdinalIgnoreCase))
        .SelectMany(header => header.Value)
        .ToArray();

    private static bool HasRelation(
        IDictionary<string, Generated.HalLink>? links,
        string relation,
        HttpMethod expectedMethod) =>
        links is not null
        && links.TryGetValue(relation, out Generated.HalLink? link)
        && link is not null
        && !string.IsNullOrWhiteSpace(link.Href)
        && MethodMatches(link.Method, expectedMethod);

    private static bool MethodMatches(string? method, HttpMethod expectedMethod) =>
        string.Equals(method, expectedMethod.Method, StringComparison.OrdinalIgnoreCase)
        || expectedMethod == HttpMethod.Get && string.IsNullOrWhiteSpace(method);

    private static string NewIdempotencyKey() =>
        Guid.CreateVersion7().ToString("D");

    private static Uri NormalizeTarget(Uri targetBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(targetBaseAddress);
        if (!targetBaseAddress.IsAbsoluteUri
            || !string.Equals(
                targetBaseAddress.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(targetBaseAddress.UserInfo)
            || !string.IsNullOrEmpty(targetBaseAddress.Query)
            || !string.IsNullOrEmpty(targetBaseAddress.Fragment))
        {
            throw new ArgumentException(
                "Setup live target must be an absolute HTTPS URI without credentials, query, or fragment.",
                nameof(targetBaseAddress));
        }

        return new Uri(
            targetBaseAddress.AbsoluteUri.TrimEnd('/') + "/",
            UriKind.Absolute);
    }

    private static void ValidateTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Setup live tenant is required.", nameof(tenantId));
    }

    private static Wire.SetupEnrollmentScope[] SnapshotScopes(
        IReadOnlyList<Wire.SetupEnrollmentScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        Wire.SetupEnrollmentScope[] snapshot = scopes.ToArray();
        if (snapshot.Length is < 1 or > 3
            || snapshot.Distinct().Count() != snapshot.Length
            || snapshot.Any(scope => !Enum.IsDefined(scope)))
        {
            throw new ArgumentException(
                "Setup enrollment scopes must be a non-empty unique closed set.",
                nameof(scopes));
        }
        return snapshot;
    }

    private static Generated.SetupEnrollmentScope ToGeneratedScope(
        Wire.SetupEnrollmentScope scope) => scope switch
        {
            Wire.SetupEnrollmentScope.TargetRead =>
                Generated.SetupEnrollmentScope.Target_read,
            Wire.SetupEnrollmentScope.SecretBindingReadiness =>
                Generated.SetupEnrollmentScope.Secret_binding_readiness,
            Wire.SetupEnrollmentScope.SecretBindingWrite =>
                Generated.SetupEnrollmentScope.Secret_binding_write,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

    private Wire.SetupEnrollmentScope ToWireScope(
        Generated.SetupEnrollmentScope scope) => scope switch
        {
            Generated.SetupEnrollmentScope.Target_read =>
                Wire.SetupEnrollmentScope.TargetRead,
            Generated.SetupEnrollmentScope.Secret_binding_readiness =>
                Wire.SetupEnrollmentScope.SecretBindingReadiness,
            Generated.SetupEnrollmentScope.Secret_binding_write =>
                Wire.SetupEnrollmentScope.SecretBindingWrite,
            _ => throw ContractViolation()
        };

    private Wire.SetupEnrollmentState ToWireEnrollmentState(
        Generated.SetupEnrollmentState state) => state switch
        {
            Generated.SetupEnrollmentState.Active => Wire.SetupEnrollmentState.Active,
            Generated.SetupEnrollmentState.Revoked => Wire.SetupEnrollmentState.Revoked,
            Generated.SetupEnrollmentState.Expired => Wire.SetupEnrollmentState.Expired,
            _ => throw ContractViolation()
        };

    private Wire.SetupSecretBindingReadinessState ToWireReadinessState(
        Generated.SetupSecretBindingReadinessState state) => state switch
        {
            Generated.SetupSecretBindingReadinessState.Unconfigured =>
                Wire.SetupSecretBindingReadinessState.Unconfigured,
            Generated.SetupSecretBindingReadinessState.Ready =>
                Wire.SetupSecretBindingReadinessState.Ready,
            Generated.SetupSecretBindingReadinessState.Unavailable =>
                Wire.SetupSecretBindingReadinessState.Unavailable,
            Generated.SetupSecretBindingReadinessState.Unauthorized =>
                Wire.SetupSecretBindingReadinessState.Unauthorized,
            Generated.SetupSecretBindingReadinessState.Invalid =>
                Wire.SetupSecretBindingReadinessState.Invalid,
            _ => throw ContractViolation()
        };

    private Wire.SetupSecretBindingOperationState ToWireOperationState(
        Generated.SetupSecretBindingOperationState state) => state switch
        {
            Generated.SetupSecretBindingOperationState.Accepted =>
                Wire.SetupSecretBindingOperationState.Accepted,
            Generated.SetupSecretBindingOperationState.Succeeded =>
                Wire.SetupSecretBindingOperationState.Succeeded,
            Generated.SetupSecretBindingOperationState.Failed =>
                Wire.SetupSecretBindingOperationState.Failed,
            Generated.SetupSecretBindingOperationState.Cancelled =>
                Wire.SetupSecretBindingOperationState.Cancelled,
            _ => throw ContractViolation()
        };

    private Wire.SetupSecretBindingOperationOutcome ToWireOperationOutcome(
        Generated.SetupSecretBindingOperationOutcome outcome) => outcome switch
        {
            Generated.SetupSecretBindingOperationOutcome.Accepted =>
                Wire.SetupSecretBindingOperationOutcome.Accepted,
            Generated.SetupSecretBindingOperationOutcome.Ready =>
                Wire.SetupSecretBindingOperationOutcome.Ready,
            Generated.SetupSecretBindingOperationOutcome.Unavailable =>
                Wire.SetupSecretBindingOperationOutcome.Unavailable,
            Generated.SetupSecretBindingOperationOutcome.Unauthorized =>
                Wire.SetupSecretBindingOperationOutcome.Unauthorized,
            Generated.SetupSecretBindingOperationOutcome.Invalid =>
                Wire.SetupSecretBindingOperationOutcome.Invalid,
            Generated.SetupSecretBindingOperationOutcome.Cancelled =>
                Wire.SetupSecretBindingOperationOutcome.Cancelled,
            Generated.SetupSecretBindingOperationOutcome.Unavailable_enrollment =>
                Wire.SetupSecretBindingOperationOutcome.UnavailableEnrollment,
            _ => throw ContractViolation()
        };
}
