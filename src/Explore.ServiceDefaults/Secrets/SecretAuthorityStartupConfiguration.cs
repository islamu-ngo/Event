using SecretKit.Core;
using SecretKit.Infisical;
using System.Collections.Frozen;
using Microsoft.Extensions.Configuration;

namespace Explore.ServiceDefaults.Secrets;

public sealed partial class SecretAuthorityAcquisition
{
    /// <summary>Explicitly exports a staged startup group as managed configuration strings.</summary>
    public async ValueTask<SecretOutcome<IReadOnlyDictionary<string, string?>>> PrepareConfigurationGroupAsync(
        IReadOnlyList<string> roots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        CancellationToken lifetime;
        lock (_operationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            lifetime = _lifetime.Token;
            _activePreparations++;
        }
        try
        {
            using var timeout = new CancellationTokenSource(
                _options?.RequestTimeout ?? TimeSpan.FromSeconds(15), _clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetime, timeout.Token);
            linked.Token.ThrowIfCancellationRequested();
            string[] requestedRoots = roots.ToArray();
            SecretAcquisitionPolicy.AdmitRoots(_selection, requestedRoots);
            var staged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (_local is not null)
            {
                foreach (var pair in _local.RuntimeValues)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (!SecretStartupConfigurationProjection.TryMapLocal(_selection.Consumer, pair.Key, out string folder)
                        || !requestedRoots.Contains(folder, StringComparer.Ordinal))
                        continue;
                    staged.Add(pair.Key.Replace("__", ":", StringComparison.Ordinal), pair.Value);
                }
            }
            else
            {
                var sources = new List<(SecretAcquisitionEntry Entry, IReadOnlyList<string> Destinations)>();
                var admittedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string folder in requestedRoots)
                {
                    var inventory = await _client!.InspectAsync(new InfisicalInventoryScope(
                        _reader.Authority, _options!.ProjectId, _options.VaultEnvironment,
                        folder, maximumItems: 1000), linked.Token).ConfigureAwait(false);
                    if (!inventory.IsSuccess)
                        return SecretOutcome<IReadOnlyDictionary<string, string?>>.Failure(
                            inventory.Status, inventory.RetryAfter);
                    if (!inventory.Value.IsComplete || inventory.Value.Items.Count > 1000)
                        return SecretOutcome<IReadOnlyDictionary<string, string?>>.Failure(SecretStatus.Unknown);
                    foreach (SecretObservation observation in inventory.Value.Items)
                    {
                        if (!observation.Reference.BelongsTo(_reader.Authority)
                            || observation.Reference.Address is not InfisicalSecretAddress address
                            || !string.Equals(address.ProjectId, _options.ProjectId, StringComparison.Ordinal)
                            || !string.Equals(address.Environment, _options.VaultEnvironment, StringComparison.Ordinal)
                            || !string.Equals(address.Folder, folder, StringComparison.Ordinal)
                            || address.Resolution != InfisicalResolution.Raw || address.IncludeImports)
                            return SecretOutcome<IReadOnlyDictionary<string, string?>>.Failure(SecretStatus.Invalid);
                        IReadOnlyList<string> destinations =
                            SecretStartupConfigurationProjection.Destinations(folder, address.Key);
                        if (destinations.Count == 0)
                            continue;
                        var entries = destinations.Select(destination =>
                            new SecretAcquisitionEntry(folder, address.Key, destination)).ToArray();
                        SecretAcquisitionPolicy.Admit(_selection, entries);
                        if (destinations.Any(destination => !admittedDestinations.Add(destination)))
                            return SecretOutcome<IReadOnlyDictionary<string, string?>>.Failure(SecretStatus.Invalid);
                        sources.Add((entries[0], destinations));
                    }
                }
                if (sources.Count == 0)
                    return SecretOutcome<IReadOnlyDictionary<string, string?>>.Failure(SecretStatus.Absent);
                // Inventory discovers names only. Every advertised member remains required
                // in the exact read; deletion between these observations fails the group.
                var prepared = await PrepareGroupAsync(
                    sources.Select(source => source.Entry), linked.Token).ConfigureAwait(false);
                if (!prepared.IsSuccess)
                    return SecretOutcome<IReadOnlyDictionary<string, string?>>.Failure(
                        prepared.Status == SecretStatus.Absent ? SecretStatus.Unknown : prepared.Status,
                        prepared.RetryAfter);
                using var lease = prepared.Value;
                foreach (var source in sources)
                {
                    string? value = lease.Configuration[source.Entry.ConfigurationKey];
                    if (value is null || value.Contains("${", StringComparison.Ordinal))
                        return SecretOutcome<IReadOnlyDictionary<string, string?>>.Failure(SecretStatus.Invalid);
                    foreach (string destination in source.Destinations)
                        staged.Add(destination, value);
                }
            }
            lock (_operationGate)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (_disposed != 0)
                    throw new OperationCanceledException("secret_authority_retired", linked.Token);
                return staged.Count == 0
                    ? SecretOutcome<IReadOnlyDictionary<string, string?>>.Failure(SecretStatus.Absent)
                    : SecretOutcome<IReadOnlyDictionary<string, string?>>.Success(
                        staged.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
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
}
