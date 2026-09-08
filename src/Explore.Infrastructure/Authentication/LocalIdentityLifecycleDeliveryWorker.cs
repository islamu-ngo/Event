// ABOUTME: Recovers durable native lifecycle pointers independently of the accepting HTTP request.
// ABOUTME: Never stores transport payloads or replays admitted Unknown operations after process failure.

using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Authentication;

public sealed class LocalIdentityLifecycleDeliveryWorker(
    IServiceScopeFactory scopes,
    ILogger<LocalIdentityLifecycleDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // Transport/DB exception text can contain recipient or secret material.
                // The native delivery ledger retains ownership and any admitted uncertainty.
                logger.LogError(new EventId(4720, "LocalLifecycleDeliveryScanFailed"),
                    "Local lifecycle delivery scan failed; durable operation state retained.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalIdentityLifecyclePointer> pending;
        await using (var read = scopes.CreateAsyncScope())
            pending = await read.ServiceProvider.GetRequiredService<ILocalIdentityLifecycleDeliveryStore>()
                .ReadPendingSynchronizationAsync(32, cancellationToken);
        // Mirror repair is independent of SMTP intent and the original public token lifetime.
        foreach (var pointer in pending)
        {
            try
            {
                await using var repair = scopes.CreateAsyncScope();
                var response = await repair.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ReconcileLocalIdentityLifecycleMirrorCommand(pointer), cancellationToken);
                if (!response.IsSuccess)
                    logger.LogWarning(new EventId(4721, "LocalLifecycleMirrorDeferred"), "Local lifecycle mirror repair deferred.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                logger.LogError(new EventId(4722, "LocalLifecycleMirrorFailed"),
                    "Local lifecycle mirror repair failed; durable receipt retained.");
            }
        }
        await using var delivery = scopes.CreateAsyncScope();
        await delivery.ServiceProvider.GetRequiredService<LocalIdentityLifecycleDeliveryProcessor>().DrainAsync(cancellationToken);
    }
}
