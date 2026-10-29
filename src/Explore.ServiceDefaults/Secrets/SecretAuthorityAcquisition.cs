using SecretKit.Core;
using SecretKit.Extensions.Configuration;
using SecretKit.Infisical;
using SecretKit.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Explore.ServiceDefaults.DataProtection;
using System.Collections.Frozen;

namespace Explore.ServiceDefaults.Secrets;

public sealed partial class SecretAuthorityAcquisition : IAsyncDisposable
{
    private readonly SecretAuthoritySelection _selection;
    private readonly ISecretReader _reader;
    private readonly TimeProvider _clock;
    private readonly InfisicalAuthorityOptions? _options;
    private readonly InfisicalClient? _client;
    private readonly InfisicalPeriodicTokenSession? _session;
    private InfisicalAuthenticationMaintenance? _maintenance;
    private readonly SocketsHttpHandler? _ownedTransport;
    private readonly LocalAuthorityReader? _local;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<Task> _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    public SecretAuthorityKind Provider => _selection.Provider;
    public SecretAuthoritySelection Selection => _selection;
    public Guid GenerationId { get; } = Guid.CreateVersion7();
    public TimeSpan ReadTimeout => _options?.RequestTimeout ?? TimeSpan.FromSeconds(15);
    public InfisicalAuthorityOptions? InfisicalOptions => _options;

    /// <summary>Borrowed native client for admitted outer-layer operations; the process owner alone disposes it.</summary>
    public InfisicalClient? NativeClient
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _client;
        }
    }

    public InfisicalAuthenticationMaintenance? AuthenticationMaintenance => Volatile.Read(ref _maintenance);

    /// <summary>Starts only the selected session's renewal helper, before lengthy serving-host admission.</summary>
    public async Task StartAuthenticationMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        InfisicalAuthenticationMaintenance maintenance;
        CancellationToken lifetime;
        lock (_operationGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_session is null)
                return;
            if (_maintenance is not null)
                throw new InvalidOperationException("secret_authentication_maintenance_already_started");
            maintenance = new InfisicalAuthenticationMaintenance(_session, TimeSpan.FromSeconds(10));
            _maintenance = maintenance;
            lifetime = _lifetime.Token;
            _activePreparations++;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            try
            {
                await maintenance.StartAsync(linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
            }
            catch
            {
                await maintenance.DisposeAsync().ConfigureAwait(false);
                throw;
            }
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
            || options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(1)
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
        SocketsHttpHandler? ownedTransport = null;
        InfisicalPeriodicTokenSession? session = null;
        InfisicalClient? client = null;
        SecretAuthorityAcquisition? owner = null;
        try
        {
            if (hostTransport is null)
                ownedTransport = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    ActivityHeadersPropagator = null
                };
            SocketsHttpHandler transport = hostTransport ?? ownedTransport!;
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
            owner = new SecretAuthorityAcquisition(selection, client, clock, options, client, session, ownedTransport);
            client = null;
            session = null;
            ownedTransport = null;
            var outcome = SecretOutcome<SecretAuthorityAcquisition>.Success(owner);
            owner = null;
            return outcome;
        }
        catch (ArgumentException)
        {
            return SecretOutcome<SecretAuthorityAcquisition>.Failure(SecretStatus.Invalid);
        }
        finally
        {
            using var unpublishedTransport = ownedTransport;
            await using var unpublishedOwner = owner;
            if (client is not null)
                await client.DisposeAsync().ConfigureAwait(false);
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
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

    public ValueTask<SecretOutcome<SecretAuthorityLease>> PrepareGroupAsync(
        IEnumerable<SecretAcquisitionEntry> entries,
        CancellationToken cancellationToken = default) =>
        PrepareGroupCoreAsync(entries, allowAbsent: false, cancellationToken);

    /// <summary>Collects declared optional fields without treating authority failures as absence.</summary>
    public ValueTask<SecretOutcome<SecretAuthorityLease>> PrepareAvailableGroupAsync(
        IEnumerable<SecretAcquisitionEntry> entries,
        CancellationToken cancellationToken = default) =>
        PrepareGroupCoreAsync(entries, allowAbsent: true, cancellationToken);

    private async ValueTask<SecretOutcome<SecretAuthorityLease>> PrepareGroupCoreAsync(
        IEnumerable<SecretAcquisitionEntry> entries, bool allowAbsent,
        CancellationToken cancellationToken)
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
                var nativeSelections = requested.Select(entry => new SecretSelection(entry.ConfigurationKey, _reader,
                    new SecretReference(_reader.Authority, new InfisicalSecretAddress(
                        _options.ProjectId, _options.VaultEnvironment, entry.Folder, entry.Key,
                        null, InfisicalResolution.Raw, false))));
                var result = allowAbsent
                    ? await PrepareAvailableSelectionsAsync(requested, nativeSelections, bounded.Token).ConfigureAwait(false)
                    : await PrepareSelectionsAsync(requested, nativeSelections, _clock, bounded.Token).ConfigureAwait(false);
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
            var local = allowAbsent
                ? await PrepareAvailableSelectionsAsync(requested, selections, linked.Token).ConfigureAwait(false)
                : await PrepareSelectionsAsync(requested, selections, _clock, linked.Token).ConfigureAwait(false);
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

    private async ValueTask<SecretOutcome<SecretAuthorityLease>> PrepareAvailableSelectionsAsync(
        SecretAcquisitionEntry[] requested, IEnumerable<SecretSelection> selections,
        CancellationToken cancellationToken)
    {
        using var retained = new RetainedAuthorityReader(_reader.Authority);
        var presentEntries = new List<SecretAcquisitionEntry>();
        var presentSelections = new List<SecretSelection>();
        int index = 0;
        foreach (SecretSelection selection in selections)
        {
            var result = await _reader.ReadAsync(selection.Reference, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                retained.Reads.Add(selection.Reference, result.Value);
                presentEntries.Add(requested[index]);
                presentSelections.Add(new(selection.ConsumerKey, retained, selection.Reference));
            }
            else if (result.Status != SecretStatus.Absent)
            {
                return SecretOutcome<SecretAuthorityLease>.Failure(result.Status, result.RetryAfter);
            }
            index++;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (presentEntries.Count == 0)
            return SecretOutcome<SecretAuthorityLease>.Failure(SecretStatus.Absent);
        // Export the retained observations, not a second provider read that can race
        // deletion, recreation or a changed credential between collection and publication.
        return await PrepareSelectionsAsync(
            presentEntries.ToArray(), presentSelections, _clock, cancellationToken).ConfigureAwait(false);
    }

    private sealed class RetainedAuthorityReader(SecretAuthority authority) : ISecretReader, IDisposable
    {
        public SecretAuthority Authority { get; } = authority;
        internal Dictionary<SecretReference, SecretRead> Reads { get; } = new();

        public ValueTask<SecretOutcome<SecretRead>> ReadAsync(
            SecretReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reference.BelongsTo(Authority) || !Reads.TryGetValue(reference, out var read))
                return ValueTask.FromResult(SecretOutcome<SecretRead>.Failure(SecretStatus.Invalid));
            SecretRead? copy = null;
            try
            {
                copy = new SecretRead(read.Material.Copy(), read.Observation);
                var outcome = SecretOutcome<SecretRead>.Success(copy);
                copy = null;
                return ValueTask.FromResult(outcome);
            }
            finally
            {
                copy?.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (SecretRead read in Reads.Values)
                read.Dispose();
        }
    }

    /// <summary>
    /// Acquires only the selected serving consumer's ring and admission, without configuration export.
    /// The returned owner pins a complete native generation; it cannot read arbitrary authority keys.
    /// </summary>
    public async ValueTask<SecretDataProtectionMaterial> PrepareDataProtectionAsync(
        string partition, CancellationToken cancellationToken = default)
    {
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
            string selectedPartition = _selection.Consumer switch
            {
                SecretConsumerRole.Api => "api",
                SecretConsumerRole.Bff => "bff",
                SecretConsumerRole.Combined => "combined",
                _ => throw new InvalidOperationException("secret_data_protection_scope_invalid")
            };
            if (!string.Equals(partition, selectedPartition, StringComparison.Ordinal))
                throw new InvalidOperationException("secret_data_protection_scope_invalid");
            string prefix = ExternalDataProtectionRegistration.EnvironmentPrefix(partition);
            using var timeout = new CancellationTokenSource(
                _options?.RequestTimeout ?? TimeSpan.FromSeconds(15), _clock);
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, timeout.Token);
            _local?.Refresh();
            ISecretReader dataProtectionReader = _local?.FreshReader ?? _reader;
            SecretSelection Select(string name, string folder, string key) => new(name, dataProtectionReader,
                new SecretReference(dataProtectionReader.Authority, _options is null
                    ? new LocalSecretAddress(key)
                    : new InfisicalSecretAddress(_options.ProjectId, _options.VaultEnvironment,
                        folder, key, null, InfisicalResolution.Raw, false)));
            using var loader = new SecretSnapshotLoader(
                [Select("Ring", $"/runtime/{partition}/data-protection", prefix + "_RING"),
                 Select("Admission", $"/admission/{partition}/data-protection", prefix + "_ADMISSION")], _clock);
            var result = await loader.RefreshAsync(bounded.Token).ConfigureAwait(false);
            if (!result.IsSuccess)
                throw new InvalidOperationException("secret_data_protection_unavailable");
            if (_session is not null)
            {
                var token = await _session.GetTokenAsync(bounded.Token).ConfigureAwait(false);
                if (!token.IsSuccess)
                    throw new InvalidOperationException("secret_data_protection_unavailable");
                token.Value.Dispose();
            }
            SecretDataProtectionMaterial? material = null;
            try
            {
                material = new SecretDataProtectionMaterial(loader.Acquire());
                lock (_operationGate)
                {
                    bounded.Token.ThrowIfCancellationRequested();
                    if (_disposed != 0)
                        throw new OperationCanceledException("secret_authority_retired", bounded.Token);
                    var resultMaterial = material;
                    material = null;
                    return resultMaterial;
                }
            }
            finally
            {
                material?.Dispose();
            }
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
            _retired.TrySetResult(RetireAsync());
        return new ValueTask(_retired.Task.Unwrap());
    }

    private async Task RetireAsync()
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
                    if (_maintenance is not null)
                        await _maintenance.DisposeAsync().ConfigureAwait(false);
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
        SecretSnapshotLoader? loader = null;
        SecretSnapshotHandle? handle = null;
        SecretConfigurationExport? export = null;
        IConfigurationRoot? configuration = null;
        SecretAuthorityLease? lease = null;
        try
        {
            loader = new SecretSnapshotLoader(selections, timeProvider);
            var prepared = await loader.RefreshAsync(cancellationToken).ConfigureAwait(false);
            if (!prepared.IsSuccess)
                return SecretOutcome<SecretAuthorityLease>.Failure(prepared.Status, prepared.RetryAfter);
            cancellationToken.ThrowIfCancellationRequested();
            handle = loader.Acquire();
            export = SecretConfigurationExport.CreateAcknowledgingManagedStrings(handle,
                requested.Select(entry => new SecretConfigurationMapping(entry.ConfigurationKey, entry.ConfigurationKey)));
            configuration = new ConfigurationBuilder().Add(export).Build();
            lease = new SecretAuthorityLease(loader, handle, export, configuration);
            loader = null;
            handle = null;
            export = null;
            configuration = null;
            var outcome = SecretOutcome<SecretAuthorityLease>.Success(lease);
            lease = null;
            return outcome;
        }
        finally
        {
            using var unpublishedLoader = loader;
            using var unpublishedHandle = handle;
            using var unpublishedExport = export;
            using var unpublishedConfiguration = configuration as IDisposable;
            using var unpublishedLease = lease;
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

        internal IReadOnlyDictionary<string, string?> RuntimeValues { get; } =
            configuration.AsEnumerable().ToFrozenDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        internal ISecretReader FreshReader => new FreshLocalReader(this);

        internal void Refresh() => (configuration as IConfigurationRoot)?.Reload();

        public ValueTask<SecretOutcome<SecretRead>> ReadAsync(
            SecretReference reference, CancellationToken cancellationToken = default) =>
            Read(reference, fresh: false, cancellationToken);

        private ValueTask<SecretOutcome<SecretRead>> Read(
            SecretReference reference, bool fresh, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reference.BelongsTo(Authority) || reference.Address is not LocalSecretAddress address)
                return ValueTask.FromResult(SecretOutcome<SecretRead>.Failure(SecretStatus.Invalid));
            string? value = fresh ? configuration[address.Key] : RuntimeValues.GetValueOrDefault(address.Key);
            if (value is null)
                return ValueTask.FromResult(SecretOutcome<SecretRead>.Failure(SecretStatus.Absent));
            if (value.Length == 0)
                return ValueTask.FromResult(SecretOutcome<SecretRead>.Failure(SecretStatus.Invalid));
            SecretRead? read = null;
            try
            {
                read = new SecretRead(SecretMaterial.FromText(value),
                    new SecretObservation(reference, null, clock.GetUtcNow(), SecretValidity.Valid, null));
                var outcome = SecretOutcome<SecretRead>.Success(read);
                read = null;
                return ValueTask.FromResult(outcome);
            }
            finally
            {
                read?.Dispose();
            }
        }

        public void Dispose() => ownedConfiguration?.Dispose();

        private sealed class FreshLocalReader(LocalAuthorityReader owner) : ISecretReader
        {
            public SecretAuthority Authority => owner.Authority;
            public ValueTask<SecretOutcome<SecretRead>> ReadAsync(
                SecretReference reference, CancellationToken cancellationToken = default) =>
                owner.Read(reference, fresh: true, cancellationToken);
        }
    }
}

/// <summary>Owns exactly two protected values from one complete acquired generation.</summary>
public sealed class SecretDataProtectionMaterial : IDisposable
{
    private readonly SecretSnapshotHandle _handle;
    internal SecretDataProtectionMaterial(SecretSnapshotHandle handle) => _handle = handle;
    public SecretRead ReadRing() => _handle.Read("Ring");
    public SecretRead ReadAdmission() => _handle.Read("Admission");
    public void Dispose() => _handle.Dispose();
    public override string ToString() => "secret_data_protection_material";
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
