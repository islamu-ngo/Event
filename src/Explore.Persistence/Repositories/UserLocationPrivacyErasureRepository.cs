using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class UserLocationPrivacyErasureRepository(ExploreDbContext dbContext)
    : IUserLocationPrivacyErasureRepository, IUserPrivacyErasureRepository
{
    public async Task<IReadOnlyList<PrivacyErasureProviderCandidate>> GetProviderCandidatesAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        RequireId(subjectId, nameof(subjectId));
        string reason = TenantFilterBypassReasons.UserPrivacyErasure;
        var candidates = new List<PrivacyErasureProviderCandidate>();

        candidates.AddRange(await dbContext.UserExternalLogins
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => value.UserId == subjectId
                && value.AuthenticationProviderId == (int)AuthenticationProviderKind.Keycloak)
            .Select(value => new PrivacyErasureProviderCandidate(
                PrivacyErasureProviderKind.Keycloak,
                PrivacyErasureProviderAction.RevokeOrUnlinkExternalIdentity,
                null,
                value.Id,
                PrivacyErasureProviderLocatorKind.AccountIdentifier,
                value.ProviderKey))
            .ToArrayAsync(cancellationToken));
        candidates.AddRange(await dbContext.AtprotoIdentities
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => value.Actor.UserId == subjectId)
            .Select(value => new PrivacyErasureProviderCandidate(
                PrivacyErasureProviderKind.Atproto,
                PrivacyErasureProviderAction.RevokeOrUnlinkExternalIdentity,
                null,
                value.Id,
                PrivacyErasureProviderLocatorKind.Did,
                value.Did))
            .ToArrayAsync(cancellationToken));
        candidates.AddRange(await dbContext.WebPushSubscriptions
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => value.UserId == subjectId)
            .Select(value => new PrivacyErasureProviderCandidate(
                PrivacyErasureProviderKind.WebPush,
                PrivacyErasureProviderAction.InvalidateSubscription,
                value.TenantId,
                value.Id,
                PrivacyErasureProviderLocatorKind.WebPushEndpoint,
                value.Endpoint))
            .ToArrayAsync(cancellationToken));
        candidates.AddRange(await dbContext.StorageObjects
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => value.Actor != null
                && value.Actor.UserId == subjectId
                && value.ObjectKey != null)
            .Select(value => new PrivacyErasureProviderCandidate(
                PrivacyErasureProviderKind.ObjectStorage,
                PrivacyErasureProviderAction.DeleteOwnedObject,
                value.TenantId,
                value.Id,
                PrivacyErasureProviderLocatorKind.ObjectKey,
                value.ObjectKey!))
            .ToArrayAsync(cancellationToken));
        IQueryable<Guid> registrationFileStorageIds = OwnedRegistrationFileStorageIds(subjectId, reason);
        candidates.AddRange(await dbContext.StorageObjects
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => registrationFileStorageIds.Contains(value.Id) && value.ObjectKey != null)
            .Select(value => new PrivacyErasureProviderCandidate(
                PrivacyErasureProviderKind.ObjectStorage,
                PrivacyErasureProviderAction.DeleteOwnedObject,
                value.TenantId,
                value.Id,
                PrivacyErasureProviderLocatorKind.ObjectKey,
                value.ObjectKey!))
            .ToArrayAsync(cancellationToken));
        candidates.AddRange(await dbContext.StorageUploadSessions
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => value.UserId == subjectId && value.ObjectKey != null)
            .Select(value => new PrivacyErasureProviderCandidate(
                PrivacyErasureProviderKind.ObjectStorage,
                PrivacyErasureProviderAction.DeleteOwnedObject,
                value.TenantId,
                value.Id,
                PrivacyErasureProviderLocatorKind.ObjectKey,
                value.ObjectKey!))
            .ToArrayAsync(cancellationToken));
        candidates.AddRange(await dbContext.EmailDispatchOutbox
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => value.RecipientUserId == subjectId && value.ProviderMessageId != null)
            .Select(value => new PrivacyErasureProviderCandidate(
                PrivacyErasureProviderKind.Smtp,
                PrivacyErasureProviderAction.ExpireLocalMetadataWithoutRecall,
                value.TenantId,
                value.Id,
                PrivacyErasureProviderLocatorKind.ProviderResourceIdentifier,
                value.ProviderMessageId!))
            .ToArrayAsync(cancellationToken));
        candidates.AddRange(await dbContext.WebhookEndpoints
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => value.Consumer != null
                && value.Consumer.OwnerUserId == subjectId
                && value.ProviderEndpointId != null)
            .Select(value => new PrivacyErasureProviderCandidate(
                PrivacyErasureProviderKind.Webhook,
                PrivacyErasureProviderAction.DeleteOrAnonymizeProviderCopy,
                value.TenantId,
                value.Id,
                PrivacyErasureProviderLocatorKind.ProviderResourceIdentifier,
                value.ProviderEndpointId!))
            .ToArrayAsync(cancellationToken));
        candidates.AddRange(await dbContext.EventReportExternalLinks
            .IgnoreAllFilters(reason)
            .AsNoTracking()
            .Where(value => value.Report != null
                && value.Report.ReporterUserId == subjectId
                && (value.ProviderCaseId != null || value.ProviderSignalId != null))
            .Select(value => new PrivacyErasureProviderCandidate(
                value.Provider == EventReportExternalProvider.Osprey
                    ? PrivacyErasureProviderKind.Osprey
                    : PrivacyErasureProviderKind.Coop,
                PrivacyErasureProviderAction.CorrectOrDeleteProviderCopy,
                value.TenantId,
                value.Id,
                PrivacyErasureProviderLocatorKind.ProviderResourceIdentifier,
                value.ProviderCaseId ?? value.ProviderSignalId!))
            .ToArrayAsync(cancellationToken));

        return candidates
            .OrderBy(value => value.ProviderKind)
            .ThenBy(value => value.TenantId)
            .ThenBy(value => value.TargetId)
            .ToArray();
    }

    public async Task EraseProviderBackedLocalUserMetadataAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        RequireId(subjectId, nameof(subjectId));
        string reason = TenantFilterBypassReasons.UserPrivacyErasure;
        DateTime utcNow = DateTime.UtcNow;
        Guid[] webhookConsumerIds = await dbContext.WebhookConsumers
            .IgnoreAllFilters(reason)
            .Where(value => value.OwnerUserId == subjectId)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var uploadReservations = await dbContext.StorageUploadSessions
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId
                && value.ReservedBytes > 0
                && (value.Status == StorageUploadSessionStates.Reserved
                    || value.Status == StorageUploadSessionStates.Uploading))
            .GroupBy(value => new { value.TenantId, value.Provider })
            .Select(group => new
            {
                group.Key.TenantId,
                group.Key.Provider,
                ReservedBytes = group.Sum(value => value.ReservedBytes)
            })
            .ToArrayAsync(cancellationToken);

        foreach (var reservation in uploadReservations)
        {
            await dbContext.StorageUsageCounters
                .IgnoreAllFilters(reason)
                .Where(value => value.TenantId == reservation.TenantId
                    && value.Provider == reservation.Provider)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        value => value.ReservedBytes,
                        value => value.ReservedBytes >= reservation.ReservedBytes
                            ? value.ReservedBytes - reservation.ReservedBytes
                            : 0)
                    .SetProperty(value => value.UpdatedAt, utcNow)
                    .SetProperty(value => value.UpdatedBy, (Guid?)null), cancellationToken);
        }

        await dbContext.UserExternalLogins
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.WebPushDispatchOutbox
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.WebPushSubscriptions
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationDeliveries
            .IgnoreAllFilters(reason)
            .Where(value => value.NotificationIntent != null
                && value.NotificationIntent.RecipientUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EmailDispatchOutbox
            .IgnoreAllFilters(reason)
            .Where(value => value.RecipientUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.IntegrationSyncOutbox
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.StorageUploadSessions
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.UserId, (Guid?)null)
                .SetProperty(value => value.ReservedBytes, 0L)
                .SetProperty(value => value.ObjectKey, (string?)null)
                .SetProperty(value => value.Sha256Checksum, (string?)null)
                .SetProperty(value => value.OriginalFileName, (string?)null)
                .SetProperty(value => value.SafeDisplayName, string.Empty)
                .SetProperty(value => value.IdempotencyKey, (string?)null)
                .SetProperty(value => value.FailureMessage, (string?)null)
                .SetProperty(value => value.CreatedBy, (Guid?)null)
                .SetProperty(value => value.UpdatedAt, utcNow)
                .SetProperty(value => value.UpdatedBy, (Guid?)null), cancellationToken);
        await dbContext.StorageObjects
            .IgnoreAllFilters(reason)
            .Where(value => value.Actor != null
                && value.Actor.UserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Uri, string.Empty)
                .SetProperty(value => value.ObjectKey, (string?)null)
                .SetProperty(value => value.Provider, StorageProviders.Local)
                .SetProperty(value => value.FullName, string.Empty)
                .SetProperty(value => value.SafeDisplayName, string.Empty)
                .SetProperty(value => value.ContentType, (string?)null)
                .SetProperty(value => value.Sha256Checksum, (string?)null)
                .SetProperty(value => value.OwningResourceKind, (string?)null)
                .SetProperty(value => value.OwningResourceId, (Guid?)null)
                .SetProperty(value => value.ActorId, (Guid?)null)
                .SetProperty(value => value.LifecycleState, StorageObjectLifecycleStates.Deleted)
                .SetProperty(value => value.IsDeleted, true)
                .SetProperty(value => value.DeletedAt, utcNow)
                .SetProperty(value => value.DeletedBy, subjectId)
                .SetProperty(value => value.UpdatedAt, utcNow)
                .SetProperty(value => value.UpdatedBy, subjectId), cancellationToken);
        await dbContext.WebhookLocalTargetSnapshots
            .IgnoreAllFilters(reason)
            .Where(value => value.WebhookEndpoint != null
                && webhookConsumerIds.Contains(value.WebhookEndpoint.ConsumerId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.DestinationUrl, string.Empty)
                .SetProperty(value => value.CredentialReference, string.Empty)
                .SetProperty(value => value.UpdatedAt, utcNow), cancellationToken);
        await dbContext.WebhookEndpoints
            .IgnoreAllFilters(reason)
            .Where(value => webhookConsumerIds.Contains(value.ConsumerId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Url, string.Empty)
                .SetProperty(value => value.Description, (string?)null)
                .SetProperty(value => value.SecretRef, string.Empty)
                .SetProperty(value => value.PreviousSecretRef, (string?)null)
                .SetProperty(value => value.PreviousSecretValidUntil, (DateTime?)null)
                .SetProperty(value => value.ProviderEndpointId, (string?)null)
                .SetProperty(value => value.StatusId, (int)WebhookEndpointStatus.Archived)
                .SetProperty(value => value.AutoPauseReason, (string?)null)
                .SetProperty(value => value.LastResumedAt, (DateTime?)null)
                .SetProperty(value => value.LastResumedBy, (Guid?)null)
                .SetProperty(value => value.UpdatedAt, utcNow), cancellationToken);
        foreach (Guid consumerId in webhookConsumerIds)
        {
            await dbContext.WebhookConsumers
                .IgnoreAllFilters(reason)
                .Where(value => value.Id == consumerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.OwnerUserId, (Guid?)null)
                    .SetProperty(value => value.ConsumerKindId, (int)WebhookConsumerKind.Tenant)
                    .SetProperty(value => value.Name, $"Deleted user {consumerId:N}")
                    .SetProperty(value => value.StatusId, (int)WebhookConsumerStatus.Archived)
                    .SetProperty(value => value.ExternalProviderAppId, (string?)null)
                    .SetProperty(value => value.ProviderModeId, (int)WebhookProviderMode.Local)
                    .SetProperty(value => value.ConfigurationVersion, value => value.ConfigurationVersion + 1)
                    .SetProperty(value => value.UpdatedAt, utcNow), cancellationToken);
        }
        await dbContext.EventReportExternalLinks
            .IgnoreAllFilters(reason)
            .Where(value => value.Report != null
                && value.Report.ReporterUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ProviderCaseId, (string?)null)
                .SetProperty(value => value.ProviderSignalId, (string?)null)
                .SetProperty(value => value.ProviderUrl, (string?)null)
                .SetProperty(value => value.SyncState, EventReportSyncState.Disabled)
                .SetProperty(value => value.LastSyncedAt, (DateTime?)null)
                .SetProperty(value => value.LastErrorCategory, (string?)null)
                .SetProperty(value => value.UpdatedAt, utcNow), cancellationToken);
    }

    public async Task AnonymizeRetainedAuditEvidenceAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        RequireId(subjectId, nameof(subjectId));
        string reason = TenantFilterBypassReasons.UserPrivacyErasure;
        Guid[] tenantUserIds = await dbContext.TenantUsers
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);

        await dbContext.TenantInvitations
            .IgnoreAllFilters(reason)
            .Where(value => value.AcceptedByUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.TenantInvitations
            .IgnoreAllFilters(reason)
            .Where(value => value.InvitedByUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.InvitedByUserId, (Guid?)null), cancellationToken);
        await dbContext.EventContactShareExports
            .IgnoreAllFilters(reason)
            .Where(value => value.ExportedByUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ExportedByUserId, (Guid?)null), cancellationToken);
        await dbContext.EventContactShareConsentHistory
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.UserId, (Guid?)null), cancellationToken);
        await dbContext.ConfigurationChangeLogs
            .Where(value => value.UserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.UserId, (Guid?)null), cancellationToken);
        await dbContext.EventOrganizerClaims
            .IgnoreAllFilters(reason)
            .Where(value => value.ReviewerUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ReviewerUserId, (Guid?)null), cancellationToken);
        await dbContext.Events
            .IgnoreAllFilters(reason)
            .Where(value => value.SubmittedByUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.SubmittedByUserId, (Guid?)null), cancellationToken);
        await dbContext.OrganizationReviews
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.UserId, (Guid?)null)
                .SetProperty(value => value.ReviewerName, "Deleted user"), cancellationToken);
        await dbContext.SupportAccessAuditEvents
            .Where(value => value.ActorUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ActorUserId, (Guid?)null), cancellationToken);
        await dbContext.SupportAccessAuditEvents
            .Where(value => value.TargetTenantUserId.HasValue
                && tenantUserIds.Contains(value.TargetTenantUserId.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.TargetTenantUserId, (Guid?)null), cancellationToken);
        DateTimeOffset supportAccessEndedAt = DateTimeOffset.UtcNow;
        await dbContext.SupportAccessSessions
            .Where(value => value.ActorUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ActorUserId, (Guid?)null)
                .SetProperty(value => value.StatusId, (int)SupportAccessSessionStatusEnum.Revoked)
                .SetProperty(value => value.EndReasonId, (int?)SupportAccessEndReasonEnum.RevokedByPolicy)
                .SetProperty(value => value.EndedAtUtc, (DateTimeOffset?)supportAccessEndedAt), cancellationToken);
        await dbContext.SupportAccessSessions
            .Where(value => value.ApprovedByUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ApprovedByUserId, (Guid?)null), cancellationToken);
        await dbContext.SupportAccessSessions
            .Where(value => value.TargetTenantUserId.HasValue
                && tenantUserIds.Contains(value.TargetTenantUserId.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.TargetTenantUserId, (Guid?)null), cancellationToken);
        await dbContext.TenantLifecycleLogs
            .Where(value => value.TransitionedByUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.TransitionedByUserId, (Guid?)null), cancellationToken);
        await dbContext.TenantPlanApplicationLogs
            .Where(value => value.AppliedByUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.AppliedByUserId, (Guid?)null), cancellationToken);
        await dbContext.TenantPlanAssignments
            .Where(value => value.AssignedByUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.AssignedByUserId, (Guid?)null), cancellationToken);
        await dbContext.EventReportExternalLinks
            .IgnoreAllFilters(reason)
            .Where(value => value.Report != null && value.Report.ReporterUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EventReportEvidenceItems
            .IgnoreAllFilters(reason)
            .Where(value => value.CreatedByUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EventReports
            .IgnoreAllFilters(reason)
            .Where(value => value.ReporterUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ReporterUserId, (Guid?)null)
                .SetProperty(value => value.ReporterActorId, (Guid?)null)
                .SetProperty(value => value.ReporterLocale, (string?)null)
                .SetProperty(value => value.ReporterIpHash, (string?)null)
                .SetProperty(value => value.ReporterUserAgentHash, (string?)null)
                .SetProperty(value => value.ReportCaseUpdatesConsent, false)
                .SetProperty(value => value.ReportFollowUpContactConsent, false), cancellationToken);
        await dbContext.EventModerationRecords
            .IgnoreAllFilters(reason)
            .Where(value => value.ModeratorUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ModeratorUserId, (Guid?)null), cancellationToken);
        await dbContext.EventReportCases
            .IgnoreAllFilters(reason)
            .Where(value => value.AssignedModeratorUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.AssignedModeratorUserId, (Guid?)null), cancellationToken);
        await dbContext.EventReportDecisions
            .IgnoreAllFilters(reason)
            .Where(value => value.ModeratorUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ModeratorUserId, (Guid?)null), cancellationToken);
        await dbContext.AuditLogs
            .IgnoreAllFilters(reason)
            .Where(value => value.ActorId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ActorId, (Guid?)null)
                .SetProperty(value => value.OldValues, (string?)null)
                .SetProperty(value => value.NewValues, (string?)null), cancellationToken);
        await dbContext.EventRoleAssignments
            .IgnoreAllFilters(reason)
            .Where(value => value.RevokedByUserId == subjectId && value.UserId != subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.RevokedByUserId, (Guid?)null), cancellationToken);
        await dbContext.EventLocations
            .IgnoreAllFilters(reason)
            .Where(value => value.LastPolicyActorUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.LastPolicyActorUserId, (Guid?)null), cancellationToken);
        await dbContext.InstanceBootstrapStates
            .Where(value => value.CompletedByUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.CompletedByUserId, (Guid?)null), cancellationToken);
        await dbContext.ManagedTenantProvisioningOperations
            .Where(value => value.TenantAdministratorUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.TenantAdministratorUserId, (Guid?)null), cancellationToken);
    }

    public async Task EraseRegistrationAndLocalNotificationsAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        RequireId(subjectId, nameof(subjectId));
        string reason = TenantFilterBypassReasons.UserPrivacyErasure;
        Guid[] tenantUserIds = await dbContext.TenantUsers
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        await EraseRegistrationAnswerFilesAsync(subjectId, cancellationToken);

        await dbContext.NotificationFanoutRuns
            .IgnoreAllFilters(reason)
            .Where(value => value.CursorUserId == subjectId
                || (value.CursorSubscriberTenantUserId.HasValue
                    && tenantUserIds.Contains(value.CursorSubscriberTenantUserId.Value)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.CursorUserId, (Guid?)null)
                .SetProperty(value => value.CursorSubscriberTenantUserId, (Guid?)null), cancellationToken);
        await dbContext.NotificationDeliveries
            .IgnoreAllFilters(reason)
            .Where(value => value.NotificationIntent != null
                && value.NotificationIntent.RecipientUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationExternalDelegations
            .IgnoreAllFilters(reason)
            .Where(value => value.NotificationIntent != null
                && value.NotificationIntent.RecipientUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Notifications
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationIntents
            .IgnoreAllFilters(reason)
            .Where(value => value.RecipientUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EventContactShareExportItems
            .Where(value => value.Consent != null && value.Consent.UserSubjectId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EventContactShareConsentHistory
            .IgnoreAllFilters(reason)
            .Where(value => value.Consent != null && value.Consent.UserSubjectId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EventContactShareConsents
            .IgnoreAllFilters(reason)
            .Where(value => value.UserSubjectId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.RegistrationParticipantPii
            .IgnoreAllFilters(reason)
            .Where(value => value.RegistrationParticipant != null
                && value.RegistrationParticipant.LinkedUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ParticipantAdmissionEligibilities
            .IgnoreAllFilters(reason)
            .Where(value => value.SubjectUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    value => value.SubjectUserId,
                    (Guid?)null)
                .SetProperty(
                    value => value.RequirementsCompletedAt,
                    (DateTime?)null)
                .SetProperty(
                    value => value.SubjectConsentRecordId,
                    (Guid?)null)
                .SetProperty(
                    value => value.SubjectConsentGrantedAt,
                    (DateTime?)null),
                cancellationToken);
        await dbContext.RegistrationParticipants
            .IgnoreAllFilters(reason)
            .Where(value => value.LinkedUserId == subjectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.LinkedUserId, (Guid?)null), cancellationToken);
        await dbContext.EventRegistrations
            .IgnoreAllFilters(reason)
            .Where(value => value.LinkedUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        string subjectKey = subjectId.ToString();
        await dbContext.IdempotencyRecords
            .Where(value => value.UserId == subjectKey)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task EraseRegistrationAnswerFilesAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        RequireId(subjectId, nameof(subjectId));
        string reason = TenantFilterBypassReasons.UserPrivacyErasure;
        RegistrationAnswerFile[] files = await OwnedRegistrationFiles(subjectId, reason)
            .ToArrayAsync(cancellationToken);
        if (files.Length == 0)
        {
            return;
        }

        Guid[] fileIds = files.Select(file => file.Id).ToArray();
        Guid[] storageIds = files.Select(file => file.StorageObjectId).Distinct().ToArray();
        StorageObject[] storageObjects = await dbContext.StorageObjects
            .IgnoreAllFilters(reason)
            .Where(storage => storageIds.Contains(storage.Id))
            .ToArrayAsync(cancellationToken);
        DateTime utcNow = DateTime.UtcNow;
        foreach (StorageObject storage in storageObjects)
        {
            storage.Uri = string.Empty;
            storage.ObjectKey = null;
            storage.FullName = string.Empty;
            storage.SafeDisplayName = string.Empty;
            storage.ContentType = null;
            storage.Sha256Checksum = null;
            storage.MarkDeleted(subjectId, utcNow);
        }

        RegistrationAnswerFileRelease[] releases = await dbContext.RegistrationAnswerFileReleases
            .IgnoreAllFilters(reason)
            .Where(release => fileIds.Contains(release.RegistrationAnswerFileId))
            .ToArrayAsync(cancellationToken);
        dbContext.RegistrationAnswerFileReleases.RemoveRange(releases);
        dbContext.RegistrationAnswerFiles.RemoveRange(files);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<RegistrationAnswerFile> OwnedRegistrationFiles(Guid subjectId, string reason)
        => from file in dbContext.RegistrationAnswerFiles.IgnoreAllFilters(reason)
           join submission in dbContext.RegistrationSubmissions.IgnoreAllFilters(reason)
               on new { file.TenantId, file.EventId, Id = file.RegistrationSubmissionId }
               equals new { submission.TenantId, submission.EventId, submission.Id }
           join order in dbContext.RegistrationOrders.IgnoreAllFilters(reason)
               on new { submission.TenantId, submission.EventId, Id = submission.RegistrationOrderId }
               equals new { order.TenantId, order.EventId, order.Id }
           where order.AccountUserId == subjectId
           select file;

    private IQueryable<Guid> OwnedRegistrationFileStorageIds(Guid subjectId, string reason)
        => OwnedRegistrationFiles(subjectId, reason).Select(file => file.StorageObjectId);

    public async Task EraseMembershipsAndPreferencesAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        RequireId(subjectId, nameof(subjectId));
        string reason = TenantFilterBypassReasons.UserPrivacyErasure;

        await dbContext.ActorKeyStores
            .IgnoreAllFilters(reason)
            .Where(value => value.Actor.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.AiConsentGrants
            .IgnoreAllFilters(reason)
            .Where(value => value.SubjectUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ExternalApiKeys
            .Where(value => value.ExternalApiKeyOwnerTypeId == (int)ExternalApiKeyOwnerType.User
                && value.OwnerId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserAppearancePreferences
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserAppearanceProfiles
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationChannelPreferences
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.NotificationPreferenceProfiles
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserNotificationPreferences
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserPreferences
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.EventRoleAssignments
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ActorSubscriptions
            .IgnoreAllFilters(reason)
            .Where(value => value.SubscriberUserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.GroupMembers
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.OrganizationMembers
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.TenantUsers
            .IgnoreAllFilters(reason)
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.PlatformUserRoles
            .Where(value => value.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Location>> GetOwnedPrivateHomesAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        RequireId(ownerUserId, nameof(ownerUserId));
        List<Location> homes = await dbContext.Locations
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(location => location.OwnerUserId == ownerUserId
                && location.LocationKindId == (int)LocationKindEnum.PrivateHome)
            .OrderBy(location => location.TenantId)
            .ThenBy(location => location.Id)
            .ToListAsync(cancellationToken);
        if (homes.Count == 0)
        {
            return homes;
        }

        Guid[] locationIds = homes.Select(home => home.Id).ToArray();
        List<LocationRoom> rooms = await dbContext.LocationRooms
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
            .IncludeDeleted()
            .Where(room => locationIds.Contains(room.LocationId))
            .OrderBy(room => room.LocationId)
            .ThenBy(room => room.Id)
            .ToListAsync(cancellationToken);
        ILookup<Guid, LocationRoom> roomsByLocation = rooms.ToLookup(room => room.LocationId);
        foreach (Location home in homes)
        {
            home.Rooms = roomsByLocation[home.Id].ToList();
        }

        return homes;
    }

    public async Task<IReadOnlyList<EventLocation>> GetEventLocationsAsync(
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locationIds);
        Guid[] normalizedIds = locationIds.Distinct().ToArray();
        if (normalizedIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Location ids must be non-empty.", nameof(locationIds));
        }

        if (normalizedIds.Length == 0)
        {
            return [];
        }

        return await dbContext.EventLocations
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(eventLocation => eventLocation.LocationId.HasValue
                && normalizedIds.Contains(eventLocation.LocationId.Value))
            .OrderBy(eventLocation => eventLocation.TenantId)
            .ThenBy(eventLocation => eventLocation.EventId)
            .ThenBy(eventLocation => eventLocation.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Actor>> GetUserActorsAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        RequireId(ownerUserId, nameof(ownerUserId));
        return await dbContext.Actors
            .IncludeDeleted()
            .Include(actor => actor.Pii)
            .Include(actor => actor.AtprotoIdentities)
            .Where(actor => actor.UserId == ownerUserId)
            .OrderBy(actor => actor.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(
        IReadOnlyCollection<EventLocationDisclosureAudit> audits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audits);
        dbContext.EventLocationDisclosureAudits.AddRange(audits);
        await dbContext.SavePrivacyErasureChangesAsync(cancellationToken);
    }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }
}
