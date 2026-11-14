using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;

namespace Explore.Application.Services;

/// <summary>
/// Applies current tenant enrollment policy inside the original writer's final
/// admission boundary. Provider-specific verification remains with its producer.
/// </summary>
public sealed class TenantEnrollmentAdmissionOperation(
    ITenantEnrollmentAdmissionWriter writer,
    IVisitorAccessCapabilityResolver capabilities)
{
    public Task<TenantUser> ExecuteAsync(
        Func<CancellationToken, Task> authorizeProvider, CancellationToken cancellationToken) =>
        writer.AdmitAsync(async token =>
        {
            await authorizeProvider(token).ConfigureAwait(false);
            await AuthorizeNewMembershipAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    public async Task AuthorizeNewMembershipAsync(CancellationToken cancellationToken)
    {
        if (!await IsOpenAsync(capabilities, writer.TenantId, cancellationToken).ConfigureAwait(false))
            throw new TenantEnrollmentClosedException();
    }

    /// <summary>Evaluates new enrollment while the caller retains its settings and identity fences through commit.</summary>
    public static async Task<bool> IsOpenAsync(
        IVisitorAccessCapabilityResolver capabilities, Guid tenantId, CancellationToken cancellationToken)
    {
        var current = await capabilities.ResolveAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return current.UserRegistrationEnabled && current.AllowsAccountRequiredParticipation;
    }
}
