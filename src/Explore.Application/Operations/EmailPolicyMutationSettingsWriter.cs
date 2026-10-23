using System.Collections.Immutable;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Settings;

namespace Explore.Application.Operations;

/// <summary>
/// Direct service entrypoints acquire purpose before resolving the concrete writer.
/// Native command owners borrow their already selected context and transaction.
/// </summary>
public sealed class EmailPolicyMutationSettingsWriter(
    IPolicyMutationExecutionScopeFactory scopes,
    ITenantContextAccessor tenantContext,
    Func<IServiceProvider, IEmailDeliverySettingsWriter> resolveWriter)
    : IEmailDeliverySettingsWriter, IMixedEmailDeliverySettingsWriter
{
    public Task<EmailDeliverySettingsWriteResult> ApplyMixedAsync(
        MixedSettingsBatchMutation mutation, CancellationToken cancellationToken = default)
    {
        mutation.RequireActive();
        var services = (scopes as IMixedSettingsExecutionScopeAccess
            ?? throw new UnauthorizedAccessException("The original mixed execution scope is required."))
            .BorrowMixedSettings(mutation);
        var writer = resolveWriter(services) as IMixedEmailDeliverySettingsWriter
            ?? throw new UnauthorizedAccessException("The selected writer has no mixed Email capability.");
        return writer.ApplyMixedAsync(mutation, cancellationToken);
    }

    public Task<EmailDeliverySettingsWriteResult> ApplyAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(mutations, actorUserId, instanceTenantAction: false, cancellationToken);

    public Task<EmailDeliverySettingsWriteResult> ApplyInstanceTenantAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(mutations, actorUserId, instanceTenantAction: true, cancellationToken);

    private async Task<EmailDeliverySettingsWriteResult> ApplyCoreAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        bool instanceTenantAction, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || mutations.IsDefault
            || mutations.Any(mutation => mutation is null || mutation.TenantId == Guid.Empty))
            return new(EmailDeliverySettingsWriteStatus.InvalidMutation, []);
        if (mutations.IsEmpty)
            return new(EmailDeliverySettingsWriteStatus.NoChange, []);

        Guid? target = mutations.Any(mutation => mutation.TenantId is null) ? null : mutations[0].TenantId;
        if (instanceTenantAction && !target.HasValue
            || target.HasValue && (mutations.Any(mutation => mutation.TenantId != target)
                || !instanceTenantAction && tenantContext.TenantId != target))
            return new(EmailDeliverySettingsWriteStatus.InvalidMutation, []);

        await using var scope = await scopes.CreateAsync(
            target is { } tenantId ? PolicyMutationScope.EmailTenant(tenantId) : PolicyMutationScope.EmailInstance,
            cancellationToken);
        return instanceTenantAction
            ? await resolveWriter(scope.Services).ApplyInstanceTenantAsync(mutations, actorUserId, cancellationToken)
            : await resolveWriter(scope.Services).ApplyAsync(mutations, actorUserId, cancellationToken);
    }

    public async Task<EmailDeliverySettingsWriteResult> DisableAsync(
        EmailDeliveryDisableConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        if (confirmation.TenantId == Guid.Empty
            || confirmation.TenantId.HasValue && confirmation.TenantId != tenantContext.TenantId)
            return new(EmailDeliverySettingsWriteStatus.InvalidConfirmation, []);
        await using var scope = await scopes.CreateAsync(
            confirmation.TenantId is { } tenantId
                ? PolicyMutationScope.EmailTenant(tenantId) : PolicyMutationScope.EmailInstance,
            cancellationToken);
        return await resolveWriter(scope.Services).DisableAsync(confirmation, cancellationToken);
    }
}
