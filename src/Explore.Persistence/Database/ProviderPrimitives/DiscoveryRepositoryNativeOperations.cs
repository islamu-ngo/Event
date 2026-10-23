using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Federation;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Domain.Services.Discovery;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Executes fixed discovery capabilities without taking ownership of traversal or finalization.</summary>
internal sealed class DiscoveryRepositoryNativeOperations
{
    private readonly ExploreDbContext context;

    private DiscoveryRepositoryNativeOperations(ExploreDbContext context) => this.context = context;

    internal static DiscoveryRepositoryNativeOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal Task<Event?> ReadPublicDetailAsync(PublicEventDiscoveryReadBinding binding,
        Guid? id, string? code, CancellationToken token) =>
        PostgresPublicEventDetailContract.ReadAsync(context, binding, id, code, token);

    internal Task<Event?> ReadPublicDetailAsync(PublicEventDiscoveryReadBinding binding,
        PostgresNativeQueueEnrollment? enrollment, Guid? id, string? code, CancellationToken token)
    {
        if (enrollment is null || enrollment != binding.Enrollment)
            throw new UnauthorizedAccessException("public_detail_original_enrollment_required");
        return ReadPublicDetailAsync(binding, id, code, token);
    }

    internal Task<(List<Event> Items, int TotalCount)> PageLocalAsync(
        PublicEventDiscoveryReadBinding binding, EventQuerySpecification specification,
        int pageNumber, int pageSize, CancellationToken token) =>
        PublicEventDiscoveryProviderOperations.PageLocalAsync(
            context, binding, specification, pageNumber, pageSize, token);

    internal Task<DateTimeOffset?> GetNextResponseBoundaryAsync(
        PublicEventDiscoveryReadBinding binding, ILocationPrivacyGovernanceService privacy,
        AtprotoEventGovernanceResolver federation, DateTimeOffset observedAtUtc, CancellationToken token) =>
        PublicEventResponseClockProviderOperations.GetNextAsync(context, binding, privacy, federation, observedAtUtc, token);

    internal Task FenceTenantAsync(Guid tenant, CancellationToken token) =>
        PostgresDiscoveryTraversalContract.FenceTenantAsync(context, tenant, token);

    internal async Task<bool> CanFinalizeDisclosureAsync(IReadOnlyCollection<Guid> ordered, CancellationToken token) =>
        ordered.Any(id => context.TenantFilterTenantId != id)
        || await context.Database.SqlQueryRaw<bool>(
            "SELECT pg_catalog.has_function_privilege(current_user, {0}, 'EXECUTE') AS \"Value\"",
            PostgresDiscoveryDisclosureContract.FunctionSql(context, "fence_tenants") + "(uuid[])")
            .SingleAsync(token);

    internal Task<IReadOnlyList<EventDiscoveryIdentity>> GetBindingsAsync(Guid tenant,
        EventDiscoverySourceKind kind, IReadOnlyCollection<string> keys, CancellationToken token) =>
        PostgresDiscoveryTraversalContract.GetBindingsAsync(context, tenant, kind, keys, token);

    internal Task<EventDiscoveryRevision?> ReadRevisionAsync(Guid tenant, CancellationToken token) =>
        PostgresDiscoveryTraversalContract.ReadRevisionAsync(context, tenant, token);

    internal Task<EventDiscoverySnapshot?> FindReusableAsync(Guid tenant, string criteria,
        long identityEpoch, long disclosureEpoch, DateTime now, CancellationToken token) =>
        PostgresDiscoveryTraversalContract.FindReusableAsync(context, tenant, criteria, identityEpoch, disclosureEpoch, now, token);

    internal Task<EventDiscoverySnapshot?> CaptureAsync(EventDiscoverySnapshot snapshot,
        EventDiscoveryTraversalLimits limits, DateTime now, CancellationToken token) =>
        PostgresDiscoveryTraversalContract.CaptureAsync(context, snapshot, limits, now, token);

    internal Task<EventDiscoverySnapshot?> GetSnapshotAsync(Guid tenant, Guid snapshot, CancellationToken token) =>
        PostgresDiscoveryTraversalContract.GetSnapshotAsync(context, tenant, snapshot, token);

    internal Task<IReadOnlyList<EventDiscoverySnapshotItem>> GetItemsAsync(
        Guid tenant, Guid snapshot, long after, int take, CancellationToken token) =>
        PostgresDiscoveryTraversalContract.GetItemsAsync(context, tenant, snapshot, after, take, token);

    internal Task<int> PurgeAsync(Guid tenant, DateTime now, int maximum, CancellationToken token) =>
        PostgresDiscoveryTraversalContract.PurgeAsync(context, tenant, now, maximum, token);

    internal Task<IReadOnlyList<Event>> SeekLocalAsync(PublicEventDiscoveryReadBinding binding,
        EventQuerySpecification specification, EventDiscoverySourceCursor? after, int take, CancellationToken token) =>
        PublicEventDiscoveryProviderOperations.SeekLocalAsync(context, binding, specification, after, take, token);

    internal Task<IReadOnlyList<Event>> MembersLocalAsync(PublicEventDiscoveryReadBinding binding,
        EventQuerySpecification specification, IReadOnlyDictionary<Guid, Guid> sessions, CancellationToken token) =>
        PublicEventDiscoveryProviderOperations.MembersLocalAsync(context, binding, specification, sessions, token);

    internal Task<IReadOnlyList<AtprotoEventProjection>> SeekRemoteAsync(PublicEventDiscoveryReadBinding binding,
        AtprotoEventProjectionQuery query, EventDiscoverySourceCursor? after, CancellationToken token) =>
        PublicEventDiscoveryProviderOperations.SeekRemoteAsync(context, binding, query, after, token);

    internal Task<IReadOnlyList<AtprotoEventProjection>> MembersRemoteAsync(PublicEventDiscoveryReadBinding binding,
        AtprotoEventProjectionQuery query, IReadOnlyCollection<Guid> records, CancellationToken token) =>
        PublicEventDiscoveryProviderOperations.MembersRemoteAsync(context, binding, query, records, token);

    internal Task<IReadOnlyList<AtprotoEventProjection>> VisibleRemoteRecordsAsync(PublicEventDiscoveryReadBinding binding,
        IReadOnlyCollection<Guid> records, CancellationToken token) =>
        PublicEventDiscoveryProviderOperations.VisibleRemoteRecordsAsync(context, binding, records, token);

    internal static async Task AcquireReservationAsync(ExploreDbContext context, Guid tenant, CancellationToken token)
    {
        await EventDiscoverySnapshotProviderOperations.BootstrapReservationAsync(context, tenant, token);
        // PostgreSQL bootstrap already performs the native write fence. Other
        // providers retain the original reservation row fence after bootstrap.
        if (!context.Database.IsNpgsql())
            await RelationalEntityRowFence.AcquireGlobalAsync<EventDiscoverySnapshotReservation>(context, tenant, token);
    }
}
