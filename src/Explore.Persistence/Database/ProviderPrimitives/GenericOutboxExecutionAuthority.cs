using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class GenericOutboxExecutionAuthority(
    ExploreDbContext context, IGenericOutboxAdmission admission) : IGenericOutboxExecutionAuthority
{
    public async Task BindAsync(GenericOutboxClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (context.GenericOutboxExecutionClaim is not null || !OwnsContext(claim)
            || await admission.ReadClaimAsync(claim.MessageId, claim.LeaseExpiresAt, claim.Status, cancellationToken) != claim)
            throw Denied();
        context.BindOutboxExecutionClaim(claim);
    }

    public Task RequireAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        RequireAsync(message, OutboxMessageStatus.Processing, cancellationToken);

    public Task RequireDeadLetterAsync(OutboxMessage message, CancellationToken cancellationToken) =>
        RequireAsync(message, OutboxMessageStatus.DeadLettered, cancellationToken);

    private async Task RequireAsync(
        OutboxMessage message, OutboxMessageStatus status, CancellationToken cancellationToken)
    {
        RequireCurrentEnvelope(message, status);
        var bound = context.GenericOutboxExecutionClaim!;
        // A failure may change Processing to DeadLettered, but never replace its original expiry/source.
        var persisted = await admission.ReadClaimAsync(bound.MessageId, bound.LeaseExpiresAt, status, cancellationToken);
        if (persisted != bound with { Status = status })
            throw Denied();
    }

    /// <summary>
    /// Validates a freshly read original Runtime envelope without renting a second
    /// source reader. Only the concrete worker issuer composes this with its exact
    /// operation proof on the creator's original locked transaction.
    /// </summary>
    internal void RequireCurrentEnvelope(OutboxMessage message,
        OutboxMessageStatus status = OutboxMessageStatus.Processing)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bound = context.GenericOutboxExecutionClaim;
        if (bound is null || !OwnsContext(bound) || message.Id != bound.MessageId
            || message.Status != status || message.NextRetryAt != bound.LeaseExpiresAt
            || message.AggregateId != bound.AggregateId || message.AggregateType != bound.AggregateType
            || message.EventType != bound.EventType || message.CreatedAt != bound.CreatedAt
            || status == OutboxMessageStatus.Processing && bound.Status != status
            || status == OutboxMessageStatus.DeadLettered && !GenericOutboxRoutes.SupportsDeadLetter(message.EventType)
            || !string.Equals(bound.PayloadDigest, GenericOutboxAdmission.PayloadDigest(message.Payload), StringComparison.OrdinalIgnoreCase))
            throw Denied();
    }

    private bool OwnsContext(GenericOutboxClaim claim) =>
        !context.IsTenantFilterBypassed
        && context.BindTenantLease() == claim.TenantId
        && (claim.Ownership == GenericOutboxOwnership.Tenant
            ? claim.TenantId is { } tenant && tenant != Guid.Empty
            : claim.Ownership is GenericOutboxOwnership.Subject or GenericOutboxOwnership.Instance && claim.TenantId is null);

    private static InvalidOperationException Denied() => new("generic_outbox_execution_not_admitted");
}
