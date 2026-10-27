using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Final-use SMTP owner, not a producer. The durable handoff attempt already exists. Policy and
/// retained erasure serialization surround a read-only primary authority transaction and a single
/// terminal transport callback; no business mutation or notification acknowledgement is committed.
/// </summary>
public sealed class ModerationNoticeEmailAuthority(
    ExploreDbContext context, ModerationNoticeReadRepository notices,
    IPrivacyIdentityFenceAuthority privacy, INotificationPreferenceResolver preferences)
    : IModerationNoticeEmailAuthority
{
    public async Task<EmailResult> SendAsync(ModerationNoticeEmailRequest request,
        Func<EmailMessage, CancellationToken, Task<EmailResult>> send, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.OutboxId == Guid.Empty
            || request.ProcessingLeaseToken == Guid.Empty || request.AttemptNumber < 1
            || !context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.ChangeTracker.HasChanges() || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId != request.TenantId || context.ModerationTrustDomainId is not null)
            return Rejected();

        // Acquire directly: a database execution strategy must never replay a transport callback.
        await using var policy = await RelationalNamedLock.AcquireSessionAsync(
            context, $"explore:setting-mutation:{GovernanceSettingKeys.Email.DeliveryEnabled}", cancellationToken);
        var entered = false;
        return await privacy.ExecuteSerializedAsync(async token =>
        {
            if (entered) return Rejected();
            entered = true;
            return await notices.SendEmailInAuthorityAsync(request, preferences, privacy, send, token);
        }, cancellationToken);
    }

    internal static EmailResult Rejected() =>
        EmailResult.Fail("moderation_notice_authority_unavailable", outcome: SmtpDeliveryOutcome.AuthorityRejected);
}

public sealed partial class ModerationNoticeReadRepository
{
    /// <summary>
    /// SMTP is a system delivery to the original recipient, not impersonated portal access.
    /// Reuse the portal's original-source/disclosure/reviewer qualification without borrowing
    /// an HTTP identity. No explanation is returned to the email consumer.
    /// </summary>
    internal async Task<EmailResult> SendEmailInAuthorityAsync(ModerationNoticeEmailRequest request,
        INotificationPreferenceResolver preferences, IPrivacyIdentityFenceAuthority privacy,
        Func<EmailMessage, CancellationToken, Task<EmailResult>> send, CancellationToken token)
    {
        if (context.ChangeTracker.HasChanges() || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId != request.TenantId || context.ModerationTrustDomainId is not null
            || context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
            return ModerationNoticeEmailAuthority.Rejected();
        context.ChangeTracker.Clear();
        await using IDbContextTransaction? ownedTransaction = context.IdentityFenceOwnsTransaction
            ? null : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || !await ProviderTenantMatchesAsync(request.TenantId, token))
            return ModerationNoticeEmailAuthority.Rejected();
        try
        {
            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
            if (host is null) return ModerationNoticeEmailAuthority.Rejected();
            context.ModerationTrustDomainId = host.TrustDomainId;
            var dispatch = await context.EmailDispatchOutbox.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TenantId == request.TenantId && row.Id == request.OutboxId, token);
            if (dispatch is null) return ModerationNoticeEmailAuthority.Rejected();
            var processor = await context.EmailDispatchProcessorStates.AsNoTracking()
                .SingleOrDefaultAsync(row => row.ProcessorCode == "smtp", token);
            var tenantControl = await context.EmailDispatchTenantControls.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TenantId == request.TenantId, token);
            if (processor is null || tenantControl is null) return ModerationNoticeEmailAuthority.Rejected();
            var notice = await context.ModerationNotices.AsNoTracking().SingleOrDefaultAsync(
                row => row.Id == dispatch.SourceId && row.TenantId == request.TenantId
                    && row.RecipientSubjectKind == 1 && row.RecipientSubjectId == dispatch.RecipientUserId, token);
            if (notice is null) return ModerationNoticeEmailAuthority.Rejected();
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .Where(row => row.EffectId == notice.EffectId && row.TenantId == request.TenantId)
                .Select(row => new { row.DecisionId, row.SnapshotHash, row.RetainUntil })
                .SingleOrDefaultAsync(token);
            if (source is null || source.RetainUntil is not { } retentionCeiling
                || clock.GetUtcNow() >= retentionCeiling)
                return ModerationNoticeEmailAuthority.Rejected();
            var decision = await context.ModerationDecisions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == source.DecisionId, token);
            if (decision is null) return ModerationNoticeEmailAuthority.Rejected();
            var reviewers = await context.ModerationReviewerAssignments.AsNoTracking()
                .Where(row => row.CaseId == decision.CaseId && row.WithdrawnAt == null).ToArrayAsync(token);
            var humans = reviewers.SelectMany(row => new Guid?[]
                    { row.ReviewerHumanId, row.ControllerHumanId, row.QualifiedByHumanId, row.AssessedByHumanId })
                .Append(dispatch.RecipientUserId).Where(id => id.HasValue).Select(id => id!.Value)
                .Distinct().Order().ToArray();
            var read = new ModerationNoticeReadRequest(request.TenantId, dispatch.RecipientUserId, notice.Id, null, 1);
            var representativePlans = await DiscoverRepresentativePlansAsync(read, host, [notice.Id], token);
            var representativeRoles = representativePlans.Values.SelectMany(plan => plan.Assignments)
                .Select(row => row.RoleId).Distinct().Order().ToArray();
            await RelationalEntityRowFence.AcquireGlobalAsync<EmailDispatchProcessorState>(context, processor.Id, token);
            await RelationalEntityRowFence.AcquireAsync<EmailDispatchTenantControl>(
                context, request.TenantId, row => row.Id, tenantControl.Id, token);
            await FenceHumansAsync(humans, request.TenantId, representativeRoles, token);
            await EventAuthoritySnapshotService.AcquirePermissionSourcesAsync(context, representativeRoles, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<UserPii>(context, dispatch.RecipientUserId, token);
            foreach (var id in await context.UserIdentityEmailClaims.AsNoTracking()
                .Where(row => row.UserId == dispatch.RecipientUserId).OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
                await RelationalEntityRowFence.AcquireGlobalAsync<UserIdentityEmailClaim>(context, id, token);
            foreach (var id in await context.UserIdentityEmailEvidence.AsNoTracking()
                .Where(row => row.UserId == dispatch.RecipientUserId).OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
                await RelationalEntityRowFence.AcquireGlobalAsync<UserIdentityEmailEvidence>(context, id, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
            foreach (var actor in representativePlans.Values.SelectMany(plan => plan.Parents)
                .SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId })
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().Order())
                await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actor, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, request.TenantId, token);
            // Parent keys fence new rows; existing opt-outs, mutes and verification evidence
            // also need their own native write fences, including currently soft-deleted copies.
            foreach (var id in await context.NotificationChannelPreferences.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
                .Where(row => row.TenantId == request.TenantId && row.OrganizationId == null && row.GroupId == null
                    && (row.UserId == null || row.UserId == dispatch.RecipientUserId))
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
                await RelationalEntityRowFence.AcquireAsync<NotificationChannelPreference>(
                    context, request.TenantId, row => row.Id, id, token);
            foreach (var id in await context.NotificationPreferenceProfiles.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
                .Where(row => row.TenantId == request.TenantId && row.OrganizationId == null && row.GroupId == null
                    && (row.UserId == null || row.UserId == dispatch.RecipientUserId))
                .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
                await RelationalEntityRowFence.AcquireAsync<NotificationPreferenceProfile>(
                    context, request.TenantId, row => row.Id, id, token);
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
                .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == request.TenantId, token);
            if (epoch is null) return ModerationNoticeEmailAuthority.Rejected();
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNotice>(context, notice.Id, token);
            await using var original = await RelationalNamedLock.AcquireTransactionAsync(
                context, $"moderation.notice:{notice.EffectId:D}", token);
            await RelationalEntityRowFence.AcquireAsync<NotificationIntent>(
                context, request.TenantId, row => row.Id, dispatch.NotificationIntentId, token);
            await RelationalEntityRowFence.AcquireAsync<EmailDispatchOutbox>(
                context, request.TenantId, row => row.Id, request.OutboxId, token);
            var acquiredAt = clock.GetUtcNow();

            async Task<EmailMessage?> QualifyAsync()
            {
                if (!ReferenceEquals(context.Database.CurrentTransaction, transaction)
                    || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
                    || context.IsTenantFilterBypassed || context.TenantFilterTenantId != request.TenantId
                    || context.ModerationTrustDomainId != host.TrustDomainId || context.ChangeTracker.HasChanges()
                    || clock.GetUtcNow() < acquiredAt || !await ProviderTenantMatchesAsync(request.TenantId, token)
                    || await privacy.IsSubjectFencedAsync(read.RecipientId, token)
                    || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == read.RecipientId, token))
                    return null;
                var currentHost = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
                if (currentHost is null || currentHost.Id != host.Id || currentHost.TrustDomainId != host.TrustDomainId
                    || currentHost.SolutionId != host.SolutionId || currentHost.InstanceId != host.InstanceId)
                    return null;
                var qualified = await ReadQualifiedAsync(notice.Id, read, host, acquiredAt, representativePlans, token);
                if (qualified is null || qualified.Entry.Source.SnapshotHash != source.SnapshotHash
                    || qualified.Entry.Notice.NativeIntentId != dispatch.NotificationIntentId)
                    return null;
                var current = await context.EmailDispatchOutbox.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == request.OutboxId && row.TenantId == request.TenantId, token);
                var intent = await context.NotificationIntents.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == dispatch.NotificationIntentId && row.TenantId == request.TenantId, token);
                if (current is null || intent is null || current.ContentRedactedAt is not null
                    || current.Status != EmailDispatchStatus.Processing
                    || current.ProcessingLeaseToken != request.ProcessingLeaseToken
                    || current.AttemptCount != request.AttemptNumber || current.RecipientUserId != read.RecipientId
                    || !ModerationNoticeEmailTemplate.Matches(current, intent, notice.Id)
                    || intent.SafePayloadHash != qualified.Entry.Source.SnapshotHash
                    || intent.DeduplicationKey != $"moderation-notice:{notice.EffectId:N}:{(int)notice.Audience}:{read.RecipientId:N}"
                    || intent.CategoryId != (int)NotificationCategoryEnum.TrustSafetyModeration
                    || intent.OwnershipTypeId != (int)NotificationOwnershipTypeEnum.IslamuEvent)
                    return null;
                var delivery = await context.NotificationDeliveries.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.TenantId == request.TenantId
                        && row.EmailDispatchOutboxId == request.OutboxId
                        && row.ChannelId == (int)NotificationPreferenceChannelEnum.Email, token);
                if (delivery is null || delivery.NotificationIntentId != intent.Id
                    || delivery.StatusId is not ((int)NotificationDeliveryStatusEnum.Pending)
                        and not ((int)NotificationDeliveryStatusEnum.Queued)
                    || delivery.DeliveryPolicyId != (int)NotificationDeliveryPolicyEnum.ModerationContextOptional
                    || delivery.PolicyVersion != 1 || delivery.IsRequired || delivery.TemplateVersion != 1
                    || delivery.TemplateKey != ModerationNoticeEmailTemplate.Key
                    || delivery.DisclosureLevel != "moderation_portal_pointer" || delivery.LinkAllowed
                    || delivery.PreferenceCategoryCode != NotificationPreferenceCategoryCodes.TrustSafety
                    || delivery.RecipientAddressSource != RecipientAddressSource.TenantUserVerifiedEmail)
                    return null;
                if (!await context.EmailDispatchAttempts.AsNoTracking().AnyAsync(row =>
                        row.TenantId == request.TenantId && row.EmailDispatchOutboxId == request.OutboxId
                        && row.AttemptNumber == request.AttemptNumber && row.Outcome == EmailDispatchAttemptOutcome.Unknown
                        && row.FailureCategory == "provider_handoff_started" && row.CompletedAt == null, token)
                    || !await context.EmailDispatchReceipts.AsNoTracking().AnyAsync(row =>
                        row.TenantId == request.TenantId && row.EmailDispatchOutboxId == request.OutboxId
                        && row.PublishEventId == current.PublishEventId && row.Status == EmailDispatchReceiptStatus.Processing, token))
                    return null;
                if (!await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == request.TenantId
                        && row.TenantStatusId == (int)TenantStatusEnum.Active, token)
                    || !await context.TenantUsers.AsNoTracking().AnyAsync(row => row.TenantId == request.TenantId
                        && row.UserId == read.RecipientId && row.StatusId == (int)TenantUserStatusEnum.Active, token)
                    || await context.EmailDispatchProcessorStates.AsNoTracking().AnyAsync(
                        row => row.ProcessorCode == "smtp" && row.IsPaused, token))
                    return null;
                var control = await context.EmailDispatchTenantControls.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.TenantId == request.TenantId, token);
                var policy = await EmailDeliveryPolicyReader.ReadAsync(context, request.TenantId, token);
                if (control?.IsPaused == true || policy.State != EmailDeliveryState.Available
                    || EmailDeliveryPolicy.ShouldSuppressOptional(policy.State, true,
                        intent.EmailDeliveryPolicyRevision, control?.OptionalSuppressedThroughRevision))
                    return null;
                var preference = await preferences.ResolveAsync(new(request.TenantId, read.RecipientId, null, null,
                    NotificationPreferenceCategoryCodes.TrustSafety, NotificationPreferenceChannelCodes.Email), token);
                if (!preference.IsEnabled) return null;
                var user = await context.Users.AsNoTracking().Include(row => row.Pii)
                    .Include(row => row.IdentityEmailClaims).ThenInclude(row => row.Evidence)
                    .SingleOrDefaultAsync(row => row.Id == read.RecipientId, token);
                var address = RecipientEmailAddressResolver.Resolve(user, read.RecipientId);
                var snapshot = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(qualified.Entry.Source.SnapshotJson)!.Notice!;
                return address.HasVerifiedEmail && clock.GetUtcNow() < qualified.ValidUntil
                    ? ModerationNoticeEmailTemplate.Create(notice.Id, address.Email!, qualified.ValidUntil,
                        new(snapshot.RetainUntil, qualified.Entry.Notification is null ? snapshot.ContestationCalendarMonths : null))
                    : null;
            }

            // Fresh final reads include preferences, current evidence-backed address and the exact
            // original finite purpose. No cached queue payload is forwarded to SMTP.
            var content = await QualifyAsync();
            token.ThrowIfCancellationRequested();
            if (content is null || clock.GetUtcNow().UtcDateTime >= content.DisclosureUntilUtc
                || content.ModerationNoticeWindow?.Allows(clock.GetUtcNow()) != true)
                return ModerationNoticeEmailAuthority.Rejected();
            return await send(content, token);
        }
        finally
        {
            context.ModerationTrustDomainId = null;
        }
    }
}
