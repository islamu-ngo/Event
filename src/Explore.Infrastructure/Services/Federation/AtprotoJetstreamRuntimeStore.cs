using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Requests.Commands;
using Explore.Application.Services.Federation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services.Federation;

public interface IAtprotoJetstreamRuntimeStore
{
    Task<IReadOnlyList<Guid>> ResolveEnabledTenantIdsAsync(CancellationToken cancellationToken);
    Task<AtprotoJetstreamClaim?> TryClaimAsync(
        string service,
        string owner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
    Task<bool> TryRenewAsync(
        AtprotoJetstreamClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken);
    Task<bool> TryApplyAndAdvanceAsync(
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken);
    Task<AtprotoPdsRecoveryResult> ReconcilePdsSnapshotsAsync(
        ReconcileAtprotoPdsSnapshotsCommand command,
        CancellationToken cancellationToken);
}

internal sealed class AtprotoJetstreamRuntimeStore(IServiceScopeFactory scopeFactory)
    : IAtprotoJetstreamRuntimeStore
{
    public async Task<IReadOnlyList<Guid>> ResolveEnabledTenantIdsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<AtprotoJetstreamTenantPresentationResolver>()
            .ResolveEnabledTenantIdsAsync(cancellationToken);
    }

    public async Task<AtprotoJetstreamClaim?> TryClaimAsync(
        string service,
        string owner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAtprotoJetstreamRepository>()
            .TryClaimAsync(service, owner, claimedAt, leaseDuration, cancellationToken);
    }

    public async Task<bool> TryRenewAsync(
        AtprotoJetstreamClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAtprotoJetstreamRepository>()
            .TryRenewAsync(claim, observedAt, leaseExpiresAt, cancellationToken);
    }

    public async Task<bool> TryApplyAndAdvanceAsync(
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        bool applied = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ImportAtprotoFederatedEventCommand(request), cancellationToken);
        if (applied
            && AffectsEventDiscovery(request)
            && scope.ServiceProvider.GetService<IAtprotoDiscoveryCacheInvalidator>() is { } cacheInvalidator)
        {
            await cacheInvalidator.InvalidateAsync(cancellationToken);
        }

        return applied;
    }

    public async Task<AtprotoPdsRecoveryResult> ReconcilePdsSnapshotsAsync(
        ReconcileAtprotoPdsSnapshotsCommand command,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        AtprotoPdsRecoveryResult result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(command, cancellationToken);
        if (result.Outcome == AtprotoPdsRecoveryOutcome.Completed
            && scope.ServiceProvider.GetService<IAtprotoDiscoveryCacheInvalidator>() is { } cacheInvalidator)
        {
            await cacheInvalidator.InvalidateAsync(cancellationToken);
        }

        return result;
    }

    private static bool AffectsEventDiscovery(AtprotoJetstreamApplyRequest request) =>
        request.EventProjection is not null
        || request.EventProjectionInvalidation is not null
        // A purge removes projections wholesale, so cached discovery results must not survive it.
        || request.AccountPurge is not null
        || string.Equals(
            request.Record?.Collection,
            AtprotoJetstreamConstants.EventCollection,
            StringComparison.Ordinal);
}
