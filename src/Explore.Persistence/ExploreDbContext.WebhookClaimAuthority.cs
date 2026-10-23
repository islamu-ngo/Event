using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private readonly Dictionary<(Guid TargetId, long Fence), NativeWebhookClaimOwner> _nativeWebhookClaims = [];

    internal sealed record NativeWebhookClaimSnapshot(
        Guid TargetId, Guid TenantId, Guid MessageId, Guid PlanId, Guid EndpointId, int StatusId,
        string? LeaseOwner, Guid? LeaseToken, long Fence, DateTimeOffset? LeaseExpiresAtUtc,
        long ConcurrencyVersion, DateTimeOffset NextActionAtUtc);

    internal sealed record NativeWebhookClaimReadOwner(
        PostgresWebhookClaimCustody Primitive, PostgresWebhookClaimCustody.Owner PhysicalOwner,
        object TenantLease, Guid TenantId);

    private sealed record NativeWebhookClaimOwner(
        NativeWebhookClaimReadOwner Authority, NativeWebhookClaimSnapshot Original,
        WebhookLocalTargetSnapshot Target)
    {
        public NativeWebhookClaimBinding? Binding { get; set; }
    }

    private sealed record NativeWebhookClaimBinding(
        WebhookDeliveryAttempt Attempt, NativeWebhookEvidenceSnapshot Evidence,
        NativeWebhookClaimSnapshot SettledTarget)
    {
        public bool Consumed { get; set; }
        public bool Failed { get; set; }
    }

    private sealed record NativeWebhookEvidenceSnapshot(
        Guid Id, Guid TenantId, Guid MessageId, Guid EndpointId, int AttemptNumber, int OutcomeId,
        long Fence, Guid? LeaseToken, DateTime? LeaseExpiresAt, DateTime? ProcessingStartedAt,
        DateTime ScheduledAt, DateTime? SentAt, DateTime? CompletedAt, int? HttpStatusCode,
        string? FailureCategory, int? DurationMs, DateTime? NextRetryAt);

    internal NativeWebhookClaimReadOwner? CaptureNativeWebhookClaimReadOwner(Guid tenantId)
    {
        if (PostgresWebhookClaimCustody.For(this) is not { } primitive) return null;
        try
        {
            // Preflight and transport handoff reads have no explicit transaction and retain no proof.
            if (primitive.Capture() is not { } physicalOwner) return null;
            ValidateTenantLease();
            if (IsTenantFilterBypassed || !_tenantLeaseBound || _tenantLeaseId != tenantId
                || _tenantLeaseIdentity is null)
                throw NativeWebhookClaimMismatch();
            return new(primitive, physicalOwner, _tenantLeaseIdentity, tenantId);
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            throw;
        }
    }

    internal void RetainNativeWebhookClaim(
        NativeWebhookClaimReadOwner? authority, WebhookLocalTargetSnapshot target,
        NativeWebhookClaimSnapshot snapshot)
    {
        if (authority is null) return;
        RequireNativeWebhookClaimOwner(authority);
        var entry = Entry(target);
        if (snapshot.TenantId != authority.TenantId
            || snapshot.StatusId != (int)WebhookLocalDeliveryStatus.Delivering
            || snapshot.LeaseToken is null || snapshot.Fence < 1
            || snapshot.LeaseExpiresAtUtc is null || entry.State != EntityState.Unchanged
            || CaptureWebhookTarget(target) != snapshot
            || CaptureWebhookTarget((WebhookLocalTargetSnapshot)entry.OriginalValues.ToObject()) != snapshot)
            throw NativeWebhookClaimMismatch();
        if (_nativeWebhookClaims.Values.Any(owner =>
                !authority.Primitive.SharesTransaction(owner.Authority.PhysicalOwner, authority.PhysicalOwner)))
            _nativeWebhookClaims.Clear();
        var key = (snapshot.TargetId, snapshot.Fence);
        if (_nativeWebhookClaims.TryGetValue(key, out var existing) && existing.Binding is not null)
            throw NativeWebhookClaimMismatch();
        _nativeWebhookClaims[key] = new(authority, snapshot, target);
    }

    internal void BindNativeWebhookClaimEvidence(IReadOnlyCollection<WebhookDeliveryAttempt> attempts)
    {
        if (PostgresWebhookClaimCustody.For(this) is null) return;
        try
        {
            if (TenantFilterTenantId is null) return;
            ValidateTenantLease();
            ChangeTracker.DetectChanges();
            foreach (var attempt in attempts)
            {
                var matches = _nativeWebhookClaims.Values.Where(candidate =>
                    candidate.Original.TenantId == attempt.TenantId
                    && candidate.Original.MessageId == attempt.MessageId
                    && candidate.Original.EndpointId == attempt.EndpointId
                    && candidate.Original.Fence == attempt.ProcessingFence
                    && candidate.Original.Fence == attempt.AttemptNumber).ToArray();
                if (matches.Length != 1 || matches[0].Binding is not null
                    || attempt.Id == Guid.Empty || Entry(attempt).State != EntityState.Detached
                    || _nativeWebhookClaims.Values.Any(candidate => candidate.Binding?.Evidence.Id == attempt.Id))
                    throw NativeWebhookClaimMismatch();
                var owner = matches[0];
                RequireNativeWebhookClaimOwner(owner.Authority);
                var target = CaptureWebhookTarget(owner.Target);
                if (!IsNativeWebhookClaimTransition(owner, target, attempt.OutcomeId)
                    || attempt.CompletedAt is not { } completed
                    || owner.Original.LeaseExpiresAtUtc is not { } expiry || completed >= expiry.UtcDateTime
                    || attempt.ProcessingLeaseToken is { } token && token != owner.Original.LeaseToken
                    || attempt.ProcessingLeaseExpiresAt is { } end && end != expiry.UtcDateTime)
                    throw NativeWebhookClaimMismatch();
                owner.Binding = new(attempt, CaptureWebhookEvidence(attempt), target);
            }
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            throw;
        }
    }

    internal void PoisonNativeWebhookClaimEvidence(IReadOnlyCollection<WebhookDeliveryAttempt> attempts)
    {
        foreach (var owner in _nativeWebhookClaims.Values)
        {
            if (owner.Binding is not { } binding
                || !attempts.Any(attempt => ReferenceEquals(attempt, binding.Attempt)))
                continue;
            // This slot survives EF savepoint rollback and ChangeTracker.Clear. A failed insert
            // cannot leave earlier native health changes eligible for an outer commit.
            binding.Failed = true;
            if (ReferenceEquals(Database.CurrentTransaction, owner.Authority.PhysicalOwner.Transaction))
                PoisonStorageReferenceTransaction();
        }
    }

    private async Task ValidateNativeWebhookClaimEvidenceAsync(CancellationToken cancellationToken)
    {
        if (_nativeWebhookClaims.Values.All(owner => owner.Binding is null)) return;
        try
        {
            ChangeTracker.DetectChanges();
            foreach (var owner in _nativeWebhookClaims.Values.Where(value => value.Binding is not null))
            {
                var binding = owner.Binding!;
                RequireNativeWebhookClaimOwner(owner.Authority);
                if (binding.Failed)
                    throw NativeWebhookClaimMismatch();
                var evidence = Entry(binding.Attempt);
                if (CaptureWebhookEvidence(binding.Attempt) != binding.Evidence
                    || CaptureWebhookEvidence((WebhookDeliveryAttempt)evidence.OriginalValues.ToObject()) != binding.Evidence)
                    throw NativeWebhookClaimMismatch();
                if (binding.Consumed)
                {
                    // Audit can save again after settlement. Retired proof cannot authorize another
                    // attempt, but rewriting EF state/originals must not hide mutation of this object.
                    if (evidence.State != EntityState.Unchanged
                        || CaptureWebhookTarget(owner.Target) != binding.SettledTarget
                        || CaptureWebhookTarget((WebhookLocalTargetSnapshot)Entry(owner.Target).OriginalValues.ToObject())
                            != binding.SettledTarget)
                        throw NativeWebhookClaimMismatch();
                    continue;
                }
                if (evidence.State != EntityState.Added
                    || CaptureWebhookTarget(owner.Target) != binding.SettledTarget
                    || !IsNativeWebhookClaimTransition(owner, binding.SettledTarget, binding.Evidence.OutcomeId))
                    throw NativeWebhookClaimMismatch();

                await owner.Authority.Primitive.ValidateOriginalTargetAsync(
                    owner.Authority.PhysicalOwner, owner.Original, cancellationToken);
                RequireNativeWebhookClaimOwner(owner.Authority);
            }
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            throw;
        }
    }

    internal void ConsumeNativeWebhookClaimEvidence(
        IReadOnlyCollection<WebhookDeliveryAttempt> attempts, int savedEntries)
    {
        if (PostgresWebhookClaimCustody.For(this) is null) return;
        try
        {
            if (TenantFilterTenantId is null) return;
            foreach (var attempt in attempts)
            {
                var owner = _nativeWebhookClaims.Values.SingleOrDefault(value =>
                    ReferenceEquals(value.Binding?.Attempt, attempt));
                if (owner is null || owner.Binding is not { Consumed: false, Failed: false } binding || savedEntries < 1)
                    throw NativeWebhookClaimMismatch();
                RequireNativeWebhookClaimOwner(owner.Authority);
                var evidence = Entry(attempt);
                var target = Entry(owner.Target);
                if (evidence.State != EntityState.Unchanged || target.State != EntityState.Unchanged
                    || CaptureWebhookEvidence(attempt) != binding.Evidence
                    || CaptureWebhookEvidence((WebhookDeliveryAttempt)evidence.OriginalValues.ToObject()) != binding.Evidence
                    || CaptureWebhookTarget(owner.Target) != binding.SettledTarget
                    || CaptureWebhookTarget((WebhookLocalTargetSnapshot)target.OriginalValues.ToObject()) != binding.SettledTarget)
                    throw NativeWebhookClaimMismatch();
                binding.Consumed = true;
            }
        }
        catch
        {
            PoisonStorageReferenceTransaction();
            throw;
        }
    }

    private bool IsNativeWebhookClaimTransition(
        NativeWebhookClaimOwner owner, NativeWebhookClaimSnapshot target, int outcome)
    {
        var entry = Entry(owner.Target);
        if (entry.State != EntityState.Modified || owner.Original.ConcurrencyVersion == long.MaxValue
            || owner.Original.Fence > int.MaxValue
            || CaptureWebhookTarget((WebhookLocalTargetSnapshot)entry.OriginalValues.ToObject()) != owner.Original
            || target != (owner.Original with
            {
                StatusId = target.StatusId,
                LeaseOwner = null,
                LeaseToken = null,
                LeaseExpiresAtUtc = null,
                ConcurrencyVersion = owner.Original.ConcurrencyVersion + 1,
                NextActionAtUtc = target.NextActionAtUtc
            }))
            return false;
        return (WebhookLocalDeliveryStatus)target.StatusId switch
        {
            WebhookLocalDeliveryStatus.Succeeded => outcome == (int)WebhookDeliveryAttemptOutcome.Succeeded,
            WebhookLocalDeliveryStatus.RetryDue or WebhookLocalDeliveryStatus.DeadLettered =>
                outcome == (int)WebhookDeliveryAttemptOutcome.Failed,
            WebhookLocalDeliveryStatus.Abandoned => outcome == (int)WebhookDeliveryAttemptOutcome.Abandoned,
            _ => false
        };
    }

    private bool? NativeWebhookClaimForeignKeyVisible(
        EntityEntry entry, IEntityType principal, IReadOnlyList<IProperty> properties, object?[] values)
    {
        foreach (var owner in _nativeWebhookClaims.Values)
        {
            if (owner.Binding is not { Consumed: false, Failed: false } binding) continue;
            bool evidence = ReferenceEquals(entry.Entity, binding.Attempt);
            bool target = ReferenceEquals(entry.Entity, owner.Target);
            if ((!evidence || principal.ClrType != typeof(WebhookEndpoint)
                    && principal.ClrType != typeof(WebhookDeliveryAttemptOutcomeLookup)
                    && principal.ClrType != typeof(Tenant))
                && (!target || principal.ClrType != typeof(WebhookLocalDeliveryStatusLookup)))
                continue;
            RequireNativeWebhookClaimOwner(owner.Authority);
            if (properties.Count != 1 || properties[0].Name != "Id" || values.Length != 1)
                return false;
            // The exact admitted graph supplies FK provenance, not Tenant visibility or activity.
            // Database FK constraints still enforce existence of this bound parent reference.
            if (evidence && principal.ClrType == typeof(Tenant))
                return entry.State == EntityState.Added
                    && values[0] is Guid tenant && tenant == owner.Original.TenantId
                    && tenant == binding.Evidence.TenantId;
            return evidence
                ? entry.State == EntityState.Added && (principal.ClrType == typeof(WebhookEndpoint)
                    ? values[0] is Guid endpoint && endpoint == binding.Evidence.EndpointId
                    : values[0] is int outcome && outcome == binding.Evidence.OutcomeId)
                : entry.State == EntityState.Modified
                    && values[0] is int status && status == binding.SettledTarget.StatusId;
        }
        return null;
    }

    private void RequireNativeWebhookClaimOwner(NativeWebhookClaimReadOwner authority)
    {
        ValidateTenantLease();
        if (IsTenantFilterBypassed || TenantFilterTenantId != authority.TenantId
            || !ReferenceEquals(_tenantLeaseIdentity, authority.TenantLease)
            || !authority.Primitive.IsCurrent(this, authority.PhysicalOwner))
            throw NativeWebhookClaimMismatch();
    }

    private InvalidOperationException NativeWebhookClaimMismatch()
    {
        PoisonStorageReferenceTransaction();
        return new("tracked_tenant_authority_mismatch");
    }

    private void ResetNativeWebhookClaims() => _nativeWebhookClaims.Clear();

    private static NativeWebhookClaimSnapshot CaptureWebhookTarget(WebhookLocalTargetSnapshot target) => new(
        target.Id, target.TenantId, target.WebhookMessageId, target.DeliveryPlanSnapshotId,
        target.WebhookEndpointId, target.DeliveryStatusId, target.ProcessingLeaseOwner,
        target.ProcessingLeaseToken, target.DeliveryFence, target.ProcessingLeaseExpiresAtUtc,
        target.ConcurrencyVersion, target.NextActionAtUtc);

    private static NativeWebhookEvidenceSnapshot CaptureWebhookEvidence(WebhookDeliveryAttempt attempt) => new(
        attempt.Id, attempt.TenantId, attempt.MessageId, attempt.EndpointId, attempt.AttemptNumber, attempt.OutcomeId,
        attempt.ProcessingFence, attempt.ProcessingLeaseToken, attempt.ProcessingLeaseExpiresAt,
        attempt.ProcessingStartedAt, attempt.ScheduledAt, attempt.SentAt, attempt.CompletedAt,
        attempt.HttpStatusCode, attempt.FailureCategory, attempt.DurationMs, attempt.NextRetryAt);
}
