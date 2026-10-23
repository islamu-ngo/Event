using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Native = Explore.Persistence.Security.PostgresSupportAdministrationContract;

namespace Explore.Persistence.Security;

/// <summary>
/// Keeps original forwarding service semantics without granting scalar session reads.
/// Mutation and protected disclosure use the separately acquired closed DATA lease.
/// </summary>
internal sealed class SupportAdministrationSessionRepository : SupportAccessSessionRepository, ISupportAccessSessionRepository
{
    private readonly ExploreDbContext context;
    private readonly ExploreDbContextExecutionLease execution;
    private readonly Func<PostgresNativeQueueEnrollment> enrollment;

    public SupportAdministrationSessionRepository(ExploreDbContext context,
        ExploreDbContextExecutionLease execution, Func<PostgresNativeQueueEnrollment> enrollment) : base(context)
    {
        this.context = context;
        this.execution = execution;
        this.enrollment = enrollment;
    }

    Task<SupportAccessSession?> ISupportAccessSessionRepository.GetActiveForActorAsync(
        Guid actorUserId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        ReadSessionAsync(null, actorUserId, null, nowUtc, cancellationToken);

    Task<SupportAccessSession?> ISupportAccessSessionRepository.GetActiveOwnedSessionAsync(
        Guid sessionId, Guid actorUserId, Guid? targetTenantId, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
        => ReadSessionAsync(sessionId, actorUserId, targetTenantId, nowUtc, cancellationToken);

    private async Task<SupportAccessSession?> ReadSessionAsync(
        Guid? sessionId, Guid actorUserId, Guid? targetTenantId, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        execution.RequireOriginalOwner(context);
        Guid? selectedTenant = context.BindTenantLease();
        if (context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || execution.PolicyPurpose is not null || selectedTenant is null
            || sessionId.HasValue && selectedTenant != targetTenantId || execution.HumanLifetime is null
            || execution.HumanScope?.Subject?.AccountKey is null
            || execution.HumanScope.Subject.PrincipalId != actorUserId)
            throw new UnauthorizedAccessException("Forwarded support DATA requires the original selected human.");
        var contextId = context.ContextId;
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction;
        bool opened = connection.State != ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            string image = await Native.ReadAsync(context, enrollment(),
                sessionId.HasValue
                    ? new("Forwarded", actorUserId, selectedTenant.Value, sessionId, selectedTenant)
                    : new("Discover", actorUserId, selectedTenant.Value, AtUtc: nowUtc),
                cancellationToken);
            execution.RequireOriginalOwner(context);
            context.ValidateTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId != selectedTenant
                || !connection.Matches(context)
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || execution.HumanScope?.Subject?.PrincipalId != actorUserId)
                throw new UnauthorizedAccessException("Original forwarded support DATA selection changed.");
            using var document = JsonDocument.Parse(image);
            var rows = document.RootElement.GetProperty("Data").GetProperty("Sessions");
            var session = rows.GetArrayLength() == 0 ? null
                : SupportAdministrationPostgresSource.Materialize<SupportAccessSession>(context, rows[0]);
            return session is not null && session.IsActiveAt(nowUtc) ? session : null;
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }
}
