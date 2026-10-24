using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Owns one native material mutation and its immutable cause receipt on the primary store.
/// It neither activates moderation nor acknowledges notices, other consequences or the effect.
/// </summary>
public sealed partial class ModerationResourceQuarantineRepository(
    ExploreDbContext context, IMachinePrincipalAccessor machine,
    IModerationMachineSubjectBindingRepository bindings, IExternalApiKeyAuthenticationRepository keys,
    TimeProvider clock, IPrivacyIdentityFenceAuthority privacy) : IModerationResourceQuarantineRepository
{
    public async Task<ModerationResourceQuarantineResult> ExecuteAsync(
        Guid effectId, Guid leaseId, long expectedEffectRevision, int bindingOrdinal,
        Guid resourceId, Guid materialVersionId, CancellationToken cancellationToken = default)
    {
        if (effectId == Guid.Empty || leaseId == Guid.Empty || expectedEffectRevision is < 1 or long.MaxValue
            || bindingOrdinal is < 0 or >= 64 || resourceId == Guid.Empty || materialVersionId == Guid.Empty)
            throw new ArgumentException("Quarantine intent requires the exact lease, ordinal and native material.");
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.IsTenantFilterBypassed || context.ModerationTrustDomainId is not null
            || context.ChangeTracker.Entries().Any() || context.TenantFilterTenantId is not { } tenantId
            || tenantId == Guid.Empty)
            return new(ModerationResourceQuarantineOutcome.Unavailable);

        var receiptId = Guid.CreateVersion7();
        DateTimeOffset? appliedAt = null;
        var principal = machine.Current;
        Func<CancellationToken, Task> beforePrimaryCommit = _ => Task.CompletedTask;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
                privacy.ExecuteEnrollmentSerializedAsync(async cancellation =>
            {
                beforePrimaryCommit = _ => Task.CompletedTask;
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    throw new UnavailableQuarantine();
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                    : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellation);
                var transaction = context.Database.CurrentTransaction ?? throw new UnavailableQuarantine();
                var qualified = await QualifyAsync(effectId, leaseId, expectedEffectRevision, bindingOrdinal,
                    resourceId, materialVersionId, cancellationToken);
                if (qualified is null || machine.Current != principal)
                    throw new UnavailableQuarantine();
                var resource = await context.EventResources.Include(row => row.ModerationQuarantines)
                    .SingleOrDefaultAsync(row => row.Id == resourceId && row.TenantId == tenantId, cancellationToken);
                if (resource is null || resource.MaterialVersionId != materialVersionId
                    || resource.ModerationQuarantineCount != resource.ModerationQuarantines.Count
                    || resource.ModerationQuarantines.Any(row => row.TenantId != tenantId
                        || row.EventId != resource.EventId || row.EventResourceId != resourceId
                        || row.MaterialVersionId != materialVersionId))
                    throw new UnavailableQuarantine();
                foreach (var cause in resource.ModerationQuarantines) cause.ValidatePersistedState();
                var existing = await context.EventResourceModerationQuarantines.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.EventResourceId == resourceId
                        && row.EffectId == effectId && row.BindingOrdinal == bindingOrdinal, cancellationToken);
                var retired = false;
                EventResourceModerationQuarantine receipt;
                if (existing is not null)
                {
                    existing.ValidatePersistedState();
                    if (existing.EventResourceId != resourceId || existing.EventId != qualified.Generation.EventId
                        || existing.MaterialVersionId != materialVersionId || existing.TenantId != tenantId
                        || existing.AppliedAt < qualified.Effect.CreatedAt || existing.AppliedAt > clock.GetUtcNow())
                        throw new UnavailableQuarantine();
                    receipt = existing;
                    if (existing.ReleaseId is { } releaseId)
                    {
                        await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantineRelease>(
                            context, releaseId, cancellationToken);
                        var release = await context.Set<EventResourceModerationQuarantineRelease>().AsNoTracking()
                            .SingleOrDefaultAsync(row => row.Id == releaseId && row.TenantId == tenantId, cancellationToken);
                        if (release is null) throw new UnavailableQuarantine();
                        release.ValidatePersistedState();
                        if (!release.Owns(existing) || release.ReleasedAt > clock.GetUtcNow())
                            throw new UnavailableQuarantine();
                        retired = true;
                    }
                }
                else
                {
                    appliedAt ??= clock.GetUtcNow();
                    if (appliedAt < qualified.ObservedAt || appliedAt >= qualified.ValidUntil)
                        throw new UnavailableQuarantine();
                    receipt = resource.ApplyModerationQuarantine(receiptId, effectId, bindingOrdinal,
                        materialVersionId, qualified.Generation.ResourceStamp, appliedAt.Value);
                    await context.SaveChangesAsync(cancellationToken);
                }

                await context.FlushDisclosureAsync(cancellation);
                // Retain final qualification for a colocated privacy owner's commit as well.
                beforePrimaryCommit = async finalToken =>
                {
                    var final = await QualifyAsync(effectId, leaseId, expectedEffectRevision, bindingOrdinal,
                        resourceId, materialVersionId, finalToken);
                    var finishedAt = clock.GetUtcNow();
                    if (final is null || machine.Current != principal || finishedAt < qualified.ObservedAt
                        || finishedAt >= qualified.ValidUntil || finishedAt >= final.ValidUntil
                        || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                        || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                        || context.ModerationTrustDomainId != qualified.Effect.TrustDomainId
                        || context.ChangeTracker.HasChanges())
                        throw new UnavailableQuarantine();
                };
                await beforePrimaryCommit(cancellation);
                if (owned is not null) await owned.CommitAsync(cancellation);
                return new ModerationResourceQuarantineResult(retired
                    ? ModerationResourceQuarantineOutcome.Retired : existing is null
                    ? ModerationResourceQuarantineOutcome.Applied
                    : ModerationResourceQuarantineOutcome.AlreadyApplied, receipt);
            }, finalToken => beforePrimaryCommit(finalToken), cancellationToken));
        }
        catch (UnavailableQuarantine)
        {
            return new(ModerationResourceQuarantineOutcome.Unavailable);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ModerationResourceQuarantineOutcome.Conflict);
        }
        catch (DbException)
        {
            return new(ModerationResourceQuarantineOutcome.Unavailable);
        }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        {
            return new(ModerationResourceQuarantineOutcome.Unavailable);
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private sealed class UnavailableQuarantine : Exception;
    private sealed record QualifiedCause(
        ModerationEffectIntent Effect, ModerationResourceGeneration Generation,
        DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
}
