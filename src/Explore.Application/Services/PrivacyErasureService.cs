using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Services;

/// <summary>Retains the original Global owner and ordered source custody around retained erasure.</summary>
public sealed class PrivacyErasureService(
    IServiceProvider provider,
    IPolicyMutationExecutionScopeFactory scopes) : IPrivacyErasureService
{
    public Task<PrivacyErasureStartDto> EraseUserAsync(
        Guid userId, Guid intentId, CancellationToken cancellationToken) =>
        ExecuteMutationAsync((core, token) => core.EraseUserAsync(userId, intentId, token), cancellationToken);

    public async Task ReplayPendingAsync(CancellationToken cancellationToken) =>
        _ = await ExecuteMutationAsync(async (core, token) =>
        {
            await core.ReplayPendingAsync(token);
            return true;
        }, cancellationToken);

    public Task<Guid?> AuthenticateReceiptAsync(string receipt, CancellationToken cancellationToken) =>
        provider.GetRequiredService<RetainedAuthorityPrivacyErasureWorkflow>()
            .AuthenticateReceiptAsync(receipt, cancellationToken);

    public Task<PrivacyErasureStatusDto?> GetStatusAsync(Guid intentId, CancellationToken cancellationToken) =>
        provider.GetRequiredService<RetainedAuthorityPrivacyErasureWorkflow>()
            .GetStatusAsync(intentId, cancellationToken);

    private async Task<TResult> ExecuteMutationAsync<TResult>(
        Func<RetainedAuthorityPrivacyErasureWorkflow, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        IPolicyMutationExecutionScope? selected = null;
        IHumanSecuritySourceGate? human = null;
        IEventDiscoveryEnrollmentGate? discovery = null;
        IAsyncDisposable? ownedHuman = null;
        IAsyncDisposable? ownedDiscovery = null;
        bool humanEstablished = false;
        bool discoveryEstablished = false;
        List<Exception> failures = [];
        TResult result = default!;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            selected = await scopes.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
            human = selected.Services.GetRequiredService<IHumanSecuritySourceGate>();
            discovery = selected.Services.GetRequiredService<IEventDiscoveryEnrollmentGate>();
            if (!human.TryRequireCurrent())
            {
                if (discovery.TryRequireCurrent())
                    throw new InvalidOperationException("discovery_source_gate_order_invalid");
                ownedHuman = await human.AcquireAsync(cancellationToken);
            }
            human.RequireCurrent();
            humanEstablished = true;
            if (!discovery.TryRequireCurrent())
                ownedDiscovery = await discovery.AcquireAsync(cancellationToken);
            discovery.RequireCurrent();
            discoveryEstablished = true;

            // Resolving the core earlier would bind its repositories to the caller's tenant owner.
            var core = selected.Services.GetRequiredService<RetainedAuthorityPrivacyErasureWorkflow>();
            result = await operation(core, cancellationToken);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        if (humanEstablished)
        {
            try { human!.RequireCurrent(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (discoveryEstablished)
        {
            try { discovery!.RequireCurrent(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        foreach (IAsyncDisposable? owned in new IAsyncDisposable?[] { ownedDiscovery, ownedHuman, selected })
        {
            if (owned is null) continue;
            try { await owned.DisposeAsync(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
        {
            var aggregate = new AggregateException("Privacy erasure and original owner cleanup failures.", failures);
            if (failures[0] is HumanMutationOutcomeUnknownException)
                throw new HumanMutationOutcomeUnknownException(aggregate);
            if (failures[0] is TransactionCommittedCleanupException)
                throw new TransactionCommittedCleanupException(aggregate);
            throw aggregate;
        }
        return result;
    }
}
