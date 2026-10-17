using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Closed source-visible transition expectations. Identity fields are never erased to make a
/// comparison pass. Unchanged rows permit business rejections; only the selected command's
/// domain transition can replace a predecessor. This is not the business validator or remote proof.
/// </summary>
internal static class HumanWebhookMutationContinuity
{
    internal static void Require(ISecureRequest command, HumanWebhookResourceSourceFacts before,
        HumanWebhookResourceSourceFacts after, bool afterMutation)
    {
        bool Consumer(HumanWebhookConsumerFact old, HumanWebhookConsumerFact current)
        {
            if (old == current) return true;
            return afterMutation && command is UpdateWebhookConsumerProviderModeCommand update
                && old.Id == update.ConsumerId && old.StatusId != (int)WebhookConsumerStatus.Archived
                && old.ConfigurationVersion == update.ExpectedConfigurationVersion
                && old.ProviderModeId != update.ProviderModeId
                && Enum.IsDefined(typeof(WebhookProviderMode), update.ProviderModeId)
                && current == old with
                {
                    ProviderModeId = update.ProviderModeId,
                    ConfigurationVersion = checked(old.ConfigurationVersion + 1)
                };
        }

        bool Endpoint(HumanWebhookEndpointFact old, HumanWebhookEndpointFact current)
        {
            if (old == current) return true;
            if (!afterMutation) return false;
            HumanWebhookEndpointFact? expected = command switch
            {
                ArchiveWebhookEndpointCommand value when value.EndpointId == old.Id
                    && old.StatusId != (int)WebhookEndpointStatus.Archived => old with
                    {
                        StatusId = (int)WebhookEndpointStatus.Archived,
                        DeliveryStateVersion = checked(old.DeliveryStateVersion + 1)
                    },
                PauseWebhookEndpointCommand value when value.EndpointId == old.Id
                    && value.ExpectedDeliveryStateVersion == old.DeliveryStateVersion
                    && old.StatusId == (int)WebhookEndpointStatus.Active
                    && LocalConsumer(before, old.ConsumerId) => old with
                    {
                        StatusId = (int)WebhookEndpointStatus.Disabled,
                        DeliveryStateVersion = checked(old.DeliveryStateVersion + 1)
                    },
                ResumeWebhookEndpointCommand value when value.EndpointId == old.Id
                    && value.ExpectedDeliveryStateVersion == old.DeliveryStateVersion
                    && old.StatusId is (int)WebhookEndpointStatus.Disabled or (int)WebhookEndpointStatus.AutoPaused
                    && LocalConsumer(before, old.ConsumerId) => old with
                    {
                        StatusId = (int)WebhookEndpointStatus.Active,
                        DeliveryStateVersion = checked(old.DeliveryStateVersion + 1)
                    },
                UpdateWebhookEndpointCommand value when value.EndpointId == old.Id
                    && value.Governance.ExpectedConfigurationVersion == old.ConfigurationVersion
                    && old.StatusId != (int)WebhookEndpointStatus.Archived => old with
                    { ConfigurationVersion = checked(old.ConfigurationVersion + 1) },
                RotateWebhookEndpointSecretCommand value when value.EndpointId == old.Id
                    && value.ExpectedConfigurationVersion == old.ConfigurationVersion
                    && old.StatusId != (int)WebhookEndpointStatus.Archived => old with
                    { ConfigurationVersion = checked(old.ConfigurationVersion + 1) },
                _ => null
            };
            return expected == current;
        }

        bool LocalTarget(HumanWebhookLocalTargetFact old, HumanWebhookLocalTargetFact current)
        {
            if (old == current) return true;
            var attempt = before.Attempts.SingleOrDefault();
            return afterMutation && command is RetryWebhookDeliveryAttemptCommand retry
                && attempt is not null && attempt.Id == retry.AttemptId
                && attempt.OutcomeId is not ((int)WebhookDeliveryAttemptOutcome.Succeeded
                    or (int)WebhookDeliveryAttemptOutcome.Scheduled or (int)WebhookDeliveryAttemptOutcome.Sending)
                && before.Endpoints.Single().StatusId == (int)WebhookEndpointStatus.Active
                && old.DeliveryStatusId is (int)WebhookLocalDeliveryStatus.DeadLettered or (int)WebhookLocalDeliveryStatus.Abandoned
                && current == old with
                {
                    DeliveryStatusId = (int)WebhookLocalDeliveryStatus.RetryDue,
                    ConcurrencyVersion = checked(old.ConcurrencyVersion + 1)
                };
        }

        bool Publication(HumanWebhookPublicationFact old, HumanWebhookPublicationFact current)
        {
            if (old == current) return true;
            if (!afterMutation) return false;
            return command switch
            {
                ReconcileWebhookProviderPublicationCommand value =>
                    value.PublicationId == old.Id && value.TenantId == old.TenantId
                    && value.ExpectedConcurrencyVersion == old.ConcurrencyVersion
                    && old.StatusId == (int)WebhookProviderPublicationStatus.ManualReconciliation
                    && current == old with
                    {
                        StatusId = (int)WebhookProviderPublicationStatus.ProviderQueued,
                        ConcurrencyVersion = checked(old.ConcurrencyVersion + 1)
                    },
                AbandonWebhookProviderPublicationCommand value =>
                    value.PublicationId == old.Id && value.TenantId == old.TenantId
                    && value.ExpectedConcurrencyVersion == old.ConcurrencyVersion
                    && old.StatusId is (int)WebhookProviderPublicationStatus.ManualReconciliation or (int)WebhookProviderPublicationStatus.DeadLettered
                    && current.AbandonedAt is not null
                    && current == old with
                    {
                        StatusId = (int)WebhookProviderPublicationStatus.Abandoned,
                        ConcurrencyVersion = checked(old.ConcurrencyVersion + 1),
                        AbandonedAt = current.AbandonedAt
                    },
                _ => false
            };
        }

        bool Bulk(HumanWebhookBulkReplayFact old, HumanWebhookBulkReplayFact current)
        {
            if (old == current) return true;
            return afterMutation && command is CancelWebhookBulkReplayCommand cancel
                && old.Id == cancel.OperationId && old.TenantId == cancel.TenantId
                && old.StatusId == (int)WebhookBulkReplayStatus.Queued
                && old.ConcurrencyVersion == cancel.ExpectedConcurrencyVersion && current.CancelledAt is not null
                && current == old with
                {
                    StatusId = (int)WebhookBulkReplayStatus.Cancelled,
                    ConcurrencyVersion = checked(old.ConcurrencyVersion + 1),
                    CancelledAt = current.CancelledAt
                };
        }

        bool bulkMatches = Rows(before.BulkReplays, after.BulkReplays, row => row.Id, Bulk);
        if (!bulkMatches && afterMutation && command is ScheduleWebhookBulkReplayCommand schedule
            && before.BulkReplays.IsEmpty && after.BulkReplays is [var created])
            bulkMatches = created.TenantId == schedule.TenantId && created.OperationKey == schedule.OperationKey
                && created.WebhookConsumerId == schedule.WebhookConsumerId && created.WebhookEndpointId == schedule.WebhookEndpointId
                && created.StatusId == (int)WebhookBulkReplayStatus.Queued && created.ConcurrencyVersion == 1
                && created.CancelledAt is null && created.CompletedAt is null;

        if (!Rows(before.Consumers, after.Consumers, row => row.Id, Consumer)
            || !Rows(before.Endpoints, after.Endpoints, row => row.Id, Endpoint)
            || !Rows(before.Attempts, after.Attempts, row => row.Id, (a, b) => a == b)
            || !Rows(before.Messages, after.Messages, row => row.Id, (a, b) => a == b)
            || !Rows(before.Plans, after.Plans, row => row.Id, (a, b) => a == b)
            || !Rows(before.LocalTargets, after.LocalTargets, row => row.Id, LocalTarget)
            || !Rows(before.Publications, after.Publications, row => row.Id, Publication)
            || !bulkMatches || !Bindings(command, before, after, afterMutation))
            throw new UnauthorizedAccessException("Webhook custody or the exact command transition changed.");

        // Creation contributors need semantic owner continuity rather than array-container or
        // raw membership equality. Resource projection already checks current existence,
        // placement/membership and selected instance ordering. Its original owner must match.
        // Creation products and newly scheduled test rows are not selected by W: the caller
        // must construct and persist those rows using that exact retained owner/parent.
        if (command is TestWebhookEndpointCommand test && test.SourceTenantId != before.SelectedTenantId)
            throw new UnauthorizedAccessException("Endpoint testing requires the selected original source tenant.");
        if (command is ReconcileWebhookProviderPublicationCommand reconcile && reconcile.TenantId != before.SelectedTenantId
            || command is AbandonWebhookProviderPublicationCommand abandon && abandon.TenantId != before.SelectedTenantId)
            throw new UnauthorizedAccessException("Publication management requires the selected original tenant.");
    }

    internal static void RequireFinalTime(ISecureRequest command, HumanWebhookResourceSourceFacts before,
        HumanWebhookResourceSourceFacts after, DateTimeOffset now)
    {
        if (command is RetryWebhookDeliveryAttemptCommand
            && !before.LocalTargets.SequenceEqual(after.LocalTargets))
        {
            // Payload expiry changes eligibility without a write. Neither an old preparation
            // nor an own-write retention extension may lengthen the original payload lifetime.
            if (before.Messages.Any(row => !row.HasPayload || row.PayloadClearedAt is not null || row.PayloadRetentionUntil <= now.UtcDateTime)
                || after.Messages.Any(row => !row.HasPayload || row.PayloadClearedAt is not null || row.PayloadRetentionUntil <= now.UtcDateTime))
                throw new UnauthorizedAccessException("The original retry payload is no longer retained.");
        }
    }

    private static bool LocalConsumer(HumanWebhookResourceSourceFacts facts, Guid consumerId) =>
        facts.Consumers.Any(row => row.Id == consumerId
            && row.ProviderModeId is (int)WebhookProviderMode.Local or (int)WebhookProviderMode.Composite);

    private static bool Rows<T>(IEnumerable<T> before, IEnumerable<T> after, Func<T, Guid> key, Func<T, T, bool> equal)
    {
        var current = after.ToDictionary(key);
        int count = 0;
        foreach (var original in before)
        {
            count++;
            if (!current.TryGetValue(key(original), out var candidate) || !equal(original, candidate)) return false;
        }
        return count == current.Count;
    }

    private static bool Bindings(ISecureRequest command, HumanWebhookResourceSourceFacts before,
        HumanWebhookResourceSourceFacts after, bool afterMutation)
    {
        if (!afterMutation || command is not RepairWebhookProviderBindingCommand repair)
            return Rows(before.Bindings, after.Bindings, row => row.Id, (a, b) => a == b);

        int changes = 0;
        foreach (var old in before.Bindings)
        {
            var current = after.Bindings.SingleOrDefault(row => row.Id == old.Id);
            if (current is null) return false;
            if (old == current) continue;
            changes++;
            if (old.VerificationStateId is (int)WebhookProviderBindingVerificationState.Rejected
                    or (int)WebhookProviderBindingVerificationState.Revoked
                || old.Id != current.Id || old.TenantId != current.TenantId
                || old.WebhookConsumerId != current.WebhookConsumerId || old.InstanceId != current.InstanceId
                || old.ConfigurationScopeId != current.ConfigurationScopeId || old.ProviderKindId != current.ProviderKindId
                || old.ProviderEnvironment != current.ProviderEnvironment || old.NormalizedEnvironment != current.NormalizedEnvironment
                || old.ApplicationUid != current.ApplicationUid || old.NormalizedApplicationUid != current.NormalizedApplicationUid
                || current.ConcurrencyVersion != checked(old.ConcurrencyVersion + 1)
                || current.VerificationFence != checked(old.VerificationFence + 1) || !Repaired(current)) return false;
        }
        foreach (var added in after.Bindings.Where(row => before.Bindings.All(old => old.Id != row.Id)))
        {
            changes++;
            if (added.ConcurrencyVersion != 2 || added.VerificationFence != 2 || !Repaired(added)) return false;
        }
        return changes <= 1;

        bool Repaired(HumanWebhookBindingFact row) =>
            row.WebhookConsumerId == repair.ConsumerId && row.TenantId == before.Consumers.Single().TenantId
            && row.ConfigurationScopeId == (row.TenantId ?? row.InstanceId)
            && row.ProviderKindId == (int)WebhookProviderKind.Svix
            && row.ExternalApplicationId == repair.ExternalApplicationId.Trim()
            && row.NormalizedExternalApplicationId == WebhookConsumerProviderBinding.NormalizeIdentity(
                repair.ExternalApplicationId, nameof(repair.ExternalApplicationId))
            && row.VerifiedTenantId == row.TenantId && row.VerifiedWebhookConsumerId == repair.ConsumerId
            && row.VerificationStateId == (int)WebhookProviderBindingVerificationState.Verified
            && row.VerifiedAtUtc is not null && row.IsEnabled
            && row.ApplicationUid == WebhookConsumerProviderBinding.CreateApplicationUid(row.InstanceId, repair.ConsumerId)
            && row.NormalizedApplicationUid == WebhookConsumerProviderBinding.NormalizeIdentity(row.ApplicationUid, nameof(row.ApplicationUid));
    }
}
