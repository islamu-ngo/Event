using System.Collections.Immutable;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Delays concrete dependency construction until the original human owner is selected.
/// Does not open a transaction, grant authority or replay a failed attempt.
/// </summary>
internal sealed class SelectedEventResourceSettingsWriter(IServiceProvider services) : IEventResourceSettingsWriter
{
    public async Task<EventResourceSettingsWriteResult> ApplyCurrentHumanAsync(
        ImmutableArray<EventResourceSettingMutation> mutations, SettingsMutationOperation operation,
        Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        if (mutations.IsDefaultOrEmpty
            || operation is not (SettingsMutationOperation.InstanceSettings or SettingsMutationOperation.TenantSettings)
            || mutations.Select(mutation => mutation.TenantId).Distinct().Count() != 1
            || (operation == SettingsMutationOperation.TenantSettings) != mutations[0].TenantId.HasValue)
            throw new InvalidOperationException("Direct resource services require one exact Instance or Tenant action.");
        await using var selected = await services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new PolicyMutationScope(mutations[0].TenantId), cancellationToken);
        var subject = selected.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject
            ?? throw new UnauthorizedAccessException("Resource settings require the original authenticated owner.");
        return await selected.Services.GetRequiredService<IEventResourceSettingsWriter>()
            .ApplyAsync(mutations, subject, operation, actorUserId, cancellationToken);
    }

    public Task<EventResourceSettingsWriteResult> ApplyAsync(
        ImmutableArray<EventResourceSettingMutation> mutations, HumanCommitSubject subject,
        SettingsMutationOperation operation, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        var context = services.GetRequiredService<ExploreDbContext>();
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.RequireOriginalOwner(context);
        if (!ReferenceEquals(lease.HumanScope?.Subject, subject))
            throw new UnauthorizedAccessException("Resource settings must retain the selected human.");
        var writer = new EventResourceSettingsWriter(context, services.GetRequiredService<ISettingMutationLock>(),
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IInstanceAdministratorCommitAuthority>(),
            services.GetRequiredService<ITenantAdministratorCommitAuthority>(),
            services.GetRequiredService<IHumanActionCommitFinalizer>(), subject,
            services.GetRequiredService<IHumanSettingsMutationPreparation>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<Explore.Application.Features.ConfigurationManifest.Importing.IHumanConfigurationImportAdmission>());
        return MixedSettingsBatchStore.Current(context) is { } mixed
            ? mixed.ExecuteResourceAsync(this, writer, mutations, subject, operation, actorUserId, cancellationToken)
            : writer.ApplyAsync(mutations, subject, operation, actorUserId, cancellationToken);
    }

    public Task<EventResourceSettingsWriteResult> ApplyInternalAsync(
        ImmutableArray<EventResourceSettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (services.GetRequiredService<ExploreDbContextExecutionLease>().HumanScope is not null)
            throw new UnauthorizedAccessException("A selected human owner cannot enter internal resource setup.");
        var writer = new EventResourceSettingsWriter(services.GetRequiredService<ExploreDbContext>(),
            services.GetRequiredService<ISettingMutationLock>(), services.GetRequiredService<IUnitOfWork>());
        return writer.ApplyInternalAsync(mutations, actorUserId, cancellationToken);
    }
}
