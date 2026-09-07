using Explore.Application.Contracts.Persistence;
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
        return canonicalSettingKeys
            .Select(key =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                return key.Trim().ToLowerInvariant();
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
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

        return ordered.ToArray();
    }
}
