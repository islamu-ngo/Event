using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Management;
using Microsoft.Extensions.Options;

namespace Explore.API.BackgroundServices;

public sealed class ManagedControlPlaneRegistrationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ManagedControlPlaneOptions> options,
    ILogger<ManagedControlPlaneRegistrationWorker> logger,
    IAgentBrowserWorkAdmission? agentWorkAdmission = null) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var work = agentWorkAdmission is null ? null : await agentWorkAdmission.EnterAsync(stoppingToken);
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TriggerManagedControlPlaneRegistrationCommand, TriggerManagedRegistrationResultDto>>();
                var result = await handler.ExecuteAsync(
                    new TriggerManagedControlPlaneRegistrationCommand(),
                    stoppingToken);
                if (result.Success)
                {
                    logger.LogInformation("Managed Control Plane registration completed.");
                    return;
                }

                logger.LogWarning(
                    "Managed Control Plane registration is pending. FailureCategory={FailureCategory}",
                    "registration_pending");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                logger.LogWarning(
                    "Managed Control Plane registration retry failed. FailureCategory={FailureCategory}",
                    "registration_retry_failed");
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }
}
