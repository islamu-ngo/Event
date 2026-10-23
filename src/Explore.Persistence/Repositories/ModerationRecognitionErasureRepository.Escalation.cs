using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    public async Task<IReadOnlyList<ModerationRecognitionEscalation>> ReadEscalationsAsync(
        Guid authenticatedHuman, Guid? afterId, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var result = new List<ModerationRecognitionEscalation>();
        while (result.Count < limit)
        {
            var ids = await Rows<ModerationRecognitionEscalation>().Where(row =>
                row.RecipientHumanId == authenticatedHuman && (afterId == null || row.Id.CompareTo(afterId.Value) > 0))
                .OrderBy(row => row.Id).Select(row => row.Id).Take(limit - result.Count).ToArrayAsync(token);
            if (ids.Length == 0) break;
            foreach (var id in ids)
                if (await ReadEscalationAsync(id, authenticatedHuman, token) is { } row) result.Add(row);
            afterId = ids[^1];
        }
        return result;
    }

    public async Task EscalateAsync(Guid intentId, Guid? actionRecoveryId,
        IModerationRecognitionRecoveryNotifications notifications, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        // The obligation commits before notification work. Routing, owner or graph failure
        // cannot roll it back or claim delivery; the next scheduled pass sees the same row.
        var ids = await identity.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await BeginAsync(ct);
            try
            {
                var pending = await RecoveryRequestAsync(intentId, ct);
                var authority = await AcquireSubjectAsync(RecoveryRequest(pending), ct);
                pending = await RecoveryRequestAsync(intentId, ct);
                var action = await ReadEscalationActionAsync(pending, actionRecoveryId, ct);
                if (actionRecoveryId is not null && (action is null || action.ResolvedAt is not null)
                    || actionRecoveryId is null && (pending.AuthoritySequence is not null || clock.GetUtcNow() < pending.EscalateAt))
                    return Array.Empty<Guid>();
                if (actionRecoveryId is null && await HasConsumedLawfulDispositionAsync(RecoveryRequest(pending), ct))
                    return Array.Empty<Guid>();
                // The original finite terms bound storage even when their active qualification
                // has since lapsed. Current qualification is a separate delivery requirement.
                var sources = await RequiredEscalationSourcesAsync(authority, pending, action, ct);
                var result = new List<Guid>();
                foreach (var source in sources)
                    foreach (var recipient in new[] { source.PrivacyOwnerHumanId, source.RecoveryOwnerHumanId })
                    {
                        var candidate = ModerationRecognitionEscalation.Require(
                            Guid.CreateVersion7(), pending, action, source, recipient, clock.GetUtcNow());
                        var existing = await Rows<ModerationRecognitionEscalation>().SingleOrDefaultAsync(row =>
                            row.TargetId == candidate.TargetId && row.SourceId == source.Id
                            && row.RecipientHumanId == recipient && row.Stage == candidate.Stage, ct);
                        if (existing is null)
                        {
                            context.Set<ModerationRecognitionEscalation>().Add(candidate);
                            result.Add(candidate.Id);
                        }
                        else if (existing.NotificationId is null) result.Add(existing.Id);
                    }
                await context.SaveChangesAsync(ct);
                await CommitAsync(transaction, ct);
                return result.ToArray();
            }
            finally { context.ChangeTracker.Clear(); }
        }, token);
        foreach (var id in ids)
            await DeliverEscalationAsync(id, notifications, token);
    }

    private Task DeliverEscalationAsync(Guid id, IModerationRecognitionRecoveryNotifications notifications,
        CancellationToken token) => identity.ExecuteSerializedAsync(async ct =>
    {
        await using var transaction = await BeginAsync(ct);
        try
        {
            var discovered = await Rows<ModerationRecognitionEscalation>().SingleAsync(row => row.Id == id, ct);
            var pending = await RecoveryRequestAsync(discovered.IntentId, ct);
            var authority = await AcquireSubjectAsync(RecoveryRequest(pending), ct);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionEscalation>(context, id, ct);
            var row = await Rows<ModerationRecognitionEscalation>().SingleAsync(item => item.Id == id, ct);
            row.ValidatePersistedState();
            if (row.NotificationId is not null) return true;
            if (clock.GetUtcNow() >= row.RetainUntil) return false;
            if (row.ActionRecoveryId is null && await HasConsumedLawfulDispositionAsync(RecoveryRequest(pending), ct))
                return false;
            var source = await EscalationOwnerAsync(authority, pending, row, ct);
            var action = await ReadEscalationActionAsync(pending, row.ActionRecoveryId, ct);
            if (source is null || row.ActionRecoveryId is not null && (action is null || action.ResolvedAt is not null)
                || row.ActionRecoveryId is null && pending.AuthoritySequence is not null)
                return false;
            var notificationId = await notifications.DeliverAsync(row, source.TenantId, ct);
            if (notificationId is null) return false;
            var notification = await Rows<Notification>().SingleOrDefaultAsync(item => item.Id == notificationId, ct);
            if (notification is null || notification.IsDeleted || notification.UserId != row.RecipientHumanId
                || notification.TenantId != source.TenantId || notification.EntityId != row.Id.ToString("D")
                || notification.NotificationIntentId is not { } intentId
                || !await Rows<NotificationIntent>().AnyAsync(item => item.Id == intentId
                    && item.TenantId == source.TenantId && item.RecipientUserId == row.RecipientHumanId
                    && item.SafePayloadReference == $"recognition-recovery:{row.Id:D}"
                    && item.OwnershipTypeId == (int)NotificationOwnershipTypeEnum.IslamuEvent, ct)
                || !await Rows<NotificationDelivery>().AnyAsync(item => item.NotificationId == notificationId
                    && item.NotificationIntentId == intentId && item.TenantId == source.TenantId
                    && item.ChannelId == (int)NotificationPreferenceChannelEnum.InApp
                    && item.StatusId == (int)NotificationDeliveryStatusEnum.Delivered && item.IsRequired, ct))
                throw new InvalidOperationException("moderation_recognition_escalation_delivery_unproven");
            context.Attach(row);
            row.Deliver(notificationId.Value, clock.GetUtcNow());
            await context.SaveChangesAsync(ct);
            if (await EscalationOwnerAsync(authority, pending, row, ct) is null)
                throw new InvalidOperationException("moderation_recognition_escalation_owner_changed");
            await CommitAsync(transaction, ct);
            return true;
        }
        finally { context.ChangeTracker.Clear(); }
    }, token);

    public Task<ModerationRecognitionEscalation?> ReadEscalationAsync(Guid id, Guid authenticatedHuman,
        CancellationToken token) => identity.ExecuteSerializedAsync<ModerationRecognitionEscalation?>(async ct =>
    {
        await using var transaction = await BeginAsync(ct);
        try
        {
            var row = await Rows<ModerationRecognitionEscalation>().SingleOrDefaultAsync(item =>
                item.Id == id && item.RecipientHumanId == authenticatedHuman, ct);
            if (row is null || clock.GetUtcNow() >= row.RetainUntil) return null;
            var pending = await RecoveryRequestAsync(row.IntentId, ct);
            var authority = await AcquireSubjectAsync(RecoveryRequest(pending), ct);
            if (await EscalationOwnerAsync(authority, pending, row, ct) is null) return null;
            row.ValidatePersistedState();
            await CommitAsync(transaction, ct);
            return row;
        }
        finally { context.ChangeTracker.Clear(); }
    }, token);

    public Task AcknowledgeAsync(Guid id, Guid authenticatedHuman, CancellationToken token) =>
        identity.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await BeginAsync(ct);
            try
            {
                var discovered = await Rows<ModerationRecognitionEscalation>().SingleOrDefaultAsync(item =>
                    item.Id == id && item.RecipientHumanId == authenticatedHuman, ct)
                    ?? throw new UnauthorizedAccessException("Operational recovery belongs to the exact named recipient.");
                var pending = await RecoveryRequestAsync(discovered.IntentId, ct);
                var authority = await AcquireSubjectAsync(RecoveryRequest(pending), ct);
                if (await EscalationOwnerAsync(authority, pending, discovered, ct) is null)
                    throw new UnauthorizedAccessException("Operational recovery requires current independent ownership.");
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionEscalation>(context, id, ct);
                var row = await Rows<ModerationRecognitionEscalation>().SingleAsync(item => item.Id == id, ct);
                if (clock.GetUtcNow() >= row.RetainUntil)
                    throw new UnauthorizedAccessException("Operational recovery proof retention has ended.");
                context.Attach(row);
                row.Acknowledge(authenticatedHuman, clock.GetUtcNow());
                await context.SaveChangesAsync(ct);
                if (await EscalationOwnerAsync(authority, pending, row, ct) is null)
                    throw new UnauthorizedAccessException("Operational recovery ownership changed before acknowledgement.");
                await CommitAsync(transaction, ct);
                return true;
            }
            finally { context.ChangeTracker.Clear(); }
        }, token);

    private async Task<ModerationRecognitionActionRecovery?> ReadEscalationActionAsync(
        ModerationRecognitionPending pending, Guid? actionId, CancellationToken token)
    {
        if (actionId is not { } id) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionActionRecovery>(context, id, token);
        var action = await Rows<ModerationRecognitionActionRecovery>().SingleOrDefaultAsync(row => row.Id == id, token);
        if (action is null) return null;
        action.ValidatePersistedState();
        if (action.OriginalIntentId != pending.IntentId || action.PrivacyOwnerHumanId != pending.PrivacyOwnerHumanId
            || action.RecoveryOwnerHumanId != pending.RecoveryOwnerHumanId)
            throw new InvalidOperationException("moderation_recognition_escalation_target_conflict");
        return action;
    }

    private async Task<ModerationRecognitionPrivacySource?> EscalationOwnerAsync(
        CaptureAuthority authority, ModerationRecognitionPending pending, ModerationRecognitionEscalation escalation,
        CancellationToken token)
    {
        var source = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
            .SingleOrDefaultAsync(row => row.Id == escalation.SourceId, token);
        if (source is null || authority.Host is null || source.HostBindingId != authority.Host.Id
            || source.TrustDomainId != authority.Host.TrustDomainId || source.RecordedAt > pending.ReceivedAt)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacySource>(context, source.Id, token);
        source.ValidatePersistedState();
        var pair = new[] { source.PrivacyOwnerHumanId, source.RecoveryOwnerHumanId };
        if (!pair.Contains(escalation.RecipientHumanId) || !pair.Contains(escalation.OtherOwnerHumanId)) return null;
        var humans = pair.Concat([source.PrivacyControllerHumanId, source.RecoveryControllerHumanId,
            source.GovernanceControllerHumanId]).Distinct().ToArray();
        foreach (var human in humans)
        {
            if (human == pending.SubjectId || !authority.Humans.Contains(human)
                || await identity.IsSubjectFencedAsync(human, token)
                || !await Rows<User>().AnyAsync(row => row.Id == human && !row.IsDeleted, token)
                || await Rows<PrivacyErasureSaga>().AnyAsync(row => row.SubjectId == human, token)) return null;
        }
        var definition = await Rows<ModerationPolicyPublication>().SingleOrDefaultAsync(row =>
            row.Id == source.DefinitionId && row.HostBindingId == source.HostBindingId, token);
        if (definition is null
            || !await AdministratorAsync(source.PrivacyOwnerHumanId, definition.Jurisdiction, token)
            || !await AdministratorAsync(source.RecoveryOwnerHumanId, definition.Jurisdiction, token)
            || !await ReceiptAsync(source, ModerationRecognitionPrivacySourceOwner.RecordRequest(source, definition.Jurisdiction),
                ModerationQualificationSourceKind.RecognitionPrivacySource, source.Id, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)) return null;
        var adoption = await Rows<ModerationRecognitionPrivacyAdoption>().SingleOrDefaultAsync(row => row.SourceId == source.Id, token);
        if (adoption is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacyAdoption>(context, adoption.Id, token);
        adoption.ValidatePersistedState();
        // Expiry/withdrawal of matching policy cannot cancel an already received privacy
        // obligation. This historical appointment plus live independent native owners grants
        // only operational delivery/read/ack, never current legal-erasure authorization.
        return adoption.SourceRevision == source.Revision && adoption.SourceWriterGeneration == source.WriterGeneration
            && adoption.AdoptedByHumanId == source.PrivacyOwnerHumanId && adoption.AdoptedAt <= pending.ReceivedAt
            && adoption.ValidUntil > pending.ReceivedAt && source.ValidUntil > pending.ReceivedAt
            && (adoption.WithdrawnAt is null || adoption.WithdrawnAt >= pending.ReceivedAt)
            && await ReceiptAsync(source, ModerationRecognitionPrivacySourceOwner.AdoptionRequest(adoption, definition.Jurisdiction),
                ModerationQualificationSourceKind.RecognitionPrivacyAdoption, adoption.Id, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token) ? source : null;
    }

    private async Task<ModerationRecognitionPrivacySource[]> RequiredEscalationSourcesAsync(
        CaptureAuthority authority, ModerationRecognitionPending pending, ModerationRecognitionActionRecovery? action,
        CancellationToken token)
    {
        Guid[] ids;
        if (action is not null)
        {
            var item = pending.Material.SingleOrDefault(row => row.Id == action.CaptureItemId);
            if (item is null || item.ComputeMaterialReceipt() != action.CaptureMaterialReceipt)
                throw new InvalidOperationException("moderation_recognition_escalation_material_changed");
            ids = [item.SourceId];
        }
        else
        {
            var known = pending.Material.Select(row => row.SourceId).ToList();
            try { known.AddRange((await ContributionsAsync(authority, pending.SubjectId, token)).Select(row => row.Source.Id)); }
            catch (CaptureUnavailable)
            {
                // Unavailable capture is the reason for escalation, not permission to invent
                // additional owners. Original material and original named owners remain known.
            }
            known.AddRange(await Rows<ModerationRecognitionPrivacySource>().Where(row =>
                authority.Host != null && row.HostBindingId == authority.Host.Id
                && row.PrivacyOwnerHumanId == pending.PrivacyOwnerHumanId
                && row.RecoveryOwnerHumanId == pending.RecoveryOwnerHumanId && row.RecordedAt <= pending.ReceivedAt)
                .Select(row => row.Id).ToArrayAsync(token));
            ids = known.Distinct().ToArray();
        }
        var sources = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
            .Where(row => ids.Contains(row.Id)).ToArrayAsync(token);
        if (sources.Length == 0 || sources.Length != ids.Length)
            throw new InvalidOperationException("moderation_recognition_escalation_original_terms_missing");
        return sources;
    }
}
