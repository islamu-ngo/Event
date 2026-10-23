using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationNoticeReadRepository
{
    public async Task<ModerationNoticeAcknowledgementResult> AcknowledgeAsync(
        ModerationNoticeAcknowledgement request, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task> beforePrimaryCommit = _ => Task.CompletedTask;
        try
        {
            return await privacy.ExecuteEnrollmentSerializedAsync(
                token => AcknowledgeInAuthorityAsync(request, validation => beforePrimaryCommit = validation, token),
                token => beforePrimaryCommit(token), cancellationToken);
        }
        catch (AbortAcknowledgement aborted)
        {
            return new(aborted.Outcome);
        }
    }

    private async Task<ModerationNoticeAcknowledgementResult> AcknowledgeInAuthorityAsync(
        ModerationNoticeAcknowledgement request, Action<Func<CancellationToken, Task>> retainValidation,
        CancellationToken cancellationToken)
    {
        if (request.OperationId == Guid.Empty || request.OperationId.Version != 7
            || request.OperationId.Variant is < 8 or > 11
            || request.SnapshotHash is null || request.SnapshotHash.Length != 64
            || !request.SnapshotHash.All(char.IsAsciiHexDigitLower))
            return new(ModerationNoticeAcknowledgementOutcome.Conflict);
        var acquisition = await AcquireAsync(new(request.TenantId, request.SubjectId,
            request.NoticeId, null, 1), cancellationToken);
        await using var lease = acquisition.Lease;
        if (acquisition.Outcome != ModerationNoticeReadOutcome.Allowed || lease is not ReadLease readLease)
            return new(acquisition.Outcome == ModerationNoticeReadOutcome.Unauthenticated
                ? ModerationNoticeAcknowledgementOutcome.Unauthenticated
                : acquisition.Outcome == ModerationNoticeReadOutcome.NotFound
                    ? ModerationNoticeAcknowledgementOutcome.NotFound : ModerationNoticeAcknowledgementOutcome.Unavailable);
        var entry = lease.Entries.Single();
        if (entry.Notice.Audience is not (ModerationNoticeAudience.Subject or ModerationNoticeAudience.ContentRepresentative)
            || entry.Notice.RecipientSubjectId != request.SubjectId || entry.Source.SnapshotHash != request.SnapshotHash)
            return new(ModerationNoticeAcknowledgementOutcome.NotFound);
        if (entry.Notification is { } existing)
            return existing.Id == request.OperationId
                && existing.NoticeId == request.NoticeId && existing.AcknowledgedByHumanId == request.SubjectId
                && await lease.RevalidateAsync(cancellationToken) == ModerationNoticeReadOutcome.Allowed
                ? new(ModerationNoticeAcknowledgementOutcome.Acknowledged, existing.Id)
                : new(ModerationNoticeAcknowledgementOutcome.Conflict);
        if (await context.Set<ModerationNoticeNotificationReceipt>().AsNoTracking()
            .AnyAsync(row => row.Id == request.OperationId, cancellationToken))
            return new(ModerationNoticeAcknowledgementOutcome.Conflict);
        if (await lease.RevalidateAsync(cancellationToken) != ModerationNoticeReadOutcome.Allowed)
            return new(ModerationNoticeAcknowledgementOutcome.NotFound);
        var original = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(entry.Source.SnapshotJson)!.Notice!;
        var notifiedAt = clock.GetUtcNow();
        if (!ModerationNoticeRetention.CanNotifyAt(notifiedAt, original.ContestationCalendarMonths, original.RetainUntil))
            return new(ModerationNoticeAcknowledgementOutcome.NotFound);
        var receipt = ModerationNoticeNotificationReceipt.Acknowledge(request.OperationId, entry.Notice,
            entry.Source, request.TenantId, request.SubjectId, request.SnapshotHash,
            original.ContestationCalendarMonths, original.RetainUntil, notifiedAt);
        var transaction = context.Database.CurrentTransaction!;
        try
        {
            context.Set<ModerationNoticeNotificationReceipt>().Add(receipt);
            await context.SaveChangesAsync(cancellationToken);
            await context.FlushDisclosureAsync(cancellationToken);
            var host = await context.ModerationHostBindings.AsNoTracking().SingleAsync(cancellationToken);
            await RequireAcknowledgementCurrentAsync(
                request, receipt, original, host, transaction, notifiedAt, readLease.RepresentativePlans, cancellationToken);
            retainValidation(async token =>
            {
                context.ModerationTrustDomainId = receipt.TrustDomainId;
                try
                {
                    await RequireAcknowledgementCurrentAsync(request, receipt, original,
                        host, transaction, notifiedAt, readLease.RepresentativePlans, token);
                }
                finally { context.ModerationTrustDomainId = null; }
            });
            if (!context.IdentityFenceOwnsTransaction) await transaction.CommitAsync(cancellationToken);
            return new(ModerationNoticeAcknowledgementOutcome.Acknowledged, receipt.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ModerationNoticeAcknowledgementOutcome.Conflict);
        }
        catch (DbUpdateException error) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(error))
        {
            return new(ModerationNoticeAcknowledgementOutcome.Conflict);
        }
        finally
        {
            context.Entry(receipt).State = EntityState.Detached;
        }
    }

    private async Task RequireAcknowledgementCurrentAsync(
        ModerationNoticeAcknowledgement request, ModerationNoticeNotificationReceipt receipt,
        ModerationNoticeSnapshot original, ModerationHostBinding host, IDbContextTransaction transaction,
        DateTimeOffset notifiedAt,
        IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.AuthorityPlan> representativePlans, CancellationToken token)
    {
        var current = await ReadQualifiedAsync(request.NoticeId,
            new(request.TenantId, request.SubjectId, request.NoticeId, null, 1), host, notifiedAt, representativePlans, token);
        if (current?.Entry.Notification?.Id != receipt.Id
            || current.Entry.Source.SnapshotHash != request.SnapshotHash
            || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != request.TenantId
            || context.ModerationTrustDomainId != receipt.TrustDomainId || context.ChangeTracker.HasChanges()
            || !await ProviderTenantMatchesAsync(request.TenantId, token)
            || !await RecipientCurrentAsync(new(request.TenantId, request.SubjectId, request.NoticeId, null, 1), token))
            throw new AbortAcknowledgement(ModerationNoticeAcknowledgementOutcome.NotFound);
        var now = clock.GetUtcNow();
        if (now < notifiedAt || now >= current.ValidUntil
            || !ModerationNoticeRetention.CanNotifyAt(now, original.ContestationCalendarMonths, original.RetainUntil))
            throw new AbortAcknowledgement(ModerationNoticeAcknowledgementOutcome.NotFound);
    }

    private sealed class AbortAcknowledgement(ModerationNoticeAcknowledgementOutcome outcome) : Exception
    {
        public ModerationNoticeAcknowledgementOutcome Outcome { get; } = outcome;
    }
}
