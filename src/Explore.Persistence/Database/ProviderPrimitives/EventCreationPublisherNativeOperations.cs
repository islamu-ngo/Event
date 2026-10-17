using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal sealed class EventCreationPublisherNativeOperations
{
    private readonly ExploreDbContext context;

    private EventCreationPublisherNativeOperations(ExploreDbContext context) => this.context = context;

    internal static EventCreationPublisherNativeOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal Task<(IReadOnlyList<OrganizationMember> Organizations, IReadOnlyList<GroupMember> Groups,
        IReadOnlyList<RolePermission> Permissions)> ReadAsync(PostgresNativeQueueEnrollment enrollment,
        Guid tenant, Guid principal, CancellationToken token) =>
        PostgresEventCreationPublisherReadContract.ReadAsync(context, enrollment, tenant, principal, token);
}
