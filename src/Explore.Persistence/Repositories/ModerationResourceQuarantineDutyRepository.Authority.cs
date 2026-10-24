using System.Data;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationResourceQuarantineDutyRepository
{
    private async Task<DutyAuthority> AcquireAuthorityAsync(
        Guid tenantId, ApiKeyPrincipalContext principal, CancellationToken token)
    {
        if (machine.Current != principal || principal.CredentialSource != ApiKeyCredentialSource.Persisted
            || principal.TenantId is { } credentialTenant && credentialTenant != tenantId
            || !await NativeTenantAsync(tenantId, token))
            throw new DutyUnavailable();
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new DutyUnavailable();
        var binding = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token)
            ?? throw new DutyUnavailable();
        var key = await keys.GetByKeyIdForAuthentication(principal.KeyId, token) ?? throw new DutyUnavailable();
        if (binding.HostBindingId != host.Id || binding.ServicePrincipalId != host.MaintenanceServicePrincipalId
            || binding.AuthorityTenantId != principal.TenantId || binding.OwnerType != principal.OwnerType
            || binding.OwnerId != principal.OwnerId)
            throw new DutyUnavailable();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, binding.ServicePrincipalId, token);
        var actors = await context.Actors.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.ServicePrincipalId == binding.ServicePrincipalId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in actors) await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(context, key.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMachineSubjectBinding>(context, binding.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, token)
            ?? throw new DutyUnavailable();
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        context.ModerationTrustDomainId = host.TrustDomainId;
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, token)
            ?? throw new DutyUnavailable();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);
        var until = clock.GetUtcNow().AddMinutes(5);
        if (key.ExpiresAt is { } expiry && expiry < until.UtcDateTime)
            until = new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc));
        var result = new DutyAuthority(host, binding.Id, binding.Revision, key.Id, epoch.Id, epoch.Revision, until);
        await RequireCurrentAsync(result, tenantId, principal, token);
        return result;
    }

    private async Task RequireCurrentAsync(
        DutyAuthority authority, Guid tenantId, ApiKeyPrincipalContext principal, CancellationToken token)
    {
        if (machine.Current != principal || clock.GetUtcNow() >= authority.Until
            || !await NativeTenantAsync(tenantId, token) || context.ModerationTrustDomainId != authority.Host.TrustDomainId)
            throw new DutyUnavailable();
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        var binding = await bindings.ReadCurrentAsync(principal.KeyId, ModerationMachineCredentialSource.Persisted, token);
        var key = await keys.GetByKeyIdForAuthentication(principal.KeyId, token);
        if (host is null || host.Id != authority.Host.Id || host.TrustDomainId != authority.Host.TrustDomainId
            || host.SolutionId != authority.Host.SolutionId || host.InstanceId != authority.Host.InstanceId
            || host.MaintenanceServicePrincipalId != authority.Host.MaintenanceServicePrincipalId
            || binding is null || binding.Id != authority.BindingId || binding.Revision != authority.BindingRevision
            || binding.HostBindingId != host.Id || binding.ServicePrincipalId != host.MaintenanceServicePrincipalId
            || binding.AuthorityTenantId != principal.TenantId || binding.OwnerType != principal.OwnerType
            || binding.OwnerId != principal.OwnerId || key is null || key.Id != authority.KeyId
            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, token)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == authority.EpochId && row.HostBindingId == host.Id && row.TenantId == tenantId
                && row.Revision == authority.EpochRevision && row.Revision > 0, token))
            throw new DutyUnavailable();
    }

    private async Task<bool> NativeTenantAsync(Guid tenantId, CancellationToken token)
    {
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
            || context.Database.ProviderName is not (RelationalNamedLock.PostgreSqlProvider
                or RelationalNamedLock.SqlServerProvider or RelationalNamedLock.SqliteProvider or RelationalNamedLock.MySqlProvider))
            return false;
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        return await command.ExecuteScalarAsync(token) is string value
            && Guid.TryParse(value, out var current) && current == tenantId;
    }

    private IQueryable<Guid> ManagerIds(Guid tenantId) =>
        context.PlatformUserRoles.AsNoTracking().Where(row => !row.User.IsDeleted
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
            .Select(row => row.UserId)
            .Union(context.TenantUserRoleGrants.AsNoTracking().Where(row => row.TenantId == tenantId
                && row.RevokedAt == null && row.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.RoleId == (int)RoleEnum.TenantAdmin && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted)
                .Select(row => row.TenantUser.UserId))
            .Where(id => !context.PrivacyErasureSagas.Any(saga => saga.SubjectId == id));

    private async Task<Guid[]> AcquireManagersAsync(Guid tenantId, CancellationToken token)
    {
        var discovered = await ManagerIds(tenantId).Order().Take(33).ToArrayAsync(token);
        if (discovered.Length > 32) throw new DutyUnavailable();
        foreach (var id in discovered) await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        var logins = await context.UserExternalLogins.AsNoTracking().Where(row => discovered.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in logins) await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => discovered.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var row in platform) await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, row.Id, token);
        var members = await context.TenantUsers.AsNoTracking().Where(row => row.TenantId == tenantId && discovered.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in members) await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row => row.TenantId == tenantId && members.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var row in grants) await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, value => value.Id, row.Id, token);
        foreach (var role in platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId)).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [role], token);
        var result = new List<Guid>();
        var current = await ManagerIds(tenantId).Order().Take(33).ToArrayAsync(token);
        if (!discovered.SequenceEqual(current)) throw new DutyUnavailable();
        foreach (var id in current)
            if (!await privacy.IsSubjectFencedAsync(id, token)) result.Add(id);
        return result.ToArray();
    }

    private async Task RequireManagersCurrentAsync(Guid tenantId, Guid[] recipients, CancellationToken token)
    {
        var current = await ManagerIds(tenantId).Where(id => recipients.Contains(id)).Order().ToArrayAsync(token);
        if (!recipients.SequenceEqual(current)) throw new DutyUnavailable();
        foreach (var id in recipients)
            if (await privacy.IsSubjectFencedAsync(id, token)) throw new DutyUnavailable();
    }

    private sealed record DutyAuthority(
        ModerationHostBinding Host, Guid BindingId, long BindingRevision, Guid KeyId,
        Guid EpochId, long EpochRevision, DateTimeOffset Until);
}
