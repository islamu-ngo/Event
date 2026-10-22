using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Explore.Persistence.Security;
using static Explore.Persistence.Services.EmailDeliverySettingsMutationOwner;

namespace Explore.Persistence.Services;

/// <summary>
/// Trusted nonhuman setup/lifecycle writes on the selected email owner. The separate type
/// neither implements a human port nor accepts confirmation/audit identity as authority.
/// Native email-purpose grants and the original full SMTP lease still bound every write.
/// </summary>
public sealed class InternalEmailDeliverySettingsWriter(
    ExploreDbContext context, ISettingMutationLock mutationLock, IUnitOfWork unitOfWork)
    : IInternalEmailDeliverySettingsWriter
{
    private readonly EmailDeliverySettingsMutationOwner owner = new(context, mutationLock, unitOfWork);

    public Task<EmailDeliverySettingsWriteResult> ApplyInternalAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (context.IsConfigurationInstanceImportOwner || context.HasHumanCommitContextLease)
            throw new InvalidOperationException("Configuration import requires its original human email action.");
        if (actorUserId == Guid.Empty || mutations.IsDefault || mutations.Any(mutation => !IsValid(mutation))
            || mutations.Select(mutation => (mutation.TenantId, mutation.Key)).Distinct().Count() != mutations.Length)
            return Task.FromResult(Result(EmailDeliverySettingsWriteStatus.InvalidMutation));
        if (mutations.IsEmpty)
            return Task.FromResult(Result(EmailDeliverySettingsWriteStatus.NoChange));
        return ExecuteWithHumanSourceGateAsync(async token =>
        {
            var prepared = await owner.PrepareAsync(mutations, actorUserId, token);
            if (prepared.Before is { } before && prepared.After is { } after
                && (before.Instance.Enabled && !after.Instance.Enabled
                    || before.Tenants.Any(pair => pair.Value.Enabled && !after.Tenants[pair.Key].Enabled)))
                return Result(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
            return await owner.PersistAsync(prepared, actorUserId, token);
        }, cancellationToken);
    }

    public Task<EmailDeliverySettingsWriteResult> DisableInternalAsync(
        Guid? tenantId, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        if (context.IsConfigurationInstanceImportOwner || context.HasHumanCommitContextLease)
            throw new InvalidOperationException("Configuration import requires its original human email action.");
        if (tenantId == Guid.Empty || actorUserId == Guid.Empty)
            return Task.FromResult(Result(EmailDeliverySettingsWriteStatus.InvalidMutation));
        return ExecuteWithHumanSourceGateAsync(async token =>
        {
            var prepared = await owner.PrepareAsync(
                [new(tenantId, GovernanceSettingKeys.Email.DeliveryEnabled,
                    EmailDeliverySettingMutationKind.SetValue, Value: "false")], actorUserId, token);
            return await owner.PersistAsync(prepared, actorUserId, token);
        }, cancellationToken);
    }

    private async Task<EmailDeliverySettingsWriteResult> ExecuteWithHumanSourceGateAsync(
        Func<CancellationToken, Task<EmailDeliverySettingsWriteResult>> operation, CancellationToken cancellationToken)
    {
        // A caller-owned transaction must already retain the original pre-BEGIN
        // gate and ordered SMTP keys. Never retrofit either lock inside that owner.
        if (context.Database.CurrentTransaction is not null)
            HumanSecuritySourceGate.RequireCurrent(context);
        await using var human = !HumanSecuritySourceGate.TryRequireCurrent(context)
            ? await new HumanSecuritySourceGate(context).AcquireAsync(cancellationToken) : null;
        HumanSecuritySourceGate.RequireCurrent(context);
        return await owner.ExecuteAsync(async token =>
        {
            HumanSecuritySourceGate.RequireCurrent(context);
            var result = await operation(token);
            HumanSecuritySourceGate.RequireCurrent(context);
            return result;
        }, cancellationToken);
    }
}
