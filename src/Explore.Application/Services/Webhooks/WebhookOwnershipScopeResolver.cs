using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Webhooks;

public sealed class WebhookOwnershipScopeResolver(
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    IInstanceBootstrapStateRepository instanceBootstrapStateRepository,
    IOrganizationRepository organizationRepository,
    IGroupRepository groupRepository,
    ITenantUserRepository tenantUserRepository,
    IWebhookConsumerRepository webhookConsumerRepository,
    IWebhookEndpointRepository webhookEndpointRepository,
    IWebhookMessageRepository webhookMessageRepository,
    IWebhookDeliveryAttemptRepository webhookDeliveryAttemptRepository) : IWebhookOwnershipScopeResolver
{
    public async Task<WebhookOwnershipScopeResolution> ResolveAsync(
        int ownerKindId,
        Guid? requestedOwnerId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(WebhookConsumerKind), ownerKindId))
        {
            return Failed("webhook_owner_kind_invalid", "Webhook owner kind is invalid.");
        }

        if (requestedOwnerId == Guid.Empty)
        {
            return Failed("webhook_owner_id_invalid", "Webhook owner id must not be empty.");
        }

        var kind = (WebhookConsumerKind)ownerKindId;
        return kind switch
        {
            WebhookConsumerKind.Instance => await ResolveInstanceAsync(requestedOwnerId, cancellationToken),
            WebhookConsumerKind.Tenant => ResolveTenant(requestedOwnerId),
            WebhookConsumerKind.Organization => await ResolveOrganizationAsync(requestedOwnerId, cancellationToken),
            WebhookConsumerKind.Group => await ResolveGroupAsync(requestedOwnerId),
            WebhookConsumerKind.User => await ResolveUserAsync(requestedOwnerId, cancellationToken),
            _ => Failed("webhook_owner_kind_invalid", "Webhook owner kind is invalid.")
        };
    }

    public async Task<WebhookOwnershipScopeResolution> ResolvePersistedAsync(
        WebhookOwnedResourceKind resourceKind,
        Guid resourceId,
        CancellationToken cancellationToken)
    {
        if (resourceId == Guid.Empty)
        {
            return Failed("webhook_resource_id_invalid", "Webhook resource id must not be empty.");
        }

        WebhookConsumer? consumer;
        switch (resourceKind)
        {
            case WebhookOwnedResourceKind.Consumer:
                consumer = await webhookConsumerRepository.GetByIdForOwnerOperationAsync(
                    resourceId,
                    forUpdate: false,
                    cancellationToken);
                break;
            case WebhookOwnedResourceKind.Endpoint:
                var endpoint = await webhookEndpointRepository.GetByIdForOwnerOperationAsync(
                    resourceId,
                    forUpdate: false,
                    cancellationToken);
                consumer = endpoint?.Consumer;
                break;
            case WebhookOwnedResourceKind.Message:
                var message = await webhookMessageRepository.GetByIdForOwnerOperationAsync(
                    resourceId,
                    cancellationToken);
                consumer = message?.Consumer;
                if (message is not null && consumer is null)
                {
                    return Resolved(WebhookOwnershipScope.Create(
                        WebhookConsumerKind.Tenant,
                        message.TenantId,
                        null,
                        null,
                        null,
                        null));
                }
                break;
            case WebhookOwnedResourceKind.DeliveryAttempt:
                var attempt = await webhookDeliveryAttemptRepository.GetByIdForOwnerOperationAsync(
                    resourceId,
                    cancellationToken);
                consumer = attempt?.Endpoint?.Consumer;
                break;
            default:
                return Failed("webhook_resource_kind_invalid", "Webhook resource kind is invalid.");
        }

        if (consumer is null)
        {
            return Failed("webhook_resource_not_found", "Webhook resource was not found.");
        }

        try
        {
            return Resolved(consumer.Ownership);
        }
        catch (InvalidOperationException)
        {
            return Failed("webhook_owner_invalid", "Webhook resource ownership is invalid.");
        }
    }

    private async Task<WebhookOwnershipScopeResolution> ResolveInstanceAsync(
        Guid? requestedOwnerId,
        CancellationToken cancellationToken)
    {
        var state = await instanceBootstrapStateRepository.GetCurrent(cancellationToken);
        if (state is null || state.Status != InstanceBootstrapStatus.Completed || state.Id == Guid.Empty)
        {
            return Failed(
                "webhook_instance_identity_unavailable",
                "Instance webhook ownership requires completed instance onboarding.");
        }

        if (requestedOwnerId.HasValue && requestedOwnerId != state.Id)
        {
            return Failed("webhook_owner_mismatch", "Webhook owner does not match the current instance.");
        }

        return Resolved(WebhookOwnershipScope.Create(
            WebhookConsumerKind.Instance,
            null,
            state.Id,
            null,
            null,
            null));
    }

    private WebhookOwnershipScopeResolution ResolveTenant(Guid? requestedOwnerId)
    {
        var tenantId = tenantContext.TenantId;
        if (requestedOwnerId.HasValue && requestedOwnerId != tenantId)
        {
            return Failed("webhook_owner_mismatch", "Webhook owner does not match the current tenant.");
        }

        return Resolved(WebhookOwnershipScope.Create(
            WebhookConsumerKind.Tenant,
            tenantId,
            null,
            null,
            null,
            null));
    }

    private async Task<WebhookOwnershipScopeResolution> ResolveOrganizationAsync(
        Guid? requestedOwnerId,
        CancellationToken cancellationToken)
    {
        if (!requestedOwnerId.HasValue)
        {
            return Failed("webhook_owner_id_required", "Organization webhook ownership requires an organization id.");
        }

        var organization = await organizationRepository.GetOrganizationWithDetails(
            requestedOwnerId.Value,
            cancellationToken);
        var participation = organization?.TenantParticipations
            .SingleOrDefault(candidate => candidate.TenantId == tenantContext.TenantId);
        if (participation is null)
        {
            return Failed("webhook_owner_not_found", "Organization webhook owner was not found in the current tenant.");
        }

        return Resolved(WebhookOwnershipScope.Create(
            WebhookConsumerKind.Organization,
            participation.TenantId,
            null,
            organization.Id,
            null,
            null));
    }

    private async Task<WebhookOwnershipScopeResolution> ResolveGroupAsync(Guid? requestedOwnerId)
    {
        if (!requestedOwnerId.HasValue)
        {
            return Failed("webhook_owner_id_required", "Group webhook ownership requires a group id.");
        }

        var group = await groupRepository.GetGroupWithDetails(requestedOwnerId.Value);
        var participation = group?.TenantParticipations
            .SingleOrDefault(candidate => candidate.TenantId == tenantContext.TenantId);
        if (participation is null)
        {
            return Failed("webhook_owner_not_found", "Group webhook owner was not found in the current tenant.");
        }

        return Resolved(WebhookOwnershipScope.Create(
            WebhookConsumerKind.Group,
            participation.TenantId,
            null,
            null,
            group.Id,
            null));
    }

    private async Task<WebhookOwnershipScopeResolution> ResolveUserAsync(
        Guid? requestedOwnerId,
        CancellationToken cancellationToken)
    {
        if (currentUserService.UserId is not { } userId || userId == Guid.Empty)
        {
            return Failed(
                "webhook_user_identity_unavailable",
                "User webhook ownership requires a resolved local user identity.");
        }

        if (requestedOwnerId.HasValue && requestedOwnerId != userId)
        {
            return Failed("webhook_owner_mismatch", "Users can select only their own webhook ownership scope.");
        }

        var tenantUser = await tenantUserRepository.GetByTenantAndUserAsync(
            tenantContext.TenantId,
            userId,
            cancellationToken);
        if (tenantUser is null || tenantUser.IsDeleted || tenantUser.StatusId != (int)TenantUserStatusEnum.Active)
        {
            return Failed("webhook_owner_not_found", "An active user membership is required in the current tenant.");
        }

        return Resolved(WebhookOwnershipScope.Create(
            WebhookConsumerKind.User,
            tenantUser.TenantId,
            null,
            null,
            null,
            userId));
    }

    private static WebhookOwnershipScopeResolution Resolved(WebhookOwnershipScope scope) =>
        WebhookOwnershipScopeResolution.Resolved(scope);

    private static WebhookOwnershipScopeResolution Failed(string code, string error) =>
        WebhookOwnershipScopeResolution.Failed(code, error);
}
