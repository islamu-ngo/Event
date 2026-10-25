using SecretKit.Core;
using SecretKit.Extensions.Configuration;
using SecretKit.Infisical;
using Microsoft.Extensions.Configuration;

namespace Explore.ServiceDefaults.Secrets;

public sealed class SecretAuthorityAcquisition : IAsyncDisposable
{
    private readonly SecretAuthoritySelection _selection;
    private readonly ISecretReader _reader;
    private readonly TimeProvider _clock;
    private readonly InfisicalAuthorityOptions? _options;
    private readonly InfisicalClient? _client;
    private readonly InfisicalPeriodicTokenSession? _session;
    private readonly SocketsHttpHandler? _ownedTransport;
    private readonly LocalAuthorityReader? _local;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _operationGate = new();
    private TaskCompletionSource? _preparationsDrained;
    private int _activePreparations;
    private int _disposed;

    private SecretAuthorityAcquisition(
        SecretAuthoritySelection selection, ISecretReader reader, TimeProvider clock,
        InfisicalAuthorityOptions? options = null, InfisicalClient? client = null,
        InfisicalPeriodicTokenSession? session = null, SocketsHttpHandler? ownedTransport = null,
        LocalAuthorityReader? local = null)
    {
        _selection = selection;
        _reader = reader;
        _clock = clock;
        _options = options;
        _client = client;
        _session = session;
        _ownedTransport = ownedTransport;
        _local = local;
    }

    /// <summary>The host may explicitly maintain this session; construction starts no maintenance loop.</summary>
    public InfisicalPeriodicTokenSession? AuthenticationSession => _session;

    public static async ValueTask<SecretOutcome<SecretAuthorityAcquisition>> OpenInfisicalAsync(
        SecretAuthoritySelection selection,
        IEnumerable<SecretAcquisitionEntry> entries,
        InfisicalAuthorityOptions options,
        SecretMaterial clientSecret,
        SecretAuthorityLoginAttempt loginAttempt,
        SocketsHttpHandler? hostTransport = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSecret);
        ArgumentNullException.ThrowIfNull(loginAttempt);
        SecretAcquisitionPolicy.Admit(selection, entries);
        cancellationToken.ThrowIfCancellationRequested();
        bool development = string.Equals(selection.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(selection.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);
        if (selection.Provider != SecretAuthorityKind.Infisical
            || options.Origin is not { IsAbsoluteUri: true }
            || !(options.Origin.Scheme == Uri.UriSchemeHttps
                || development && options.Origin.IsLoopback && options.Origin.Scheme == Uri.UriSchemeHttp)
            || options.Origin.AbsolutePath != "/" || options.Origin.UserInfo.Length != 0
            || options.Origin.Query.Length != 0 || options.Origin.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(options.ProjectId) || string.IsNullOrWhiteSpace(options.VaultEnvironment)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(5)
            || options.RenewBefore <= TimeSpan.Zero
            || options.Lifetime is not (InfisicalTokenLifetime.Finite or InfisicalTokenLifetime.Periodic)
            || options.Lifetime == InfisicalTokenLifetime.Finite && options.Period is not null
            || options.Lifetime == InfisicalTokenLifetime.Periodic
                && (options.Period is null || options.Period <= TimeSpan.Zero))
            return SecretOutcome<SecretAuthorityAcquisition>.Failure(SecretStatus.Invalid);

        TimeProvider clock = timeProvider ?? TimeProvider.System;
        var network = new InfisicalNetworkOptions
        {
            AllowLoopbackDevelopmentHttp = development && options.Origin.IsLoopback,
            MaximumResponseBytes = 1024 * 1024
        };
        SocketsHttpHandler transport = hostTransport ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ActivityHeadersPropagator = null
        };
        InfisicalPeriodicTokenSession? session = null;
        InfisicalClient? client = null;
        bool transferred = false;
        try
        {
            using var timeout = new CancellationTokenSource(options.RequestTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var authentication = new InfisicalUniversalAuthentication(options.Origin, transport, network, clock);
            InfisicalUniversalLoginReceipt receipt = authentication.CreateReceipt();
            loginAttempt.Bind(receipt);
            var result = await authentication.LoginAsync(options.ClientId, clientSecret, receipt,
                cancellationToken: linked.Token).ConfigureAwait(false);
            if (!result.IsSuccess)
                return SecretOutcome<SecretAuthorityAcquisition>.Failure(result.Status, result.RetryAfter);
            using InfisicalUniversalLogin login = result.Value;
            if (!double.IsFinite(login.ExpiresIn) || login.ExpiresIn <= 0
                || !double.IsFinite(login.AccessTokenMaxTTL) || login.AccessTokenMaxTTL < 0
                || options.RenewBefore >= TimeSpan.FromSeconds(login.ExpiresIn)
                || options.Lifetime == InfisicalTokenLifetime.Finite
                    && (login.AccessTokenMaxTTL <= 0 || login.ExpiresIn > login.AccessTokenMaxTTL)
                || options.Lifetime == InfisicalTokenLifetime.Periodic
                    && (login.AccessTokenMaxTTL != 0 || options.Period != TimeSpan.FromSeconds(login.ExpiresIn)))
                return SecretOutcome<SecretAuthorityAcquisition>.Failure(SecretStatus.Invalid);
            // Bind observed timing to this login; native evidence owns the original finite ceiling.
            var evidence = login.BindLifetimeEvidence(new InfisicalTokenLifetimeEvidence
            {
                OriginalIssuedAt = login.RequestStartedAt,
                ExpiresAt = login.RequestStartedAt + TimeSpan.FromSeconds(login.ExpiresIn),
                Lifetime = options.Lifetime,
                ReportedMaximumTtl = TimeSpan.FromSeconds(login.AccessTokenMaxTTL),
                Period = options.Period
            });
            if (evidence.OriginalIssuedAt == default || evidence.ExpiresAt <= clock.GetUtcNow())
                return SecretOutcome<SecretAuthorityAcquisition>.Failure(SecretStatus.Invalid);
            session = new InfisicalPeriodicTokenSession(login, evidence,
                options.RenewBefore, options.RequestTimeout, transport, network, clock);
            client = new InfisicalClient(options.Origin, transport, session, network, clock);
            var owner = new SecretAuthorityAcquisition(selection, client, clock, options, client, session,
                hostTransport is null ? transport : null);
            transferred = true;
            return SecretOutcome<SecretAuthorityAcquisition>.Success(owner);
        }
        catch (ArgumentException)
        {
            return SecretOutcome<SecretAuthorityAcquisition>.Failure(SecretStatus.Invalid);
        }
        finally
        {
            if (!transferred)
            {
                if (client is not null)
                    await client.DisposeAsync().ConfigureAwait(false);
                if (session is not null)
                    await session.DisposeAsync().ConfigureAwait(false);
                if (hostTransport is null)
                    transport.Dispose();
            }
        }
    }

    public static SecretAuthorityAcquisition OpenLocal(
        SecretAuthoritySelection selection,
        IConfiguration? testingAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        if (selection.Provider == SecretAuthorityKind.Infisical
            || testingAuthority is not null
                && !string.Equals(selection.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("secret_authority_local_source_invalid");
        IConfiguration configuration = testingAuthority ?? (selection.Provider == SecretAuthorityKind.Environment
            ? new ConfigurationBuilder().AddEnvironmentVariables().Build()
            : new ConfigurationBuilder().AddUserSecrets("event-shared-secrets").Build());
        var reader = new LocalAuthorityReader(configuration,
            testingAuthority is null ? (IDisposable)configuration : null, TimeProvider.System);
        return new SecretAuthorityAcquisition(selection, reader, TimeProvider.System, local: reader);
    }

    public async ValueTask<SecretOutcome<SecretAuthorityLease>> PrepareGroupAsync(
        IEnumerable<SecretAcquisitionEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        CancellationToken lifetime;
        lock (_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            lifetime = _lifetime.Token;
            _activePreparations++;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            linked.Token.ThrowIfCancellationRequested();
            SecretAcquisitionEntry[] requested = entries.ToArray();
            SecretAcquisitionPolicy.Admit(_selection, requested);
            if (_options is not null)
            {
                using var timeout = new CancellationTokenSource(_options.RequestTimeout, _clock);
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
                var result = await PrepareAsync(_selection, _reader, _options.ProjectId,
                    _options.VaultEnvironment, requested, _clock, bounded.Token).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    try
                    {
                        var token = await _session!.GetTokenAsync(bounded.Token).ConfigureAwait(false);
                        if (!token.IsSuccess)
                        {
                            result.Value.Dispose();
                            return SecretOutcome<SecretAuthorityLease>.Failure(token.Status, token.RetryAfter);
                        }
                        token.Value.Dispose();
                    }
                    catch
                    {
                        result.Value.Dispose();
                        throw;
                    }
                }
                return AdmitPrepared(result, bounded.Token);
            }
            var selections = requested.Select(entry => new SecretSelection(entry.ConfigurationKey, _reader,
                new SecretReference(_reader.Authority, new LocalSecretAddress(entry.Key))));
            var local = await PrepareSelectionsAsync(requested, selections, _clock, linked.Token).ConfigureAwait(false);
            return AdmitPrepared(local, linked.Token);
        }
        finally
        {
            TaskCompletionSource? drained = null;
            lock (_operationGate)
            {
                _activePreparations--;
                if (_activePreparations == 0 && _disposed != 0)
                    drained = _preparationsDrained;
            }
            drained?.TrySetResult();
        }
    }

    private SecretOutcome<SecretAuthorityLease> AdmitPrepared(
        SecretOutcome<SecretAuthorityLease> result, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_operationGate)
            {
                if (_disposed != 0)
                    throw new OperationCanceledException("secret_authority_retired", cancellationToken);
            }
            return result;
        }
        catch
        {
            if (result.IsSuccess)
                result.Value.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        bool retire = false;
        lock (_operationGate)
        {
            if (_disposed == 0)
            {
                _disposed = 1;
                retire = true;
                if (_activePreparations > 0)
                    _preparationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        if (retire)
            _ = RetireAsync();
        return new ValueTask(_retired.Task);
    }

    private async Task RetireAsync()
    {
        try
        {
            using (_lifetime)
            using (_ownedTransport)
            using (_local)
            {
                try
                {
                    try
                    {
                        await _lifetime.CancelAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        if (_preparationsDrained is not null)
                            await _preparationsDrained.Task.ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (_client is not null)
                            await _client.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        if (_session is not null)
                            await _session.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            _retired.TrySetResult();
        }
        catch (Exception exception)
        {
            _retired.TrySetException(exception);
        }
    }

    public override string ToString() => "secret_authority_acquisition";

    public static async ValueTask<SecretOutcome<SecretAuthorityLease>> PrepareAsync(
        SecretAuthoritySelection selection,
        ISecretReader reader,
        string projectId,
        string vaultEnvironment,
        IEnumerable<SecretAcquisitionEntry> entries,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();
        SecretAcquisitionEntry[] requested = entries.ToArray();
        SecretAcquisitionPolicy.Admit(selection, requested);
        if (selection.Provider != SecretAuthorityKind.Infisical)
            throw new InvalidOperationException("secret_authority_reader_invalid");
        var selections = requested.Select(entry => new SecretSelection(
            entry.ConfigurationKey, reader,
            new SecretReference(reader.Authority, new InfisicalSecretAddress(
                projectId, vaultEnvironment, entry.Folder, entry.Key,
                null, InfisicalResolution.Raw, false))));
        return await PrepareSelectionsAsync(requested, selections, timeProvider ?? TimeProvider.System,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<SecretOutcome<SecretAuthorityLease>> PrepareSelectionsAsync(
        SecretAcquisitionEntry[] requested, IEnumerable<SecretSelection> selections,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var loader = new SecretSnapshotLoader(selections, timeProvider);
        SecretSnapshotHandle? handle = null;
        SecretConfigurationExport? export = null;
        IConfigurationRoot? configuration = null;
        bool transferred = false;
        try
        {
            var prepared = await loader.RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (!prepared.IsSuccess)
                return SecretOutcome<SecretAuthorityLease>.Failure(prepared.Status, prepared.RetryAfter);
            cancellationToken.ThrowIfCancellationRequested();
            handle = loader.Acquire();
            export = SecretConfigurationExport.CreateAcknowledgingManagedStrings(handle,
                requested.Select(entry => new SecretConfigurationMapping(entry.ConfigurationKey, entry.ConfigurationKey)));
            configuration = new ConfigurationBuilder().Add(export).Build();
            var lease = new SecretAuthorityLease(loader, handle, export, configuration);
            transferred = true;
            return SecretOutcome<SecretAuthorityLease>.Success(lease);
        }
        finally
        {
            if (!transferred)
            {
                (configuration as IDisposable)?.Dispose();
                export?.Dispose();
                handle?.Dispose();
                loader.Dispose();
            }
        }
    }

    private sealed class LocalSecretAddress(string key) : SecretAddress
    {
        internal string Key { get; } = key;
    }

    private sealed class LocalAuthorityReader(
        IConfiguration configuration, IDisposable? ownedConfiguration, TimeProvider clock) : ISecretReader, IDisposable
    {
        public SecretAuthority Authority { get; } = new();

        public ValueTask<SecretOutcome<SecretRead>> ReadAsync(
            SecretReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reference.BelongsTo(Authority) || reference.Address is not LocalSecretAddress address)
                return ValueTask.FromResult(SecretOutcome<SecretRead>.Failure(SecretStatus.Invalid));
            string? value = configuration[address.Key];
            return ValueTask.FromResult(string.IsNullOrEmpty(value)
                ? SecretOutcome<SecretRead>.Failure(SecretStatus.Absent)
                : SecretOutcome<SecretRead>.Success(new SecretRead(SecretMaterial.FromText(value),
                    new SecretObservation(reference, null, clock.GetUtcNow(), SecretValidity.Valid, null))));
        }

        public void Dispose() => ownedConfiguration?.Dispose();
    }
}

/// <summary>Retains native dispatch evidence even when the opening waiter is canceled.</summary>
public sealed class SecretAuthorityLoginAttempt
{
    private InfisicalUniversalLoginReceipt? _receipt;
    public InfisicalUniversalLoginReceipt? Receipt => Volatile.Read(ref _receipt);

    internal void Bind(InfisicalUniversalLoginReceipt receipt)
    {
        if (Interlocked.CompareExchange(ref _receipt, receipt, null) is not null)
            throw new InvalidOperationException("secret_authority_login_attempt_consumed");
    }

    public override string ToString() => "secret_authority_login_attempt";
}
