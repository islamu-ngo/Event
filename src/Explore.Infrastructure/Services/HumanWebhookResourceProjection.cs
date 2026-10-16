using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public static class HumanWebhookResourceProjection
{
    public static HumanWebhookResourceInput? Resource(HumanWebhookResourceSourceFacts facts)
    {
        var target = facts.Target;
        var operation = target.Operation;
        string action = operation switch
        {
            HumanWebhookResourceOperation.CreateConsumer or HumanWebhookResourceOperation.CreateEndpoint => AuthorizationActions.Webhooks.Create,
            HumanWebhookResourceOperation.UpdateConsumer or HumanWebhookResourceOperation.UpdateEndpoint => AuthorizationActions.Webhooks.Update,
            HumanWebhookResourceOperation.ArchiveEndpoint => AuthorizationActions.Webhooks.Delete,
            HumanWebhookResourceOperation.PauseEndpoint => AuthorizationActions.Webhooks.Pause,
            HumanWebhookResourceOperation.ResumeEndpoint => AuthorizationActions.Webhooks.Resume,
            HumanWebhookResourceOperation.RotateEndpointSecret => AuthorizationActions.Webhooks.RotateSecret,
            HumanWebhookResourceOperation.TestEndpoint => AuthorizationActions.Webhooks.Test,
            HumanWebhookResourceOperation.RetryAttempt => AuthorizationActions.Webhooks.Retry,
            HumanWebhookResourceOperation.ScheduleBulkReplay or HumanWebhookResourceOperation.CancelBulkReplay => AuthorizationActions.Webhooks.BulkReplay,
            HumanWebhookResourceOperation.RepairProviderBinding => AuthorizationActions.Webhooks.ManageProvider,
            HumanWebhookResourceOperation.ReconcilePublication => AuthorizationActions.Webhooks.ReconcilePublication,
            HumanWebhookResourceOperation.AbandonPublication => AuthorizationActions.Webhooks.AbandonPublication,
            HumanWebhookResourceOperation.OpenPortal => AuthorizationActions.Webhooks.OpenProviderPortal,
            _ => throw new InvalidOperationException("Unknown closed webhook operation.")
        };
        if (operation is HumanWebhookResourceOperation.ScheduleBulkReplay or HumanWebhookResourceOperation.CancelBulkReplay)
        {
            if (facts.SelectedTenantId is not { } tenant || tenant != target.RequestedTenantId) return null;
            if (operation == HumanWebhookResourceOperation.CancelBulkReplay
                && !facts.BulkReplays.Any(row => row.Id == target.Id && row.TenantId == tenant)) return null;
            if (target.ConsumerFilterId is { } consumerId && !facts.Consumers.Any(row => row.Id == consumerId && row.TenantId == tenant))
                return null;
            if (target.EndpointFilterId is { } endpointId && !facts.Endpoints.Any(row => row.Id == endpointId
                && row.TenantId == tenant && (target.ConsumerFilterId is null || row.ConsumerId == target.ConsumerFilterId)))
                return null;
            return new(ResourceKinds.Webhook,
                (operation == HumanWebhookResourceOperation.ScheduleBulkReplay ? tenant : target.Id!.Value).ToString("D"),
                action, new TenantScopedAuthorizationFacts(tenant));
        }
        WebhookOwnershipScope? ownership = operation == HumanWebhookResourceOperation.CreateConsumer
            ? CreationOwnership(facts) : PersistedOwnership(facts);
        return ownership is null ? null : new(ResourceKinds.Webhook,
            (operation == HumanWebhookResourceOperation.CreateConsumer ? ownership.OwnerId : target.Id!.Value).ToString("D"),
            action, WebhookOwnershipAuthorizationFacts.From(ownership));
    }

    private static WebhookOwnershipScope? CreationOwnership(HumanWebhookResourceSourceFacts facts)
    {
        var target = facts.Target;
        var kind = (WebhookConsumerKind)target.OwnerKindId!.Value;
        Guid? requested = target.RequestedOwnerId;
        Guid? tenant = facts.SelectedTenantId;
        switch (kind)
        {
            case WebhookConsumerKind.Instance:
                var instance = facts.Instances.OrderByDescending(row => row.Generation)
                    .ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id).FirstOrDefault();
                return instance is not null && instance.Status == (int)InstanceBootstrapStatus.Completed
                    && (requested is null || requested == instance.Id)
                    ? WebhookOwnershipScope.Create(kind, null, instance.Id, null, null, null) : null;
            case WebhookConsumerKind.Tenant:
                return tenant.HasValue && (requested is null || requested == tenant)
                    ? WebhookOwnershipScope.Create(kind, tenant, null, null, null, null) : null;
            case WebhookConsumerKind.Organization:
                return tenant.HasValue && facts.Organizations.Any(row => row.Id == requested && !row.IsDeleted)
                    && facts.OrganizationPlacements.Count(row => row.OwnerId == requested && row.TenantId == tenant && !row.IsDeleted) == 1
                    ? WebhookOwnershipScope.Create(kind, tenant, null, requested, null, null) : null;
            case WebhookConsumerKind.Group:
                return tenant.HasValue && facts.Groups.Any(row => row.Id == requested && !row.IsDeleted)
                    && facts.GroupPlacements.Count(row => row.OwnerId == requested && row.TenantId == tenant && !row.IsDeleted) == 1
                    ? WebhookOwnershipScope.Create(kind, tenant, null, null, requested, null) : null;
            case WebhookConsumerKind.User:
                return tenant.HasValue && (requested is null || requested == facts.PrincipalId)
                    && facts.TenantUsers.Any(row => row.TenantId == tenant && row.UserId == facts.PrincipalId
                        && !row.IsDeleted && row.StatusId == (int)TenantUserStatusEnum.Active)
                    ? WebhookOwnershipScope.Create(kind, tenant, null, null, null, facts.PrincipalId) : null;
            default:
                return null;
        }
    }

    private static WebhookOwnershipScope? PersistedOwnership(HumanWebhookResourceSourceFacts facts)
    {
        Guid? consumerId = facts.Target.Id;
        if (facts.Target.Operation is >= HumanWebhookResourceOperation.UpdateEndpoint and <= HumanWebhookResourceOperation.RetryAttempt)
        {
            Guid? endpointId = facts.Target.Id;
            if (facts.Target.Operation == HumanWebhookResourceOperation.RetryAttempt)
            {
                var attempt = facts.Attempts.SingleOrDefault(row => row.Id == facts.Target.Id);
                if (attempt is null || facts.SelectedTenantId is { } tenant && attempt.TenantId != tenant) return null;
                var message = facts.Messages.SingleOrDefault(row => row.Id == attempt.MessageId && row.TenantId == attempt.TenantId);
                var local = facts.LocalTargets.SingleOrDefault(row => row.TenantId == attempt.TenantId
                    && row.WebhookMessageId == attempt.MessageId && row.WebhookEndpointId == attempt.EndpointId);
                var plan = local is null ? null : facts.Plans.SingleOrDefault(row => row.Id == local.DeliveryPlanSnapshotId
                    && row.TenantId == attempt.TenantId && row.WebhookMessageId == attempt.MessageId);
                var endpoint = facts.Endpoints.SingleOrDefault(row => row.Id == attempt.EndpointId);
                if (message is null || plan is null || endpoint is null || plan.WebhookConsumerId != endpoint.ConsumerId
                    || message.ConsumerId.HasValue && message.ConsumerId != endpoint.ConsumerId
                    || endpoint.TenantId.HasValue && endpoint.TenantId != attempt.TenantId) return null;
                endpointId = attempt.EndpointId;
            }
            var selected = facts.Endpoints.SingleOrDefault(row => row.Id == endpointId);
            if (selected is null) return null;
            var parent = facts.Consumers.SingleOrDefault(row => row.Id == selected.ConsumerId);
            if (parent is null || parent.TenantId != selected.TenantId || parent.InstanceId != selected.InstanceId
                || parent.ConfigurationScopeId != selected.ConfigurationScopeId) return null;
            consumerId = selected.ConsumerId;
        }
        if (facts.Target.Operation is HumanWebhookResourceOperation.ReconcilePublication or HumanWebhookResourceOperation.AbandonPublication)
        {
            var publication = facts.Publications.SingleOrDefault(row => row.Id == facts.Target.Id);
            if (publication is null || publication.TenantId != facts.SelectedTenantId) return null;
            var plan = facts.Plans.SingleOrDefault(row => row.Id == publication.WebhookDeliveryPlanSnapshotId
                && row.TenantId == publication.TenantId && row.WebhookMessageId == publication.WebhookMessageId);
            if (plan is null || !facts.Messages.Any(row => row.Id == publication.WebhookMessageId && row.TenantId == publication.TenantId))
                return null;
            consumerId = plan.WebhookConsumerId;
        }
        var consumer = facts.Consumers.SingleOrDefault(row => row.Id == consumerId);
        if (consumer is null || consumer.TenantId.HasValue && facts.SelectedTenantId.HasValue
            && consumer.TenantId != facts.SelectedTenantId) return null;
        if (facts.Target.Operation is HumanWebhookResourceOperation.ReconcilePublication or HumanWebhookResourceOperation.AbandonPublication
            && consumer.TenantId != facts.SelectedTenantId) return null;
        return Ownership(consumer);
    }

    private static WebhookOwnershipScope? Ownership(HumanWebhookConsumerFact consumer)
    {
        if (consumer.ConfigurationScopeId != (consumer.TenantId ?? consumer.InstanceId)) return null;
        try
        {
            return WebhookOwnershipScope.Create((WebhookConsumerKind)consumer.ConsumerKindId, consumer.TenantId,
                consumer.InstanceId, consumer.OrganizationId, consumer.GroupId, consumer.OwnerUserId);
        }
        catch (ArgumentException) { return null; }
    }

    public static WebhookPortalConsumerCustody? PortalConsumer(HumanWebhookResourceSourceFacts facts)
    {
        if (facts.Target.Operation != HumanWebhookResourceOperation.OpenPortal) return null;
        var ownership = PersistedOwnership(facts);
        var consumer = facts.Consumers.SingleOrDefault(row => row.Id == facts.Target.Id);
        return ownership is null || consumer is null ? null : new(consumer.Id, ownership,
            (WebhookConsumerStatus)consumer.StatusId, (WebhookProviderMode)consumer.ProviderModeId, consumer.ConfigurationVersion);
    }

    /// <summary>
    /// Reproduces retained provider custody, never provider eligibility or remote verification.
    /// The parent must also compare IsEnabled, verification state and verified tenant/consumer facts.
    /// </summary>
    public static WebhookPortalBindingCustody PortalBinding(HumanWebhookBindingFact binding) => new(
        binding.Id, binding.TenantId, binding.WebhookConsumerId, binding.InstanceId,
        (WebhookProviderKind)binding.ProviderKindId, binding.ProviderEnvironment, binding.ApplicationUid,
        binding.ExternalApplicationId, binding.ProviderVersion, (WebhookProviderCapability)binding.Capabilities,
        (WebhookProviderCapability)binding.GovernanceAllowedCapabilities, binding.CapabilityResolutionVersion,
        binding.CapabilitiesResolvedAtUtc, binding.VerifiedAtUtc, binding.ConcurrencyVersion, binding.VerificationFence);
}
