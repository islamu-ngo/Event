using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Management;
using MediatR;
using Microsoft.Extensions.Options;

namespace Explore.API.BackgroundServices;

public sealed class ManagedControlPlaneRegistrationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ManagedControlPlaneOptions> options,
    ILogger<ManagedControlPlaneRegistrationWorker> logger) : BackgroundService
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
                using var scope = scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(
                    new TriggerManagedControlPlaneRegistrationCommand(),
                    stoppingToken);
                if (result.Success)
                {
                    logger.LogInformation(
                        "Managed Control Plane registration {RegistrationAttemptId} is registered.",
                        result.RegistrationAttemptId);
                    return;
                }

                logger.LogWarning(
                    "Managed Control Plane registration remains {State} with failure code {FailureCode}.",
                    result.State,
                    result.FailureCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Managed Control Plane registration retry failed with {ExceptionType}.",
                    exception.GetType().Name);
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }
}
