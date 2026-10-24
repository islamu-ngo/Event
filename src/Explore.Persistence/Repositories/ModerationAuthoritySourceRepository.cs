using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Owns native installation provenance, not moderation policy. All facts are acquired and reread
/// inside the primary serializable transaction; native fences survive until its commit or rollback.
/// </summary>
public sealed class ModerationAuthoritySourceRepository(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine)
    : IModerationAuthoritySourceRepository
{
    public async Task<ModerationAuthoritySourceResult> EstablishAsync(
        ModerationAuthoritySourceInstallation installation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (machine.IsMachineCaller)
            return ModerationAuthoritySourceResult.Denied();
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.ChangeTracker.Entries().Any() || !TenantMatches(installation.TenantId))
            return ModerationAuthoritySourceResult.Unavailable();

        ModerationAuthoritySourceResult? established = null;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: async token =>
                {
                    context.ChangeTracker.Clear();
                    var qualified = await AcquireSourcesAsync(installation, create: true, token);
                    if (qualified.Outcome != ModerationAuthoritySourceOutcome.Established)
                        throw new AbortInstallation(qualified);
                    await context.SaveChangesAsync(token);
                    if (!TenantMatches(installation.TenantId) || !await ProviderTenantMatchesAsync(installation.TenantId, token))
                        throw new AbortInstallation(ModerationAuthoritySourceResult.Unavailable());
                    established = qualified;
                    return qualified;
                },
                verifySucceeded: async token =>
                {
                    context.ChangeTracker.Clear();
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var reconciled = await AcquireSourcesAsync(installation, create: false, token);
                    return established?.Epoch is { } expected
                        && reconciled.Outcome == ModerationAuthoritySourceOutcome.Established
                        && reconciled.Epoch?.Revision == expected.Revision;
                },
                isolationLevel: IsolationLevel.Serializable,
                cancellationToken: cancellationToken);
        }
        catch (AbortInstallation aborted)
        {
            return aborted.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ModerationAuthoritySourceResult.Conflict();
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            // An absent singleton/epoch has no row to fence. Its unique constraint is the final
            // arbiter against other native writers; reconcile a concurrent identical commit only.
            context.ChangeTracker.Clear();
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var reconciled = await AcquireSourcesAsync(installation, create: false, token);
                await transaction.CommitAsync(token);
                return reconciled.Outcome == ModerationAuthoritySourceOutcome.Unavailable
                    ? ModerationAuthoritySourceResult.Conflict() : reconciled;
            }, cancellationToken);
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    private async Task<ModerationAuthoritySourceResult> AcquireSourcesAsync(
        ModerationAuthoritySourceInstallation installation, bool create, CancellationToken token)
    {
        if (!TenantMatches(installation.TenantId) || !await ProviderTenantMatchesAsync(installation.TenantId, token))
            return ModerationAuthoritySourceResult.Unavailable();

        // Serializes installation writers even before the singleton source exists. The lease is
        // transaction-owned; disposing this handle cannot release ownership before the commit.
        await using var installationFence = await RelationalNamedLock.AcquireTransactionAsync(
            context, "moderation.authority-source.installation", token);
        var userId = await admin.ResolveUserIdAsync(token);
        if (userId is null)
            return ModerationAuthoritySourceResult.Unauthenticated();

        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, userId.Value, token);
        // Provider identity is resolved only by IAdminContext. Fence its native bindings and
        // resolve again after acquisition so deletion/reassignment cannot survive as cached identity.
        var logins = await context.UserExternalLogins.AsNoTracking()
            .Where(row => row.UserId == userId).OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var loginId in logins)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, loginId, token);
        if (await admin.ResolveUserIdAsync(token) != userId
            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == userId && !row.IsDeleted, token))
            return ModerationAuthoritySourceResult.Denied();

        var assignments = await context.PlatformUserRoles.AsNoTracking()
            .Where(row => row.UserId == userId
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var assignment in assignments)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, assignment.Id, token);
        foreach (var roleId in assignments.Select(row => row.RoleId).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [roleId], token);
        var assignmentIds = assignments.Select(row => row.Id).ToArray();
        if (!await context.PlatformUserRoles.AsNoTracking().AnyAsync(row =>
            assignmentIds.Contains(row.Id) && row.UserId == userId
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token))
            return ModerationAuthoritySourceResult.Denied();

        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(
            context, installation.MaintenanceServicePrincipalId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, installation.TenantId, token);
        var tenant = await context.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == installation.TenantId, token);
        if (tenant is null)
            return ModerationAuthoritySourceResult.Unavailable();
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        tenant = await context.Tenants.AsNoTracking().Include(row => row.TenantStatus)
            .SingleOrDefaultAsync(row => row.Id == installation.TenantId, token);
        if (tenant is null || !tenant.TenantStatus.IsActiveState
            || !await context.ServicePrincipals.AsNoTracking().AnyAsync(row =>
                row.Id == installation.MaintenanceServicePrincipalId && !row.IsDeleted, token))
            return ModerationAuthoritySourceResult.Unavailable();

        var binding = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (binding is not null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, binding.Id, token);
            binding = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
            if (binding is null)
                return ModerationAuthoritySourceResult.Unavailable();
            if (binding.Id != installation.HostBindingId || binding.TrustDomainId != installation.TrustDomainId
                || binding.SolutionId != installation.SolutionId || binding.InstanceId != installation.InstanceId
                || binding.MaintenanceServicePrincipalId != installation.MaintenanceServicePrincipalId)
                return ModerationAuthoritySourceResult.Conflict();
        }
        else if (!create)
        {
            return ModerationAuthoritySourceResult.Unavailable();
        }

        var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
            row => row.TenantId == installation.TenantId && row.HostBindingId == installation.HostBindingId, token);
        if (epoch is not null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);
            epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(
                row => row.TenantId == installation.TenantId && row.HostBindingId == installation.HostBindingId, token);
            if (epoch is null || epoch.Revision <= 0)
                return ModerationAuthoritySourceResult.Unavailable();
            if (epoch.Id != installation.EpochId)
                return ModerationAuthoritySourceResult.Conflict();
        }
        else
        {
            if (!create)
                return ModerationAuthoritySourceResult.Unavailable();
            // A globally reused hidden epoch ID must fail the native PK constraint, never be
            // reparented. Tenant filtering remains enabled; no foreign facts confer authority.
            binding ??= ModerationHostBinding.Establish(installation.HostBindingId, installation.TrustDomainId,
                installation.SolutionId, installation.InstanceId, installation.MaintenanceServicePrincipalId);
            epoch = ModerationQualificationEpoch.Initialize(installation.EpochId, binding, tenant);
            if (!await context.ModerationHostBindings.AsNoTracking().AnyAsync(token))
                context.ModerationHostBindings.Add(binding);
            context.ModerationQualificationEpochs.Add(epoch);
        }

        return binding is null || !TenantMatches(installation.TenantId)
            || !await ProviderTenantMatchesAsync(installation.TenantId, token)
            ? ModerationAuthoritySourceResult.Unavailable()
            : ModerationAuthoritySourceResult.Established(binding, epoch);
    }

    private bool TenantMatches(Guid tenantId) =>
        tenantId != Guid.Empty && context.TenantFilterTenantId == tenantId && !context.IsTenantFilterBypassed;

    private async Task<bool> ProviderTenantMatchesAsync(Guid tenantId, CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider)
            return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        return value is string setting && Guid.TryParse(setting, out var currentTenant) && currentTenant == tenantId;
    }

    private sealed class AbortInstallation(ModerationAuthoritySourceResult result) : Exception
    {
        public ModerationAuthoritySourceResult Result { get; } = result;
    }
}
