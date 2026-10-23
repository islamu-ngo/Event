using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// CFG09 operational duty owner. Discovery is bounded and metadata-only; each cause gets its
/// own serializable native transaction, current maintenance credential and privacy fences.
/// This owner never reads original notice captures, allegations or protected material.
/// </summary>
public sealed partial class ModerationResourceQuarantineDutyRepository(
    ExploreDbContext context, IMachinePrincipalAccessor machine,
    IModerationMachineSubjectBindingRepository bindings, IExternalApiKeyAuthenticationRepository keys,
    IPrivacyIdentityFenceAuthority privacy, ModerationResourceReviewEscalationMaterializer notifications,
    ISettingMutationLock settings, TimeProvider clock) : IModerationResourceQuarantineDutyRepository,
    IModerationScheduleRiskReviewReconciliationRepository
{
    public async Task<ModerationResourceReviewDutyBatch> ExecutePassAsync(
        Guid? afterQuarantineId, int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (afterQuarantineId == Guid.Empty) throw new ArgumentException("A cursor must be a native identity.", nameof(afterQuarantineId));
        if (!Clean() || machine.Current is not { CredentialSource: ApiKeyCredentialSource.Persisted } principal)
            return new(false, afterQuarantineId, []);
        var tenantId = context.TenantFilterTenantId!.Value;
        Guid[] ids;
        try
        {
            ids = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var authority = await AcquireAuthorityAsync(tenantId, principal, cancellationToken);
                var found = await context.EventResourceModerationQuarantines.AsNoTracking()
                    .Where(row => row.TenantId == tenantId && row.ReleaseId == null
                        && (afterQuarantineId == null || row.Id.CompareTo(afterQuarantineId.Value) > 0))
                    .OrderBy(row => row.Id).Take(limit).Select(row => row.Id).ToArrayAsync(cancellationToken);
                await RequireCurrentAsync(authority, tenantId, principal, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return found;
            });
        }
        catch (DutyUnavailable) { return new(false, afterQuarantineId, []); }
        catch (DbException) { return new(false, afterQuarantineId, []); }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }

        var results = ImmutableArray.CreateBuilder<ModerationResourceReviewDutyResult>(ids.Length);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ExecuteOneAsync(id, tenantId, principal, cancellationToken));
        }
        // Empty/short pages wrap on the next scheduled pass; no retry loop or polling lives here.
        return new(true, ids.Length == limit ? ids[^1] : null, results.ToImmutable());
    }

    private async Task<ModerationResourceReviewDutyResult> ExecuteOneAsync(
        Guid id, Guid tenantId, ApiKeyPrincipalContext principal, CancellationToken token)
    {
        Func<CancellationToken, Task> beforePrimaryCommit = _ => Task.CompletedTask;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
                settings.ExecuteOrderedGroupsAsync([[GovernanceSettingKeys.Email.DeliveryEnabled]], outer =>
                privacy.ExecuteEnrollmentSerializedAsync(async cancellation =>
                {
                    beforePrimaryCommit = _ => Task.CompletedTask;
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                        : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellation);
                    var transaction = context.Database.CurrentTransaction ?? throw new DutyUnavailable();
                    var recipients = await AcquireManagersAsync(tenantId, cancellation);
                    var authority = await AcquireAuthorityAsync(tenantId, principal, cancellation);
                    var cause = await context.EventResourceModerationQuarantines.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == tenantId, cancellation)
                        ?? throw new DutyUnavailable();
                    cause.ValidatePersistedState();

                    // Only current custody metadata is acquired. Original purpose, personal
                    // expiry and former source authors are not authority for this distinct duty.
                    var observedAt = clock.GetUtcNow();
                    var native = await ModerationResourceGenerationReader.AcquireAsync(context, id, cause.EventResourceId,
                        authority.Host.Id, authority.Host.TrustDomainId, tenantId, 1, authority.Host.MaintenanceServicePrincipalId,
                        authority.EpochRevision, observedAt, authority.Until, cancellation) ?? throw new DutyUnavailable();
                    await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantine>(context, id, cancellation);
                    cause = await context.EventResourceModerationQuarantines.AsNoTracking()
                        .SingleAsync(row => row.Id == id && row.TenantId == tenantId, cancellation);
                    cause.ValidatePersistedState();
                    if (cause.EventId != native.EventId || cause.MaterialVersionId != native.MaterialVersionId
                        || cause.AppliedAt > observedAt)
                        throw new DutyUnavailable();
                    if (cause.ReleaseId is not null) return new ModerationResourceReviewDutyResult(ModerationResourceReviewDutyOutcome.Released);
                    var activeCount = await context.EventResourceModerationQuarantines.AsNoTracking()
                        .CountAsync(row => row.TenantId == tenantId && row.EventResourceId == cause.EventResourceId
                            && row.ReleaseId == null, cancellation);
                    if (!await context.EventResources.AsNoTracking().AnyAsync(row => row.Id == cause.EventResourceId
                        && row.TenantId == tenantId && row.ModerationQuarantineCount == activeCount
                        && row.ModerationQuarantineCount > 0, cancellation))
                        throw new DutyUnavailable();
                    await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.resource-review-duty:{tenantId:N}:{id:N}", cancellation);
                    var duty = await context.Set<EventResourceModerationReviewDuty>().AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == tenantId, cancellation);
                    if (duty is null)
                    {
                        var dueAt = await ReadOriginalDeadlineAsync(cause, authority, cancellation);
                        duty = EventResourceModerationReviewDuty.Discover(cause, dueAt);
                        context.Add(duty);
                    }
                    if (!duty.Owns(cause)) throw new DutyUnavailable();
                    var completion = await context.Set<EventResourceModerationReviewCompletion>().AsNoTracking()
                        .Where(row => row.TenantId == tenantId && row.QuarantineId == id)
                        .OrderBy(row => row.CompletedAt).ThenBy(row => row.Id).FirstOrDefaultAsync(cancellation);
                    if (completion is not null)
                    {
                        completion.ValidatePersistedState();
                        if (!completion.Owns(cause)) throw new DutyUnavailable();
                    }
                    EventResourceModerationReviewEscalation? escalation = null;
                    var outcome = completion is not null ? ModerationResourceReviewDutyOutcome.Reviewed
                        : ModerationResourceReviewDutyOutcome.AwaitingReview;
                    if (duty.IsOverdueAt(observedAt, completion))
                    {
                        escalation = await context.Set<EventResourceModerationReviewEscalation>().AsNoTracking()
                            .SingleOrDefaultAsync(row => row.Id == id && row.TenantId == tenantId, cancellation);
                        if (escalation is not null)
                        {
                            escalation.ValidatePersistedState();
                            outcome = ModerationResourceReviewDutyOutcome.AlreadyEscalated;
                        }
                        else
                        {
                            if (recipients.Length == 0)
                                throw new DutyUnavailable(ModerationResourceReviewDutyOutcome.RecipientUnavailable);
                            escalation = new()
                            {
                                Id = id, TenantId = tenantId, HostBindingId = authority.Host.Id,
                                ServicePrincipalId = authority.Host.MaintenanceServicePrincipalId,
                                QualificationRevision = authority.EpochRevision, EscalatedAt = observedAt,
                                PurposeUntil = authority.Until, RecipientCount = recipients.Length
                            };
                            escalation.ValidatePersistedState();
                            foreach (var recipient in recipients)
                                if (!await notifications.MaterializeAsync(escalation, recipient, cancellation))
                                    throw new DutyUnavailable(ModerationResourceReviewDutyOutcome.RecipientUnavailable);
                            context.Add(escalation);
                            outcome = ModerationResourceReviewDutyOutcome.Escalated;
                        }
                    }
                    await context.SaveChangesAsync(cancellation);
                    await context.FlushDisclosureAsync(cancellation);
                    beforePrimaryCommit = async finalToken =>
                    {
                        await RequireCurrentAsync(authority, tenantId, principal, finalToken);
                        await RequireManagersCurrentAsync(tenantId, recipients, finalToken);
                        var finalNative = await ModerationResourceGenerationReader.AcquireAsync(context, id, cause.EventResourceId,
                            authority.Host.Id, authority.Host.TrustDomainId, tenantId, 1, authority.Host.MaintenanceServicePrincipalId,
                            authority.EpochRevision, observedAt, authority.Until, finalToken) ?? throw new DutyUnavailable();
                        if (!native.HasSameNativeVersion(finalNative) || clock.GetUtcNow() < observedAt
                            || clock.GetUtcNow() >= authority.Until || context.Database.CurrentTransaction != transaction
                            || !await context.EventResourceModerationQuarantines.AsNoTracking().AnyAsync(row =>
                                row.Id == id && row.TenantId == tenantId && row.ReleaseId == null, finalToken))
                            throw new DutyUnavailable();
                    };
                    await beforePrimaryCommit(cancellation);
                    if (owned is not null) await owned.CommitAsync(cancellation);
                    return new ModerationResourceReviewDutyResult(outcome, duty, completion, escalation);
                }, finalToken => beforePrimaryCommit(finalToken), outer), token));
        }
        catch (DutyUnavailable unavailable) { return new(unavailable.Outcome); }
        catch (DbUpdateConcurrencyException) { return new(ModerationResourceReviewDutyOutcome.Unavailable); }
        catch (DbException) { return new(ModerationResourceReviewDutyOutcome.Unavailable); }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        { return new(ModerationResourceReviewDutyOutcome.Unavailable); }
        catch (ArgumentException) { return new(ModerationResourceReviewDutyOutcome.Unavailable); }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }
    }

    private async Task<DateTimeOffset> ReadOriginalDeadlineAsync(
        EventResourceModerationQuarantine cause, DutyAuthority authority, CancellationToken token)
    {
        // The immutable effect/decision coordinates retain the chosen object clock, not
        // permission to inspect the old allegation. No SnapshotJson or notice table is read.
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .Where(row => row.Id == cause.EffectId && row.TrustDomainId == authority.Host.TrustDomainId)
            .Select(row => new { row.DecisionId, row.SubjectKind, row.SubjectId, row.ScopeKind, row.ScopeId, row.CreatedAt })
            .SingleOrDefaultAsync(token) ?? throw new DutyUnavailable();
        var decision = await context.ModerationDecisions.AsNoTracking()
            .Where(row => row.Id == effect.DecisionId).Select(row => new { row.StartsAt, row.ReviewAt }).SingleOrDefaultAsync(token);
        if (effect.SubjectKind != (int)ModerationSubjectKind.Content || effect.SubjectId != cause.EventId
            || effect.ScopeKind != (int)ModerationScopeKind.Resource || effect.ScopeId != cause.EventResourceId
            || effect.CreatedAt > cause.AppliedAt || decision?.ReviewAt is not { } due
            || due <= cause.AppliedAt || due > decision.StartsAt.AddHours(24))
            throw new DutyUnavailable();
        return due;
    }

    private bool Clean() => context.Database.IsRelational() && context.Database.CurrentTransaction is null
        && !context.ChangeTracker.Entries().Any() && !context.IsTenantFilterBypassed
        && context.ModerationTrustDomainId is null && context.TenantFilterTenantId is { } tenant && tenant != Guid.Empty
        && System.Transactions.Transaction.Current is null;

    private sealed class DutyUnavailable(
        ModerationResourceReviewDutyOutcome outcome = ModerationResourceReviewDutyOutcome.Unavailable) : Exception
    {
        public ModerationResourceReviewDutyOutcome Outcome { get; } = outcome;
    }
}
