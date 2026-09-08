// ABOUTME: Implements per-setting transaction-scoped mutation locking for every supported relational provider.
// ABOUTME: Acquires ordered manifest leases before caller-owned transactions so snapshots start after every wait.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public sealed class RelationalSettingMutationLock : ISettingMutationLock
{
    private readonly ExploreDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly Func<string, CancellationToken, Task>?
        _beforeOuterLockAcquisition;
    private readonly AsyncLocal<IReadOnlySet<string>?> _outerOrderedKeys = new();

    public RelationalSettingMutationLock(
        ExploreDbContext dbContext,
        IUnitOfWork unitOfWork)
        : this(dbContext, unitOfWork, beforeOuterLockAcquisition: null)
    {
    }

    internal RelationalSettingMutationLock(
        ExploreDbContext dbContext,
        IUnitOfWork unitOfWork,
        Func<string, CancellationToken, Task>? beforeOuterLockAcquisition)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _beforeOuterLockAcquisition = beforeOuterLockAcquisition;
    }

    public Task<T> ExecuteAsync<T>(
        string canonicalSettingKey,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSettingKey);
        return ExecuteManyAsync([canonicalSettingKey], operation, cancellationToken);
    }

    public Task<T> ExecuteManyAsync<T>(
        IEnumerable<string> canonicalSettingKeys,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canonicalSettingKeys);
        string[] orderedKeys = NormalizeCanonicalKeys(canonicalSettingKeys);
        if (orderedKeys.Length == 0)
        {
            throw new ArgumentException(
                "At least one canonical setting key is required.",
                nameof(canonicalSettingKeys));
        }

        bool visitorPolicy = RequiresVisitorAccessFence(orderedKeys);
        if (RequiresEmailDeliveryFence(orderedKeys) || visitorPolicy)
        {
            IReadOnlySet<string>? outerKeys = _outerOrderedKeys.Value;
            if (outerKeys is null)
            {
                if (_dbContext.Database.CurrentTransaction is not null)
                    throw new InvalidOperationException(
                        "Policy locks must be acquired before the caller-owned transaction begins.");

                // Admission owns this policy lock before opening its transaction. Writers must
                // use the same order so a database writer cannot block its own lock holder.
                return ExecuteOrderedGroupsAsync(
                    [orderedKeys],
                    token => visitorPolicy
                        ? _unitOfWork.ExecuteSerializableAsync(
                            innerToken => ExecuteInsideTransactionAsync(orderedKeys, operation, innerToken), token)
                        : _unitOfWork.ExecuteInTransactionAsync(
                            innerToken => ExecuteInsideTransactionAsync(orderedKeys, operation, innerToken), token),
                    cancellationToken);
            }

            if (!orderedKeys.All(outerKeys.Contains))
                throw new InvalidOperationException("Nested policy mutations must declare all policy keys in the outer lock group.");
        }

        return _dbContext.Database.CurrentTransaction is not null
            ? ExecuteInsideTransactionAsync(
                orderedKeys,
                operation,
                cancellationToken)
            : _unitOfWork.ExecuteInTransactionAsync(
                token => ExecuteInsideTransactionAsync(
                    orderedKeys,
                    operation,
                    token),
                cancellationToken);
    }

    public Task<T> ExecuteOrderedGroupsAsync<T>(
        IEnumerable<IEnumerable<string>> canonicalSettingKeyGroups,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canonicalSettingKeyGroups);
        ArgumentNullException.ThrowIfNull(operation);
        string[] orderedKeys = NormalizeOrderedCanonicalKeyGroups(
            canonicalSettingKeyGroups);
        if (orderedKeys.Length == 0)
        {
            throw new ArgumentException(
                "At least one canonical setting-key group is required.",
                nameof(canonicalSettingKeyGroups));
        }

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Ordered setting-lock groups must be acquired before the caller-owned transaction begins.");
        }

        IExecutionStrategy strategy =
            _dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            var leases = new List<IAsyncDisposable>(orderedKeys.Length);
            try
            {
                foreach (string canonicalSettingKey in orderedKeys)
                {
                    if (_beforeOuterLockAcquisition is not null)
                    {
                        await _beforeOuterLockAcquisition(
                            canonicalSettingKey,
                            cancellationToken);
                    }

                    leases.Add(await RelationalNamedLock.AcquireSessionAsync(
                        _dbContext,
                        $"explore:setting-mutation:{canonicalSettingKey}",
                        cancellationToken));
                }

                IReadOnlySet<string>? previousKeys =
                    _outerOrderedKeys.Value;
                _outerOrderedKeys.Value =
                    orderedKeys.ToHashSet(StringComparer.Ordinal);
                try
                {
                    return await operation(cancellationToken);
                }
                finally
                {
                    _outerOrderedKeys.Value = previousKeys;
                }
            }
            finally
            {
                for (int index = leases.Count - 1; index >= 0; index--)
                {
                    await leases[index].DisposeAsync();
                }
            }
        });
    }

    private async Task<T> ExecuteInsideTransactionAsync<T>(
        IReadOnlyList<string> canonicalSettingKeys,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var leases = new List<IAsyncDisposable>(canonicalSettingKeys.Count);
        try
        {
            IReadOnlySet<string>? outerOrderedKeys =
                _outerOrderedKeys.Value;
            foreach (string canonicalSettingKey in canonicalSettingKeys)
            {
                // The outer session/process lease already owns this resource. Reacquiring it
                // can self-block on SQLite or SQL Server and increments MySQL lock ownership.
                if (outerOrderedKeys?.Contains(canonicalSettingKey) == true)
                {
                    continue;
                }

                leases.Add(await RelationalNamedLock.AcquireTransactionAsync(
                    _dbContext,
                    $"explore:setting-mutation:{canonicalSettingKey}",
                    cancellationToken));
            }

            return await operation(cancellationToken);
        }
        finally
        {
            for (int index = leases.Count - 1; index >= 0; index--)
            {
                await leases[index].DisposeAsync();
            }
        }
    }

    internal static long ComputeStableLockKey(string canonicalSettingKey) =>
        RelationalNamedLock.ComputeStableKey(
            $"explore:setting-mutation:{canonicalSettingKey.Trim().ToLowerInvariant()}");

    internal static string[] NormalizeCanonicalKeys(
        IEnumerable<string> canonicalSettingKeys)
    {
        string[] normalizedKeys = canonicalSettingKeys
            .Select(NormalizeCanonicalKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (RequiresVisitorAccessFence(normalizedKeys))
            normalizedKeys = normalizedKeys.Concat(VisitorAccessCapabilityResolver.AuthoritySettingKeys)
                .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        return RequiresEmailDeliveryFence(normalizedKeys)
            ? [GovernanceSettingKeys.Email.DeliveryEnabled,
                .. normalizedKeys.Where(key => key != GovernanceSettingKeys.Email.DeliveryEnabled)]
            : normalizedKeys;
    }

    internal static string[] NormalizeOrderedCanonicalKeyGroups(
        IEnumerable<IEnumerable<string>> canonicalSettingKeyGroups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (IEnumerable<string> group in canonicalSettingKeyGroups)
        {
            ArgumentNullException.ThrowIfNull(group);
            foreach (string key in NormalizeCanonicalKeys(group))
            {
                if (seen.Add(key))
                {
                    ordered.Add(key);
                }
            }
        }

        // Visitor authority is always one complete group, independent of the caller's other groups.
        if (RequiresVisitorAccessFence(ordered))
        {
            ordered.RemoveAll(key => VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(key));
            ordered.InsertRange(0, VisitorAccessCapabilityResolver.AuthoritySettingKeys);
        }

        if (ordered.Remove(GovernanceSettingKeys.Email.DeliveryEnabled))
            ordered.Insert(0, GovernanceSettingKeys.Email.DeliveryEnabled);

        return ordered.ToArray();
    }

    internal static bool RequiresVisitorAccessFence(IEnumerable<string> keys) =>
        keys.Select(NormalizeCanonicalKey).Any(VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains);

    internal static bool RequiresEmailDeliveryFence(IEnumerable<string> keys) =>
        keys.Select(NormalizeCanonicalKey).Any(key => key == GovernanceSettingKeys.TenantDelegation.LockSmtp
            || EmailSettingDefinitions.All.Any(definition => definition.Key == key));

    internal static string NormalizeCanonicalKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim().ToLowerInvariant();
    }
}
