using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Binds public setting mutations to the actual scoped issuer and its selected data owner.
/// This adapter never manufactures admission from a subject, transaction or import label.
/// </summary>
public sealed class HumanSettingsDataBoundary(IServiceProvider selectedServices)
{
    internal IOrdinarySettingsSource? OrdinarySource =>
        selectedServices.GetRequiredService<IHumanConfigurationImportAdmission>().CurrentProof is not null
            ? null : selectedServices.GetRequiredService<IHumanSettingsMutationPreparation>().CurrentOrdinarySource;

    internal IOrdinarySettingsMutationStore OrdinaryStore =>
        selectedServices.GetRequiredService<IOrdinarySettingsMutationStore>();

    public async Task RequireAsync(
        ExploreDbContext dataOwner, SettingScope scope, Guid? tenantId, Guid? userId,
        string key, HumanSettingsMutationKind kind, string? value = null, bool? isLocked = null,
        Guid? auditActorId = null, CancellationToken cancellationToken = default)
    {
        var lease = RequireOriginalOwner(dataOwner, auditActorId);
        var import = selectedServices.GetRequiredService<IHumanConfigurationImportAdmission>();
        if (import.CurrentProof is not null)
            await import.RequireCurrentOwnedAsync(scope, tenantId, userId, key, kind, value, isLocked, cancellationToken);
        else
        {
            if (dataOwner.IsConfigurationInstanceImportOwner)
                throw new UnauthorizedAccessException("Settings imports require the original active exact-effect producer.");
            await selectedServices.GetRequiredService<IHumanSettingsMutationPreparation>()
                .RequireCurrentOwnedAsync(scope, tenantId, userId, key, kind, value, isLocked, cancellationToken);
        }
        lease.RequireOriginalOwner(dataOwner);
    }

    internal IOrdinarySettingsMutationStore RequireOriginalOrdinaryWriter(
        ExploreDbContext dataOwner, IOrdinarySettingsSource source, Guid? auditActorId)
    {
        RequireOriginalOwner(dataOwner, auditActorId);
        if (!ReferenceEquals(OrdinarySource, source))
            throw new UnauthorizedAccessException("The ordinary writer requires its original active source.");
        // The private writer checks the exact effect and current sources immediately before SQL.
        return OrdinaryStore;
    }

    private ExploreDbContextExecutionLease RequireOriginalOwner(ExploreDbContext dataOwner, Guid? auditActorId)
    {
        var original = selectedServices.GetRequiredService<ExploreDbContext>();
        if (!ReferenceEquals(dataOwner, original))
            throw new UnauthorizedAccessException("Settings data must use the original selected owner.");
        var lease = selectedServices.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.RequireOriginalOwner(dataOwner);
        if (lease.HumanScope is null || !dataOwner.HasHumanCommitContextLease
            || !ReferenceEquals(lease.HumanScope.Subject, dataOwner.HumanCommitAuditSubject)
            || auditActorId.HasValue && auditActorId != lease.HumanScope.Subject.PrincipalId)
            throw new UnauthorizedAccessException("Settings data requires the original human and audit actor.");
        return lease;
    }
}
