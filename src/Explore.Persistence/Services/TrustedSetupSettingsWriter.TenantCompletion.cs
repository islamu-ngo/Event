using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed partial class TrustedSetupSettingsWriter
{
    /// <summary>
    /// Only the admitted continuation receives this executor. Its targets come from the
    /// immutable original capsule, never a public repository, entity or ambient permission.
    /// </summary>
    private sealed class CompletionEffects<T>(
        ExploreDbContext context, TrustedSetupSettingsApply<T> apply,
        Func<CancellationToken, Task> requireSource) : ITrustedSetupCompletionEffects
    {
        private int active = 1;
        private int step;
        private Guid membershipId;
        private int roleId;

        private async Task RequireAsync(CancellationToken token)
        {
            if (Volatile.Read(ref active) != 1 || apply.DefaultTenantCreation is not { } request
                || request.TenantId != PlatformDefaults.DefaultTenantId
                || request.ActorUserId is null || request.ActorUserId == Guid.Empty
                || request.TenantStatusId != (int)TenantStatusEnum.Provisioning)
                throw new UnauthorizedAccessException("This setup continuation has no default-tenant effect.");
            await requireSource(token);
            if (Volatile.Read(ref active) != 1 || context.ChangeTracker.HasChanges())
                throw new UnauthorizedAccessException("Setup tenant effects cannot flush unrelated tracked changes.");
        }

        public async Task<Guid> EnsureDefaultTenantAsync(CancellationToken cancellationToken)
        {
            await RequireAsync(cancellationToken);
            if (Interlocked.CompareExchange(ref step, 1, 0) != 0)
                throw new UnauthorizedAccessException("The default tenant can be ensured only once.");
            var request = apply.DefaultTenantCreation!;
            if (await context.Tenants.AsNoTracking().AnyAsync(
                tenant => tenant.Id == request.TenantId, cancellationToken))
                return request.TenantId;

            var prepared = TenantCreationService.Prepare(request);
            await RequireAsync(cancellationToken);
            context.Tenants.Add(prepared.Tenant);
            await context.SaveChangesAsync(cancellationToken);
            await RequireAsync(cancellationToken);
            context.TenantSettingsDocuments.Add(prepared.BrandingDocument);
            await context.SaveChangesAsync(cancellationToken);
            await RequireAsync(cancellationToken);
            context.TenantSettingsDocuments.Add(prepared.DirectoryOperatorIdentityDocument);
            await context.SaveChangesAsync(cancellationToken);
            return request.TenantId;
        }

        public async Task EnsureDefaultTenantAdministratorAsync(CancellationToken cancellationToken)
        {
            await RequireAsync(cancellationToken);
            if (Interlocked.CompareExchange(ref step, 2, 1) != 1)
                throw new UnauthorizedAccessException("Default membership must follow the original tenant effect.");
            var request = apply.DefaultTenantCreation!;
            Guid subject = request.ActorUserId!.Value;
            var account = apply.Source.Account
                ?? throw new UnauthorizedAccessException("Setup membership requires its original account.");
            if (!await context.Users.AsNoTracking().AnyAsync(user => user.Id == subject, cancellationToken)
                || !await context.UserExternalLogins.AsNoTracking().AnyAsync(login =>
                    login.UserId == subject && login.AuthenticationProviderId == (int)account.ProviderKind
                    && login.ProviderKey == account.Value, cancellationToken))
                throw new UnauthorizedAccessException("Setup membership requires its persisted original subject binding.");
            Guid? actorId = await context.Actors.AsNoTracking().Where(actor => actor.UserId == subject)
                .Select(actor => (Guid?)actor.Id).SingleOrDefaultAsync(cancellationToken);
            var member = await context.TenantUsers
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleOrDefaultAsync(row => row.TenantId == request.TenantId && row.UserId == subject, cancellationToken);
            await RequireAsync(cancellationToken);
            if (member is null)
            {
                member = new TenantUser
                {
                    TenantId = request.TenantId,
                    Tenant = null!,
                    UserId = subject,
                    User = null!,
                    ActorId = actorId,
                    Actor = null,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = request.OccurredAt,
                    CreatedAt = request.OccurredAt,
                    CreatedBy = subject
                };
                context.TenantUsers.Add(member);
                await context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                if (member.ActorId != actorId)
                    throw new UnauthorizedAccessException("Setup cannot reassign an existing membership actor.");
                if (member.StatusId != (int)TenantUserStatusEnum.Active)
                {
                    member.StatusId = (int)TenantUserStatusEnum.Active;
                    member.UpdatedAt = request.OccurredAt;
                    member.UpdatedBy = subject;
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
            membershipId = member.Id;
            var role = await context.Roles.AsNoTracking()
                .SingleOrDefaultAsync(row => row.MasterCode == "tenant.admin", cancellationToken)
                ?? await context.Roles.AsNoTracking().SingleOrDefaultAsync(
                    row => row.Id == (int)RoleEnum.TenantAdmin, cancellationToken);
            if (role is null || role.RoleScopeId != (int)RoleScopeEnum.Tenant)
                throw new UnauthorizedAccessException("Setup requires the current tenant administrator role.");
            roleId = role.Id;
            bool granted = await context.TenantUserRoleGrants
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .AnyAsync(row => row.TenantId == request.TenantId && row.TenantUserId == membershipId
                    && row.RoleId == roleId && row.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.RevokedAt == null, cancellationToken);
            await RequireAsync(cancellationToken);
            if (!granted)
            {
                context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    TenantId = request.TenantId,
                    Tenant = null!,
                    TenantUserId = membershipId,
                    TenantUser = null!,
                    RoleId = roleId,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = request.OccurredAt,
                    GrantedBy = subject
                });
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        internal async Task RequireCompletedAsync(CancellationToken token)
        {
            if (apply.DefaultTenantCreation is not { } request)
            {
                if (step != 0)
                    throw new UnauthorizedAccessException("Profile and multi-tenant setup cannot create a default membership.");
                return;
            }
            if (step != 2 || membershipId == Guid.Empty
                || !await context.TenantUsers
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .AsNoTracking().AnyAsync(row => row.Id == membershipId && row.TenantId == request.TenantId
                        && row.UserId == request.ActorUserId && row.StatusId == (int)TenantUserStatusEnum.Active, token)
                || !await context.TenantUserRoleGrants
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .AsNoTracking().AnyAsync(row => row.TenantId == request.TenantId
                        && row.TenantUserId == membershipId && row.RoleId == roleId
                        && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RevokedAt == null, token)
                || !await context.Roles.AsNoTracking().AnyAsync(
                    row => row.Id == roleId && row.RoleScopeId == (int)RoleScopeEnum.Tenant, token))
                throw new UnauthorizedAccessException("Setup lost its exact default membership and administrator grant.");
        }

        internal void Close() => Interlocked.Exchange(ref active, 0);
    }
}
