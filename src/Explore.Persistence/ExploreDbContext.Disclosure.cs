using System.Runtime.ExceptionServices;
using Explore.Application.Exceptions;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private EventDiscoveryDisclosureMutationScope? _disclosureMutations;

    internal EventDiscoveryDisclosureMutationScope DisclosureMutations =>
        _disclosureMutations ??= new(this);

    internal async Task<T> ExecuteDisclosureMutationAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (!Database.IsRelational())
            return await operation(cancellationToken);

        void RequireCustody()
        {
            HumanSecuritySourceGate.RequireCurrent(this);
            EventDiscoveryEnrollmentGate.RequireCurrent(this);
        }

        if (Database.CurrentTransaction is not null)
        {
            RequireCustody();
            T nested = await operation(cancellationToken);
            RequireCustody();
            return nested;
        }

        IAsyncDisposable? human = null;
        IAsyncDisposable? discovery = null;
        Exception? failure = null;
        bool settled = false;
        T result = default!;
        try
        {
            // The callback has not materialized its sources yet. Take the earlier
            // security-source resource first; neither gate grants business authority.
            if (!HumanSecuritySourceGate.TryRequireCurrent(this))
            {
                if (EventDiscoveryEnrollmentGate.TryRequireCurrent(this))
                    throw new InvalidOperationException("discovery_source_gate_order_invalid");
                human = await new HumanSecuritySourceGate(this).AcquireAsync(cancellationToken);
            }
            if (!EventDiscoveryEnrollmentGate.TryRequireCurrent(this))
                discovery = await new EventDiscoveryEnrollmentGate(this).AcquireAsync(cancellationToken);
            RequireCustody();
            result = await new EfCoreUnitOfWork(this).ExecuteSerializableAsync(async token =>
            {
                RequireCustody();
                T changed = await operation(token);
                RequireCustody();
                return changed;
            }, cancellationToken);
            settled = true;
            RequireCustody();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        failure = await ReleaseOwnedDisclosureGatesAsync(discovery, human, failure, settled);
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private static async Task<Exception?> ReleaseOwnedDisclosureGatesAsync(
        IAsyncDisposable? discovery, IAsyncDisposable? human, Exception? primary, bool settled)
    {
        List<Exception> cleanup = [];
        foreach (var owned in new[] { discovery, human })
        {
            if (owned is null)
                continue;
            try
            {
                await owned.DisposeAsync();
            }
            catch (Exception exception)
            {
                cleanup.Add(exception);
            }
        }
        Exception? failure = primary;
        if (cleanup.Count != 0)
        {
            if (primary is not null)
                cleanup.Insert(0, primary);
            failure = new AggregateException("Disclosure operation and owned gate cleanup evidence.", cleanup);
        }
        if (primary is HumanMutationOutcomeUnknownException)
            return cleanup.Count == 0 ? primary : new HumanMutationOutcomeUnknownException(failure!);
        if (failure is not null && settled)
            return new InvalidOperationException("discovery_committed_gate_cleanup_failed", failure);
        return failure;
    }

    internal Task ExecuteDisclosureMutationAsync(
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        ExecuteDisclosureMutationAsync(async token =>
        {
            await operation(token);
            return true;
        }, cancellationToken);

    internal Task FlushDisclosureAsync(CancellationToken cancellationToken) =>
        _disclosureMutations?.FlushAsync(cancellationToken) ?? Task.CompletedTask;

    internal void ResetDisclosureMutations() => _disclosureMutations?.Reset();
}
