using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Retains physical PostgreSQL custody and rechecks the fixed webhook target source on that
/// original owner. The context owns tracked evidence binding, consumption and failure poisoning.
/// </summary>
internal sealed class PostgresWebhookClaimCustody
{
    private readonly ExploreDbContext context;

    private PostgresWebhookClaimCustody(ExploreDbContext context) => this.context = context;

    internal sealed record Owner(
        ExploreDbContext Context, IDbContextTransaction Transaction,
        RelationalConnectionIdentity Connection, int BackendProcessId);

    internal static PostgresWebhookClaimCustody? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal Owner? Capture()
    {
        if (context.Database.CurrentTransaction is not { } transaction) return null;
        var connection = RelationalConnectionIdentity.Capture(context);
        if (connection.State != ConnectionState.Open || connection.BackendProcessId is not { } backend
            || !connection.Owns(transaction.GetDbTransaction()))
            throw Mismatch();
        return new(context, transaction, connection, backend);
    }

    internal bool IsCurrent(ExploreDbContext expectedContext, Owner owner) =>
        ReferenceEquals(context, expectedContext) && ReferenceEquals(owner.Context, context)
        && ReferenceEquals(context.Database.CurrentTransaction, owner.Transaction)
        && owner.Connection.State == ConnectionState.Open && owner.Connection.Matches(context)
        && owner.Connection.BackendProcessId == owner.BackendProcessId
        && owner.Connection.Owns(owner.Transaction.GetDbTransaction());

    internal bool SharesTransaction(Owner left, Owner right) =>
        ReferenceEquals(left.Context, context) && ReferenceEquals(right.Context, context)
        && ReferenceEquals(left.Transaction, right.Transaction);

    internal async Task ValidateOriginalTargetAsync(
        Owner owner, ExploreDbContext.NativeWebhookClaimSnapshot admitted, CancellationToken cancellationToken)
    {
        RequireCurrent(owner, admitted.TenantId);
        // Rollback to a pre-admission savepoint may release the earlier lock. Use the existing
        // tenant-and-key SELECT fence, never the Global/primary-key no-op UPDATE variants.
        await RelationalEntityRowFence.AcquireAsync<WebhookLocalTargetSnapshot>(
            context, admitted.TenantId, target => target.Id, admitted.TargetId, cancellationToken);
        RequireCurrent(owner, admitted.TenantId);
        var persisted = await context.WebhookLocalTargetSnapshots.AsNoTracking()
            .Where(target => target.TenantId == admitted.TenantId && target.Id == admitted.TargetId)
            .Select(target => new ExploreDbContext.NativeWebhookClaimSnapshot(
                target.Id, target.TenantId, target.WebhookMessageId, target.DeliveryPlanSnapshotId,
                target.WebhookEndpointId, target.DeliveryStatusId, target.ProcessingLeaseOwner,
                target.ProcessingLeaseToken, target.DeliveryFence, target.ProcessingLeaseExpiresAtUtc,
                target.ConcurrencyVersion, target.NextActionAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        RequireCurrent(owner, admitted.TenantId);
        if (persisted is null || persisted != admitted)
            throw Mismatch();
    }

    private void RequireCurrent(Owner owner, Guid tenantId)
    {
        context.ValidateTenantLease();
        if (tenantId == Guid.Empty || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || !IsCurrent(context, owner))
            throw Mismatch();
    }

    private static InvalidOperationException Mismatch() => new("tracked_tenant_authority_mismatch");
}
