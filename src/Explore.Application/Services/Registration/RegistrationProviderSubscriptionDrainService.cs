using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

/// <summary>The scheduler owns admission only; each original claim executes in a new tenant Runtime scope.</summary>
public sealed class RegistrationProviderSubscriptionDrainService(
    IRegistrationProviderSubscriptionClaimQueue queue,
    IRegistrationProviderSubscriptionExecutionScopeFactory executionScopes,
    TimeProvider clock)
{
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken)
    {
        int processed = 0;
        foreach (var operation in new[] { RegistrationProviderSubscriptionOperation.Renewal, RegistrationProviderSubscriptionOperation.Sweep })
        {
            var claims = await queue.ClaimAsync(operation, 10, clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromMinutes(2), cancellationToken);
            foreach (var claim in claims)
            {
                await using var scope = await executionScopes.CreateAsync(claim.TenantId, cancellationToken);
                if (await scope.ProcessAsync(claim, operation, cancellationToken))
                    processed++;
            }
        }
        return processed;
    }
}
