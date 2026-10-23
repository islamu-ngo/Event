using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;

namespace Explore.Application.Features.ConfigurationManifest.Importing;

/// <summary>Retains cooperative gates on the selected import owner, never authorization.</summary>
internal static class ConfigurationImportCoordination
{
    internal static async Task<T> ExecuteAsync<T>(
        Func<IHumanSecuritySourceGate> humanFactory,
        Func<IEventDiscoveryEnrollmentGate> discoveryFactory,
        Func<Action, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var human = humanFactory();
        var discovery = discoveryFactory();
        IAsyncDisposable? ownedHuman = null;
        IAsyncDisposable? ownedDiscovery = null;
        Exception? primary = null;
        bool completed = false;
        T result = default!;
        void RequireCurrent()
        {
            human.RequireCurrent();
            discovery.RequireCurrent();
        }
        try
        {
            if (!human.TryRequireCurrent())
            {
                if (discovery.TryRequireCurrent())
                    throw new InvalidOperationException("Import cannot acquire Human behind retained Discovery custody.");
                ownedHuman = await human.AcquireAsync(cancellationToken);
            }
            if (!discovery.TryRequireCurrent())
                ownedDiscovery = await discovery.AcquireAsync(cancellationToken);
            RequireCurrent();
            result = await operation(RequireCurrent, cancellationToken);
            completed = true;
            RequireCurrent();
        }
        catch (Exception failure) { primary = failure; }

        List<Exception> cleanup = [];
        foreach (var owned in new[] { ownedDiscovery, ownedHuman })
        {
            if (owned is null) continue;
            try { await owned.DisposeAsync(); }
            catch (Exception failure) { cleanup.Add(failure); }
        }
        if (cleanup.Count != 0)
        {
            if (primary is not null) cleanup.Insert(0, primary);
            var combined = new AggregateException("Import operation and coordination cleanup evidence.", cleanup);
            if (primary is HumanMutationOutcomeUnknownException)
                throw new HumanMutationOutcomeUnknownException(combined);
            if (primary is TransactionCommittedCleanupException)
                throw new TransactionCommittedCleanupException(combined);
            throw new ConfigurationImportCoordinationCleanupException(combined);
        }
        if (primary is not null)
        {
            if (completed) throw new ConfigurationImportCoordinationCleanupException(primary);
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        return result;
    }
}

/// <summary>Cleanup uncertainty cannot become a contradictory persisted apply-failure receipt.</summary>
internal sealed class ConfigurationImportCoordinationCleanupException(Exception innerException)
    : Exception("Import coordination cleanup failed; retain the original settlement evidence.", innerException);
