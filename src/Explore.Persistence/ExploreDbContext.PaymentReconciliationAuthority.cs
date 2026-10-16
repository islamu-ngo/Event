using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private NativePaymentReconciliationOwner? _nativePaymentReconciliationOwner;

    private sealed record NativePaymentReconciliationOwner(
        PaymentReconciliationClaim Claim, PaymentAttempt Attempt, int StatusId,
        PaymentSucceededObservation? Observation, RegistrationFinalizationEffect? Finalization,
        Guid? EventId, Guid? SourceMessageId,
        IDbContextTransaction Transaction, RelationalConnectionIdentity Connection);

    internal void RetainNativePaymentReconciliationOwner(
        PaymentReconciliationClaim claim, PaymentReconciliationEffect effect, PaymentAttempt attempt,
        PaymentSucceededObservation? observation, RegistrationFinalizationEffect? finalization)
    {
        if (!Database.IsNpgsql() || !TenantFilterTenantId.HasValue)
            return;
        var original = Entry(effect).OriginalValues;
        // The repository has locked and selected this exact live claim before applying
        // the decision. Its original identity survives Complete/Retry clearing the lease.
        if (IsTenantFilterBypassed || TenantFilterTenantId != claim.TenantId
            || effect.Id != claim.EffectId || effect.TenantId != claim.TenantId
            || effect.PaymentAttemptId != claim.PaymentAttemptId
            || attempt.Id != claim.PaymentAttemptId || attempt.TenantId != claim.TenantId
            || attempt.RegistrationOrderId != effect.RegistrationOrderId
            || original.GetValue<OutboxMessageStatus>(nameof(effect.Status)) != OutboxMessageStatus.Processing
            || original.GetValue<Guid?>(nameof(effect.ProcessingLeaseToken)) != claim.LeaseToken
            || original.GetValue<long>(nameof(effect.ProcessingFence)) != claim.ProcessingFence
            || original.GetValue<int>(nameof(effect.AttemptCount)) != claim.AttemptCount
            || observation is not null && (observation.TenantId != claim.TenantId
                || observation.PaymentAttemptId != attempt.Id
                || observation.RegistrationOrderId != attempt.RegistrationOrderId
                || observation.SourceIncomingWebhookMessageId != effect.SourceIncomingWebhookMessageId)
            || finalization is not null && (finalization.TenantId != claim.TenantId
                || finalization.RegistrationOrderId != attempt.RegistrationOrderId)
            || Database.CurrentTransaction is not { } transaction)
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        _nativePaymentReconciliationOwner = new(claim, attempt, attempt.PaymentAttemptStatusId,
            observation, finalization, finalization?.EventId, effect.SourceIncomingWebhookMessageId,
            transaction, RelationalConnectionIdentity.Capture(this));
    }

    private bool? NativePaymentReconciliationForeignKeyVisible(
        EntityEntry entry, IEntityType principal, IReadOnlyList<IProperty> properties, object?[] values)
    {
        if (_nativePaymentReconciliationOwner is not { } owner
            || principal.ClrType != typeof(Tenant) && principal.ClrType != typeof(PaymentAttemptStatus)
                && principal.ClrType != typeof(Event) && principal.ClrType != typeof(IncomingWebhookMessage))
            return null;
        bool attempt = ReferenceEquals(entry.Entity, owner.Attempt) && entry.State == EntityState.Modified;
        bool observation = ReferenceEquals(entry.Entity, owner.Observation) && entry.State == EntityState.Added;
        bool finalization = ReferenceEquals(entry.Entity, owner.Finalization) && entry.State == EntityState.Added;
        if (!attempt && !observation && !finalization)
            return null;
        if (!Database.IsNpgsql() || IsTenantFilterBypassed || TenantFilterTenantId != owner.Claim.TenantId
            || !ReferenceEquals(Database.CurrentTransaction, owner.Transaction)
            || !owner.Connection.Matches(this) || !owner.Connection.Owns(owner.Transaction.GetDbTransaction())
            || properties.Count != 1 || properties[0].Name != "Id" || values.Length != 1
            || owner.Attempt.Id != owner.Claim.PaymentAttemptId || owner.Attempt.TenantId != owner.Claim.TenantId)
            return false;
        if (principal.ClrType == typeof(PaymentAttemptStatus))
            return attempt && values[0] is int status && status == owner.StatusId
                && status == owner.Attempt.PaymentAttemptStatusId && Enum.IsDefined((PaymentAttemptStatusEnum)status);
        if (principal.ClrType == typeof(Event))
            return finalization && values[0] is Guid eventId && eventId == owner.EventId
                && eventId == owner.Finalization!.EventId;
        if (principal.ClrType == typeof(IncomingWebhookMessage))
            return observation && values[0] is Guid messageId && messageId == owner.SourceMessageId
                && messageId == owner.Observation!.SourceIncomingWebhookMessageId;
        // Only these exact new descendants may reuse the tenant from the locked graph.
        // Ordinary tracked objects still use the normal persisted visibility query.
        return values[0] is Guid tenantId && tenantId == owner.Claim.TenantId
            && (observation && owner.Observation!.TenantId == tenantId
                || finalization && owner.Finalization!.TenantId == tenantId);
    }
}
