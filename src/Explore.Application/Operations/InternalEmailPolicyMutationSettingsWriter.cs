using System.Collections.Immutable;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Operations;

/// <summary>Selects the nonhuman email purpose before constructing its concrete mutation owner.</summary>
public sealed class InternalEmailPolicyMutationSettingsWriter(
    IPolicyMutationExecutionScopeFactory scopes,
    Func<IServiceProvider, IInternalEmailDeliverySettingsWriter> resolveWriter)
    : IInternalEmailDeliverySettingsWriter
{
    public async Task<EmailDeliverySettingsWriteResult> ApplyInternalAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (mutations.IsDefault || mutations.Any(mutation => mutation is null || mutation.TenantId == Guid.Empty))
            return new(EmailDeliverySettingsWriteStatus.InvalidMutation, []);
        if (mutations.IsEmpty)
            return new(EmailDeliverySettingsWriteStatus.NoChange, []);
        Guid? target = mutations.Any(mutation => mutation.TenantId is null) ? null : mutations[0].TenantId;
        if (target.HasValue && mutations.Any(mutation => mutation.TenantId != target))
            return new(EmailDeliverySettingsWriteStatus.InvalidMutation, []);
        await using var scope = await scopes.CreateAsync(target is { } tenantId
            ? PolicyMutationScope.EmailTenant(tenantId) : PolicyMutationScope.EmailInstance, cancellationToken);
        return await resolveWriter(scope.Services).ApplyInternalAsync(mutations, actorUserId, cancellationToken);
    }

    public async Task<EmailDeliverySettingsWriteResult> DisableInternalAsync(
        Guid? tenantId, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            return new(EmailDeliverySettingsWriteStatus.InvalidMutation, []);
        await using var scope = await scopes.CreateAsync(tenantId is { } target
            ? PolicyMutationScope.EmailTenant(target) : PolicyMutationScope.EmailInstance, cancellationToken);
        return await resolveWriter(scope.Services).DisableInternalAsync(tenantId, actorUserId, cancellationToken);
    }
}
