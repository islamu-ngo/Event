using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Federation.Atproto.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure;

public sealed class PdsSyncDrainService(
    IServiceScopeFactory scopeFactory,
    IOptions<PdsSyncSettings> settings,
    TimeProvider timeProvider) : IPdsSyncDrainService
{
    private readonly PdsSyncSettings _settings = settings.Value;
    private readonly string _leaseOwner = BuildLeaseOwner();

    public async Task<PdsSyncDrainResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PdsSyncClaim> claims;
        using (IServiceScope claimScope = scopeFactory.CreateScope())
        {
            var repository = claimScope.ServiceProvider.GetRequiredService<IPdsSyncOutboxRepository>();
            claims = await repository.ClaimDueAsync(
                _settings.BatchSize,
                _leaseOwner,
                timeProvider.GetUtcNow().UtcDateTime,
                TimeSpan.FromSeconds(_settings.LeaseDurationSeconds),
                cancellationToken);
        }

        var delivered = 0;
        var failed = 0;
        var claimLost = 0;
        await Parallel.ForEachAsync(
            claims,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _settings.MaxConcurrency
            },
            async (claim, token) =>
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<AtprotoPdsDeliveryProcessor>();
                AtprotoPdsClaimResult result = await processor.ProcessAsync(
                    claim,
                    TimeSpan.FromSeconds(_settings.LeaseDurationSeconds),
                    token);
                switch (result.Outcome)
                {
                    case AtprotoPdsClaimOutcome.Delivered:
                        Interlocked.Increment(ref delivered);
                        break;
                    case AtprotoPdsClaimOutcome.DeliveryFailed:
                    case AtprotoPdsClaimOutcome.GateDenied:
                        Interlocked.Increment(ref failed);
                        break;
                    case AtprotoPdsClaimOutcome.ClaimLost:
                        Interlocked.Increment(ref claimLost);
                        break;
                }
            });

        return new PdsSyncDrainResult(claims.Count, delivered, failed, claimLost);
    }

    private static string BuildLeaseOwner()
    {
        string value = $"pds-{Environment.MachineName}-{Environment.ProcessId}-{Guid.CreateVersion7():N}";
        return value.Length <= 200 ? value : value[..200];
    }
}
