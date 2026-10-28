using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationNoticeCleanupRepository
{
    private async Task<CleanupAuthority?> AcquireAuthorityAsync(
        Guid tenantId, ApiKeyPrincipalContext principal, CancellationToken token)
    {
        if (machine.Current != principal || !await NativeTenantAsync(tenantId, token)
            || principal.TenantId is { } credentialTenant && credentialTenant != tenantId) return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        var binding = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        var key = await keys.GetByKeyIdForAuthentication(principal.KeyId, token);
        if (host is null || binding is null || key is null
            || binding.HostBindingId != host.Id || binding.ServicePrincipalId != host.MaintenanceServicePrincipalId
            || binding.AuthorityTenantId != principal.TenantId || binding.OwnerType != principal.OwnerType
            || binding.OwnerId != principal.OwnerId) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, binding.ServicePrincipalId, token);
        var actors = await context.Actors.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.ServicePrincipalId == binding.ServicePrincipalId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in actors)
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(context, key.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMachineSubjectBinding>(context, binding.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token);
        if (tenant is null) return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, token);
        if (epoch is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);
        var acquired = new CleanupAuthority(host, binding.Id, binding.Revision, key.Id, epoch.Id, epoch.Revision);
        context.ModerationTrustDomainId = host.TrustDomainId;
        if (!await AuthorityCurrentAsync(acquired, tenantId, principal, token)) return null;
        return acquired;
    }

    private async Task<bool> AuthorityCurrentAsync(
        CleanupAuthority authority, Guid tenantId, ApiKeyPrincipalContext principal, CancellationToken token)
    {
        if (machine.Current != principal || !await NativeTenantAsync(tenantId, token)
            || context.ModerationTrustDomainId != authority.Host.TrustDomainId)
            return false;
        var current = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        var binding = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        var key = await keys.GetByKeyIdForAuthentication(principal.KeyId, token);
        return current is not null && current.Id == authority.Host.Id
            && current.TrustDomainId == authority.Host.TrustDomainId
            && current.SolutionId == authority.Host.SolutionId && current.InstanceId == authority.Host.InstanceId
            && current.MaintenanceServicePrincipalId == authority.Host.MaintenanceServicePrincipalId
            && binding is not null && binding.Id == authority.BindingId && binding.Revision == authority.BindingRevision
            && binding.HostBindingId == current.Id && binding.ServicePrincipalId == current.MaintenanceServicePrincipalId
            && binding.AuthorityTenantId == principal.TenantId && binding.OwnerType == principal.OwnerType
            && binding.OwnerId == principal.OwnerId && key is not null && key.Id == authority.KeyId
            && await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == tenantId && row.TenantStatus.IsActiveState, token)
            && await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == authority.EpochId && row.HostBindingId == current.Id && row.TenantId == tenantId
                && row.Revision == authority.EpochRevision && row.Revision > 0, token);
    }

    private async Task<bool> NativeTenantAsync(Guid tenantId, CancellationToken token)
    {
        if (context.Database.CurrentTransaction is null || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId != tenantId
            || context.Database.CurrentTransaction.GetDbTransaction().Connection != context.Database.GetDbConnection())
            return false;
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return value is string setting && Guid.TryParse(setting, out var actual) && actual == tenantId;
    }

    private sealed record CleanupAuthority(
        ModerationHostBinding Host, Guid BindingId, long BindingRevision, Guid KeyId, Guid EpochId, long EpochRevision);
}
