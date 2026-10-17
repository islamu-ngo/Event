using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class UserTenantMembershipNativeQuery
{
    internal static Task<List<MembershipRow>>? ReadAsync(
        ExploreDbContext context, Guid userId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
            return null;
        string command = $$"""
            SELECT * FROM {{PostgresUserTenantMembershipContract.FunctionSql(context)}}({0})
            """;
        return context.Database.SqlQueryRaw<MembershipRow>(command, userId)
            .ToListAsync(cancellationToken);
    }

    internal sealed class MembershipRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public Guid? ActorId { get; set; }
        public int StatusId { get; set; }
        public DateTime? JoinedAt { get; set; }
        public DateTime? SuspendedAt { get; set; }
        public Guid? SuspendedBy { get; set; }
        public DateTime? BanExpiresAt { get; set; }
        public DateTime? RemovedAt { get; set; }
        public Guid? RemovedBy { get; set; }
        public string? ModerationNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }
        public string TenantFullName { get; set; } = null!;
        public string TenantSlug { get; set; } = null!;
        public string? TenantDescription { get; set; }
        public int TenantStatusId { get; set; }
        public DateTime TenantCreatedAt { get; set; }
        public Guid? TenantCreatedBy { get; set; }
        public DateTime? TenantUpdatedAt { get; set; }
        public Guid? TenantUpdatedBy { get; set; }
        public string TenantStatusMasterCode { get; set; } = null!;
        public string TenantStatusFullName { get; set; } = null!;
        public string? TenantStatusDescription { get; set; }
        public bool TenantStatusIsActiveState { get; set; }
    }
}
