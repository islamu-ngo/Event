using Explore.Application.Contracts.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Operations;

/// <summary>
/// Defers unused human dependencies on machine/internal branches. Identity preparation belongs
/// before consumer construction; invocation only resolves the already-bound explicit port.
/// </summary>
internal sealed class DeferredHumanAdministratorCommitAuthority(IServiceProvider services)
    : IInstanceAdministratorCommitAuthority, ITenantAdministratorCommitAuthority, IHumanSubjectCommitAuthority
{
    public Task<bool> IsSubjectCurrentForCommitAsync(Guid actor, CancellationToken cancellationToken) =>
        services.GetRequiredService<IHumanSubjectCommitAuthority>()
            .IsSubjectCurrentForCommitAsync(actor, cancellationToken);

    public Task<bool> IsInstanceAdministratorForCommitAsync(Guid actor, CancellationToken cancellationToken) =>
        services.GetRequiredService<IInstanceAdministratorCommitAuthority>()
            .IsInstanceAdministratorForCommitAsync(actor, cancellationToken);

    public Task<bool> IsTenantAdministratorForCommitAsync(
        Guid actor, Guid tenant, CancellationToken cancellationToken) =>
        services.GetRequiredService<ITenantAdministratorCommitAuthority>()
            .IsTenantAdministratorForCommitAsync(actor, tenant, cancellationToken);
}
