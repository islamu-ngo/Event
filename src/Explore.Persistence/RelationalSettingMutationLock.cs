using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
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
        string settingKey,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingKey);
        return ExecuteManyAsync([settingKey], operation, cancellationToken);
    }

    public Task<T> ExecuteManyAsync<T>(
        IEnumerable<string> settingKeys,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingKeys);
        string[] orderedKeys = NormalizeSettingKeys(settingKeys);
        if (orderedKeys.Length == 0)
        {
            throw new ArgumentException(
                "At least one setting key is required.",
                nameof(settingKeys));
        }

        bool visitorPolicy = RequiresVisitorAccessFence(orderedKeys);
        bool resourcePolicy = RequiresEventResourceGovernanceFence(orderedKeys);
        if (RequiresEmailDeliveryFence(orderedKeys) || visitorPolicy || resourcePolicy)
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
                    token => visitorPolicy || resourcePolicy
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
        IEnumerable<IEnumerable<string>> settingKeyGroups,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingKeyGroups);
        ArgumentNullException.ThrowIfNull(operation);
        string[] orderedKeys = NormalizeOrderedSettingKeyGroups(
            settingKeyGroups);
        if (orderedKeys.Length == 0)
        {
            throw new ArgumentException(
                "At least one setting-key group is required.",
                nameof(settingKeyGroups));
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
                foreach (string settingKey in orderedKeys)
                {
                    if (_beforeOuterLockAcquisition is not null)
                    {
                        await _beforeOuterLockAcquisition(
                            settingKey,
                            cancellationToken);
                    }

                    leases.Add(await RelationalNamedLock.AcquireSessionAsync(
                        _dbContext,
                        $"explore:setting-mutation:{settingKey}",
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
        IReadOnlyList<string> settingKeys,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var leases = new List<IAsyncDisposable>(settingKeys.Count);
        try
        {
            IReadOnlySet<string>? outerOrderedKeys =
                _outerOrderedKeys.Value;
            foreach (string settingKey in settingKeys)
            {
                // The outer session/process lease already owns this resource. Reacquiring it
                // can self-block on SQLite or SQL Server and increments MySQL lock ownership.
                if (outerOrderedKeys?.Contains(settingKey) == true)
                {
                    continue;
                }

                leases.Add(await RelationalNamedLock.AcquireTransactionAsync(
                    _dbContext,
                    $"explore:setting-mutation:{settingKey}",
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

    internal static long ComputeStableLockKey(string settingKey) =>
        RelationalNamedLock.ComputeStableKey(
            $"explore:setting-mutation:{settingKey.Trim().ToLowerInvariant()}");

    internal static string[] NormalizeSettingKeys(
        IEnumerable<string> settingKeys)
    {
        string[] normalizedKeys = settingKeys
            .Select(NormalizeSettingKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (RequiresVisitorAccessFence(normalizedKeys))
            normalizedKeys = normalizedKeys.Concat(VisitorAccessCapabilityResolver.AuthoritySettingKeys)
                .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        if (RequiresEventResourceGovernanceFence(normalizedKeys))
            normalizedKeys = normalizedKeys.Concat(EventResourceSettingMutationGuard.Keys)
                .Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();

        return RequiresEmailDeliveryFence(normalizedKeys)
            ? [GovernanceSettingKeys.Email.DeliveryEnabled,
                .. normalizedKeys.Where(key => key != GovernanceSettingKeys.Email.DeliveryEnabled)]
            : normalizedKeys;
    }

    internal static string[] NormalizeOrderedSettingKeyGroups(
        IEnumerable<IEnumerable<string>> settingKeyGroups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (IEnumerable<string> group in settingKeyGroups)
        {
            ArgumentNullException.ThrowIfNull(group);
            foreach (string key in NormalizeSettingKeys(group))
            {
                if (seen.Add(key))
                {
                    ordered.Add(key);
                }
            }
        }

        if (RequiresEventResourceGovernanceFence(ordered))
        {
            ordered.RemoveAll(EventResourceSettingMutationGuard.Handles);
            ordered.InsertRange(0, EventResourceSettingMutationGuard.Keys.OrderBy(key => key, StringComparer.Ordinal));
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
        keys.Select(NormalizeSettingKey).Any(VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains);

    internal static bool RequiresEventResourceGovernanceFence(IEnumerable<string> keys) =>
        keys.Select(NormalizeSettingKey).Any(EventResourceSettingMutationGuard.Handles);

    internal static bool RequiresEmailDeliveryFence(IEnumerable<string> keys) =>
        keys.Select(NormalizeSettingKey).Any(key => key == GovernanceSettingKeys.TenantDelegation.LockSmtp
            || EmailSettingDefinitions.All.Any(definition => definition.Key == key));

    internal static string NormalizeSettingKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim().ToLowerInvariant();
    }
}
