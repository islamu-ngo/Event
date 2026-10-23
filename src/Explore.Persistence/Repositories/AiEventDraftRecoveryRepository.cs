using System.Data;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed class AiEventDraftRecoveryRepository(
    ExploreDbContext dbContext,
    ITenantExploreDbContextFactory tenantContexts,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    PostgresNativeQueueEnrollment? nativeEnrollment = null) : IAiEventDraftRecoveryRepository
{
    private readonly Dictionary<Guid, Admission> _admissions = [];

    public Task<AiToolExecution?> AcquireAsync(
        AiEventDraftRecoveryIdentity identity, CancellationToken cancellationToken) =>
        ReadOwnedAsync(identity, acquire: true, cancellationToken);

    public Task<AiToolExecution?> ReadAsync(
        AiEventDraftRecoveryIdentity identity, CancellationToken cancellationToken) =>
        ReadOwnedAsync(identity, acquire: false, cancellationToken);

    private async Task<AiToolExecution?> ReadOwnedAsync(
        AiEventDraftRecoveryIdentity identity, bool acquire, CancellationToken cancellationToken)
    {
        RequireTenant(dbContext, identity.TenantId);
        RequireClean(dbContext);
        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("AI acquisition and recovery own a short independent tenant transaction.");
        if (!IsCurrentPrincipal(identity)) return null;

        // Capture both identities before any execution-strategy replay, not inside the delegate.
        Guid executionId = Guid.CreateVersion7();
        Guid reservedEventId = Guid.CreateVersion7();
        await using var context = await tenantContexts.CreateDbContextAsync(identity.TenantId, cancellationToken);
        return await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            context.ChangeTracker.Clear();
            var action = await FenceActionAsync(context, identity, token);
            if (action is null) return null;
            var existing = await context.AiToolExecutions.AsNoTracking().SingleOrDefaultAsync(
                value => value.TenantId == identity.TenantId && value.ProposedActionId == identity.ProposedActionId, token);
            if (existing is not null)
                return await MatchesExecutionAsync(context, identity, action, existing, token) ? existing : null;
            if (!acquire || action.Status != AiProposedActionStatus.Confirmed) return null;
            var image = FindImageSource(action);
            var execution = AiToolExecution.ReserveEventDraft(executionId, reservedEventId, action,
                identity.PrincipalUserId, identity.ActingActorId, image?.Id,
                image is null ? null : AiToolExecution.DigestPayload(image.ImageAttachmentsJson!));
            context.AiToolExecutions.Add(execution);
            await context.SaveChangesAsync(token);
            return execution;
        }, cancellationToken);
    }

    public async Task<AiToolExecution?> ReadForPrimaryCreationAsync(
        AiEventDraftRecoveryIdentity identity, Guid executionId, Guid reservedEventId, CancellationToken cancellationToken)
    {
        var execution = await AdmitAsync(identity, executionId, cancellationToken);
        if (execution is null || execution.ReservedEventId != reservedEventId
            || execution.CompletedAt.HasValue && !execution.Succeeded
            || !execution.PrimaryEventId.HasValue
                && _admissions[executionId].Action.Conversation!.Status != AiConversationStatus.Active)
        {
            _admissions.Remove(executionId);
            return null;
        }
        _admissions[executionId] = _admissions[executionId] with { PrimaryCreation = true };
        return execution;
    }

    public async Task<AiToolExecution?> ReadForSettlementAsync(
        AiEventDraftRecoveryIdentity identity, Guid executionId, Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        var execution = await AdmitAsync(identity, executionId, cancellationToken);
        if (execution is null || execution.ConcurrencyStamp != expectedConcurrencyStamp)
        {
            _admissions.Remove(executionId);
            return null;
        }
        return execution;
    }

    private async Task<AiToolExecution?> AdmitAsync(
        AiEventDraftRecoveryIdentity identity, Guid executionId, CancellationToken cancellationToken)
    {
        RequireTransaction(dbContext, identity.TenantId);
        RequireClean(dbContext);
        _admissions.Remove(executionId);
        if (!IsCurrentPrincipal(identity)) return null;
        var action = await FenceActionAsync(dbContext, identity, cancellationToken);
        if (action is null) return null;
        await RelationalEntityRowFence.AcquireAsync<AiToolExecution>(
            dbContext, identity.TenantId, value => value.Id, executionId, cancellationToken);
        foreach (var entry in dbContext.ChangeTracker.Entries<AiToolExecution>().Where(value => value.Entity.Id == executionId))
            entry.State = EntityState.Detached;
        var execution = await dbContext.AiToolExecutions.SingleOrDefaultAsync(
            value => value.TenantId == identity.TenantId && value.Id == executionId
                && value.ProposedActionId == identity.ProposedActionId, cancellationToken);
        if (execution is null || !await MatchesExecutionAsync(dbContext, identity, action, execution, cancellationToken))
            return null;
        _admissions[executionId] = new(identity, dbContext.Database.CurrentTransaction!.TransactionId,
            execution, action, execution.ReservedEventId!.Value, execution.PrimaryEventId, action.Status, false);
        return execution;
    }

    public async Task LinkPrimaryAsync(
        AiEventDraftRecoveryIdentity identity, Guid executionId, Event primaryEvent, CancellationToken cancellationToken)
    {
        var admission = RequireAdmission(identity, executionId);
        if (!admission.PrimaryCreation || admission.PrimaryEventId.HasValue
            || primaryEvent.Id != admission.ReservedEventId || primaryEvent.TenantId != identity.TenantId
            || primaryEvent.ActorId != identity.ActingActorId || primaryEvent.CreatedBy != identity.PrincipalUserId
            || primaryEvent.IsDeleted || dbContext.Entry(primaryEvent).State == EntityState.Detached)
            throw new InvalidOperationException("Primary linkage requires the native creator's exact admitted event.");
        bool persisted = await dbContext.Events.AsNoTracking().AnyAsync(
            value => value.Id == primaryEvent.Id && value.TenantId == identity.TenantId
                && value.ActorId == identity.ActingActorId && value.CreatedBy == identity.PrincipalUserId
                && !value.IsDeleted, cancellationToken);
        if (!persisted)
            throw new InvalidOperationException("The primary event must be persisted in this creation transaction.");
        admission.Execution.LinkPrimaryEvent(primaryEvent.Id, timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        _admissions.Remove(executionId);
    }

    public async Task SaveSettlementAsync(
        AiEventDraftRecoveryIdentity identity, AiToolExecution execution, CancellationToken cancellationToken)
    {
        var admission = RequireAdmission(identity, execution.Id);
        if (!ReferenceEquals(execution, admission.Execution) || admission.PrimaryCreation
            || execution.PrimaryEventId != admission.PrimaryEventId)
            throw new InvalidOperationException("Settlement must retain its exact admitted primary effect.");
        dbContext.ChangeTracker.DetectChanges();
        var entry = dbContext.Entry(execution);
        if (dbContext.ChangeTracker.Entries().Any(value => !ReferenceEquals(value.Entity, execution)
                && value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || entry.OriginalValues.GetValue<DateTime?>(nameof(AiToolExecution.CompletedAt)).HasValue
                && entry.Properties.Any(value => value.IsModified)
            || entry.Properties.Any(value => value.IsModified)
                && !entry.Property(value => value.ConcurrencyStamp).IsModified
            || execution.Succeeded && (!execution.CompletedAt.HasValue || !execution.PrimaryEventId.HasValue))
            throw new InvalidOperationException("AI settlement cannot flush other work or replace terminal evidence.");
        if (entry.OriginalValues.GetValue<DateTime?>(nameof(AiToolExecution.CompletedAt)).HasValue)
        {
            _admissions.Remove(execution.Id);
            return;
        }
        if (execution.ImageUploadSessionId is { } sessionId)
        {
            await RelationalEntityRowFence.AcquireAsync<StorageUploadSession>(
                dbContext, identity.TenantId, value => value.Id, sessionId, cancellationToken);
            var session = await dbContext.StorageUploadSessions.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == sessionId && value.TenantId == identity.TenantId, cancellationToken);
            if (session is null || session.UserId != identity.PrincipalUserId
                || session.IdempotencyKey != execution.ImageUploadOperationKey
                || session.Purpose != StorageObjectPurposes.EventImage
                || session.Visibility != StorageObjectVisibilities.PublicImage)
                throw new InvalidOperationException("AI image custody requires its principal's exact native upload reservation.");
            if (execution.ImageStorageObjectId is { } objectId)
            {
                if (session.Status != StorageUploadSessionStates.Finalized || session.StorageObjectId != objectId)
                    throw new InvalidOperationException("AI image custody requires its original finalized upload object.");
                await RelationalEntityRowFence.AcquireAsync<StorageObject>(
                    dbContext, identity.TenantId, value => value.Id, objectId, cancellationToken);
                if (!await dbContext.StorageObjects.AsNoTracking().AnyAsync(value =>
                    value.Id == objectId && value.TenantId == identity.TenantId && !value.IsDeleted
                    && value.CreatedBy == identity.PrincipalUserId
                    && value.Purpose == StorageObjectPurposes.EventImage
                    && value.Visibility == StorageObjectVisibilities.PublicImage
                    && value.LifecycleState == StorageObjectLifecycleStates.Active
                    && value.Provider == session.Provider && value.ObjectKey == session.ObjectKey
                    && value.StorageProviderBindingId == session.StorageProviderBindingId, cancellationToken))
                    throw new InvalidOperationException("AI image custody cannot adopt another or unavailable storage object.");
            }
        }
        if (execution.Succeeded && execution.ImageSourceMessageId.HasValue
            && (execution.ImageStorageObjectId is not { } imageId
                || !await dbContext.Events.AsNoTracking().AnyAsync(value =>
                    value.Id == execution.PrimaryEventId && value.TenantId == identity.TenantId
                    && value.FeaturedImageId == imageId && !value.IsDeleted, cancellationToken)))
            throw new InvalidOperationException("AI image settlement requires the retained object on the original draft.");
        if (execution.CompletedAt.HasValue)
        {
            if (execution.Succeeded)
            {
                if (execution.PrimaryEventId is not { } eventId)
                    throw new InvalidOperationException("Successful settlement requires a linked primary event.");
                if (admission.Action.Status == AiProposedActionStatus.Confirmed)
                    admission.Action.MarkExecuted(eventId);
            }
            else
                admission.Action.MarkFailed(execution.FailureCode!, execution.FailureMessage);
            admission.Action.Conversation!.ConcurrencyStamp = Guid.CreateVersion7();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        _admissions.Remove(execution.Id);
    }

    private Admission RequireAdmission(AiEventDraftRecoveryIdentity identity, Guid executionId)
    {
        RequireTransaction(dbContext, identity.TenantId);
        if (!IsCurrentPrincipal(identity) || !_admissions.TryGetValue(executionId, out var admission)
            || admission.Identity != identity || admission.TransactionId != dbContext.Database.CurrentTransaction!.TransactionId
            || AiEventDraftRecoveryIdentity.FromExecution(admission.Execution) != identity
            || admission.Execution.ReservedEventId != admission.ReservedEventId
            || admission.Execution.ToolName != nameof(AiProposedActionKind.CreateEventDraft)
            || admission.Execution.StartedAt != identity.ConfirmedAt
            || AiEventDraftRecoveryIdentity.FromConfirmedAction(admission.Action) != identity
            || admission.Action.Status != admission.ActionStatus
            || dbContext.Entry(admission.Execution).State == EntityState.Detached)
            throw new InvalidOperationException("AI persistence requires exact current transaction-bound admission.");
        return admission;
    }

    private async Task<AiProposedAction?> FenceActionAsync(
        ExploreDbContext context, AiEventDraftRecoveryIdentity identity, CancellationToken cancellationToken)
    {
        RequireTransaction(context, identity.TenantId);
        if (!IsCurrentPrincipal(identity)) return null;
        // Reuse the native actor/owner/organization/group/permission locks. The action ID alone
        // is not authority, and the detached candidate below is never used as a write target.
        var action = await new AiConversationRepository(context, nativeEnrollment).GetProposedActionForUpdateAsync(
            identity.ProposedActionId, identity.ActingActorId, cancellationToken);
        if (action?.Conversation is not { } conversation
            || conversation.TenantId != identity.TenantId || conversation.Id != identity.ConversationId
            || conversation.UserId != identity.PrincipalUserId || conversation.ActorId != identity.ActingActorId
            || conversation.IsDeleted || action.Kind != AiProposedActionKind.CreateEventDraft
            || action.Status is not (AiProposedActionStatus.Confirmed or AiProposedActionStatus.Executed)
            || action.TenantId != identity.TenantId || action.ConversationId != identity.ConversationId
            || action.ActingActorId != identity.ActingActorId || action.ConfirmedBy != identity.PrincipalUserId
            || action.CreatedAt != identity.ActionCreatedAt || action.ConfirmedAt != identity.ConfirmedAt
            || action.MessageId != identity.ActionMessageId
            || action.RejectedBy.HasValue || action.RejectedAt.HasValue || action.FailureCode is not null
            || action.Status == AiProposedActionStatus.Confirmed && action.ResultResourceId.HasValue
            || action.MessageId is { } messageId && !conversation.Messages.Any(value =>
                value.Id == messageId && value.ConversationId == identity.ConversationId && value.TenantId == identity.TenantId)
            || !string.Equals(AiToolExecution.DigestPayload(action.PayloadJson), identity.PayloadDigest, StringComparison.Ordinal))
            return null;

        if (Explore.Persistence.Database.ProviderPrimitives.AiRepositoryNativeOperations.For(context) is { } native)
        {
            if (nativeEnrollment is null)
                throw new InvalidOperationException("ai_conversation_owner_not_enrolled");
            // Event creation narrows the data fence to an organizer-eligible placement.
            // Native sources remain locked; no private Actor or membership read is granted.
            bool eligible = await native.FenceOwnerAsync(
                nativeEnrollment, identity.TenantId, identity.PrincipalUserId,
                identity.ActingActorId, identity.ActingActorId, false, cancellationToken, requireOrganizer: true);
            return eligible && IsCurrentPrincipal(identity) ? action : null;
        }

        var actor = await context.Actors.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == identity.ActingActorId && !value.IsDeleted && !value.IsSuspended, cancellationToken);
        bool eligibleActor = actor switch
        {
            { ActorTypeId: (int)ActorTypeEnum.User, OrganizationId: null, GroupId: null } =>
                actor.UserId == identity.PrincipalUserId,
            { ActorTypeId: (int)ActorTypeEnum.Organization, UserId: null, OrganizationId: { } organizationId, GroupId: null } =>
                await context.OrganizationTenants.AsNoTracking().AnyAsync(value =>
                    value.TenantId == identity.TenantId && value.OrganizationId == organizationId
                    && !value.IsDeleted && !value.IsSuspended && value.IsOrganizerEligible
                    && value.ApprovalStatusId == (int)ApprovalStatusEnum.Approved, cancellationToken),
            { ActorTypeId: (int)ActorTypeEnum.Group, UserId: null, OrganizationId: null, GroupId: { } groupId } =>
                await context.GroupTenants.AsNoTracking().AnyAsync(value =>
                    value.TenantId == identity.TenantId && value.GroupId == groupId
                    && !value.IsDeleted && !value.IsSuspended && value.IsOrganizerEligible
                    && value.ApprovalStatusId == (int)ApprovalStatusEnum.Approved, cancellationToken),
            _ => false
        };
        if (!eligibleActor) return null;
        var memberships = await context.TenantUsers.AsNoTracking()
            .Where(value => value.TenantId == identity.TenantId && value.UserId == identity.PrincipalUserId && !value.IsDeleted)
            .Select(value => value.Id).OrderBy(value => value).ToListAsync(cancellationToken);
        foreach (Guid membership in memberships)
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(
                context, identity.TenantId, value => value.Id, membership, cancellationToken);
        return IsCurrentPrincipal(identity) && await context.TenantUsers.AsNoTracking().AnyAsync(
            value => memberships.Contains(value.Id) && value.TenantId == identity.TenantId
                && value.UserId == identity.PrincipalUserId && !value.IsDeleted
                && value.StatusId == (int)TenantUserStatusEnum.Active, cancellationToken) ? action : null;
    }

    private static async Task<bool> MatchesExecutionAsync(
        ExploreDbContext context, AiEventDraftRecoveryIdentity identity, AiProposedAction action,
        AiToolExecution execution, CancellationToken cancellationToken)
    {
        if (!execution.HasDurablePrimaryReservation || execution.ToolName != nameof(AiProposedActionKind.CreateEventDraft)
            || AiEventDraftRecoveryIdentity.FromExecution(execution) != identity
            || execution.CompletedAt.HasValue && !execution.Succeeded
            || execution.Succeeded != (action.Status == AiProposedActionStatus.Executed))
            return false;
        var image = FindImageSource(action);
        if (execution.ImageSourceMessageId != image?.Id
            || !string.Equals(execution.ImageSourceDigest,
                image is null ? null : AiToolExecution.DigestPayload(image.ImageAttachmentsJson!), StringComparison.Ordinal))
            return false;
        if (execution.PrimaryEventId is not { } eventId)
            return action.Status == AiProposedActionStatus.Confirmed && !execution.PrimaryEffectLinkedAt.HasValue
                && !await context.Events.AsNoTracking().AnyAsync(value => value.Id == execution.ReservedEventId, cancellationToken);
        if (eventId != execution.ReservedEventId || !execution.PrimaryEffectLinkedAt.HasValue
            || action.Status == AiProposedActionStatus.Executed && action.ResultResourceId != eventId)
            return false;
        await RelationalEntityRowFence.AcquireAsync<Event>(
            context, identity.TenantId, value => value.Id, eventId, cancellationToken);
        return await context.Events.AsNoTracking().AnyAsync(
            value => value.Id == eventId && value.TenantId == identity.TenantId
                && value.ActorId == identity.ActingActorId && value.CreatedBy == identity.PrincipalUserId && !value.IsDeleted,
            cancellationToken);
    }

    private static AiMessage? FindImageSource(AiProposedAction action)
    {
        var messages = action.Conversation!.Messages;
        var actionMessage = messages.SingleOrDefault(value => value.Id == action.MessageId);
        if (actionMessage?.Role == AiMessageRole.User && !string.IsNullOrWhiteSpace(actionMessage.ImageAttachmentsJson))
            return actionMessage;
        if (action.MessageId is null) return null;
        return messages.Where(value => value.Role == AiMessageRole.User
                && value.Sequence < (actionMessage?.Sequence ?? long.MaxValue)
                && !string.IsNullOrWhiteSpace(value.ImageAttachmentsJson))
            .OrderByDescending(value => value.Sequence).FirstOrDefault();
    }

    private bool IsCurrentPrincipal(AiEventDraftRecoveryIdentity identity) =>
        currentUser.IsAuthenticated && currentUser.UserId == identity.PrincipalUserId
        && identity.PrincipalUserId != Guid.Empty && identity.ActingActorId != Guid.Empty
        && identity.ProposedActionId != Guid.Empty && identity.ConversationId != Guid.Empty;

    private static void RequireTenant(ExploreDbContext context, Guid tenantId)
    {
        if (tenantId == Guid.Empty || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId)
            throw new InvalidOperationException("AI recovery requires the exact ordinary Runtime tenant lease.");
    }

    private static void RequireTransaction(ExploreDbContext context, Guid tenantId)
    {
        RequireTenant(context, tenantId);
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("AI primary effects and settlement require their native Serializable transaction.");
    }

    private static void RequireClean(ExploreDbContext context)
    {
        if (context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("AI recovery cannot admit or flush unrelated pending changes.");
    }

    private sealed record Admission(
        AiEventDraftRecoveryIdentity Identity, Guid TransactionId, AiToolExecution Execution,
        AiProposedAction Action, Guid ReservedEventId, Guid? PrimaryEventId,
        AiProposedActionStatus ActionStatus, bool PrimaryCreation);
}
