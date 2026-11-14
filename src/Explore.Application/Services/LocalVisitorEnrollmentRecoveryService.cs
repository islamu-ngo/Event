using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Services;

public sealed class LocalVisitorEnrollmentRecoveryService(
    IVisitorEnrollmentRecoveryReader reader,
    ILocalVisitorEnrollmentRecoveryScopeFactory scopes,
    TimeProvider clock) : ILocalVisitorEnrollmentRecoveryService
{
    public async Task<VisitorEnrollmentRecoveryResult> RecoverOnceAsync(CancellationToken cancellationToken)
    {
        var candidates = await reader.ReadCandidatesAsync(clock.GetUtcNow().UtcDateTime,
            IVisitorEnrollmentRecoveryReader.MaximumCandidates, cancellationToken).ConfigureAwait(false);
        int settled = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var selected = await scopes.CreateAsync(candidate.TenantId, cancellationToken)
                .ConfigureAwait(false);
            var store = selected.Services.GetRequiredService<ILocalVisitorEnrollmentStore>();
            if (store.TenantId != candidate.TenantId)
                throw new UnauthorizedAccessException("Visitor recovery lost its selected tenant.");
            if (await store.RecoverAsync(candidate, cancellationToken).ConfigureAwait(false))
                settled++;
        }
        return new(candidates.Length, settled);
    }
}
