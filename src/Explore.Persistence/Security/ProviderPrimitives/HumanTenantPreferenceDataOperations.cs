using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

/// <summary>Executes the fixed preference write and image queries on the original store-owned context.</summary>
internal static class HumanTenantPreferenceDataOperations
{
    internal static async Task<bool> WriteAsync(
        ExploreDbContext context, IServiceProvider services, IHumanTenantPreferenceMutation mutation,
        Action requireOriginal, CancellationToken token)
    {
        requireOriginal();
        var enrollment = services.GetService<PostgresNativeQueueEnrollment>();
        await PostgresTenantPreferencePermissions.RequireReadyAsync(context, enrollment, token);
        requireOriginal();
        await PostgresUserTenantMembershipReadPermissions.RequireReadyAsync(context, enrollment, token);
        requireOriginal();
        bool written = await context.Users.IgnoreAutoIncludes().Where(user => user.Id == mutation.SubjectId && !user.IsDeleted
                && context.TenantUsers.IgnoreQueryFilters(new[] { QueryFilterNames.Tenant }).Any(member =>
                    member.UserId == mutation.SubjectId && member.TenantId == mutation.TenantId
                    && !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active
                    && context.Tenants.Any(tenant => tenant.Id == member.TenantId)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.LastActiveTenantId, mutation.TenantId)
                .SetProperty(user => user.UpdatedAt, mutation.UpdatedAt)
                .SetProperty(user => user.UpdatedBy, mutation.SubjectId), token) == 1;
        requireOriginal();
        return written;
    }

    internal static async Task<bool> MatchesAsync(
        ExploreDbContext context, IServiceProvider services, IHumanTenantPreferenceMutation mutation,
        Action requireOriginal, CancellationToken token)
    {
        requireOriginal();
        await PostgresTenantPreferencePermissions.RequireReadyAsync(context,
            services.GetService<PostgresNativeQueueEnrollment>(), token);
        requireOriginal();
        bool matches = await context.Users.IgnoreAutoIncludes().AsNoTracking().AnyAsync(user => user.Id == mutation.SubjectId
            && !user.IsDeleted && user.LastActiveTenantId == mutation.TenantId
            && user.UpdatedAt == mutation.UpdatedAt && user.UpdatedBy == mutation.SubjectId, token);
        requireOriginal();
        return matches;
    }
}
