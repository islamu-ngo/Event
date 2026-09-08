using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;

namespace Explore.API.Controllers;

/// <summary>
/// Webhook collections are addressable by owner, and the owner is the tenant-scoped subject a caller is
/// actually allowed to see — not whatever owner id the query string carried. Resolving that scope in one place
/// keeps every webhook collection route, and therefore every HAL pagination link, anchored to authorized
/// ownership rather than to request input.
/// <para>
/// The resolver throwing when scope is absent is deliberate: reaching link construction without a resolved
/// scope means authorization did not run, and emitting a link in that state would leak an addressable
/// collection URL for an owner the caller may not have.
/// </para>
/// </summary>
public abstract class WebhooksControllerBase(IWebhookOwnershipScopeResolver webhookOwnershipScopeResolver)
    : EventControllerBase
{
    protected async Task<WebhookCollectionRouteValues> CreateCollectionRouteValuesAsync(
        int ownerKindId,
        Guid? ownerId,
        int limit,
        CancellationToken cancellationToken,
        Guid? consumerId = null,
        Guid? messageId = null,
        Guid? endpointId = null)
    {
        var resolution = await webhookOwnershipScopeResolver.ResolveAsync(
            ownerKindId,
            ownerId,
            cancellationToken);
        var ownership = resolution.Scope ?? throw new InvalidOperationException(
            "Webhook collection ownership was not resolved after request authorization.");

        return new WebhookCollectionRouteValues(
            ownership,
            limit,
            consumerId,
            messageId,
            endpointId);
    }

    protected sealed class WebhookCollectionRouteValues(
        WebhookOwnershipScope ownership,
        int limit,
        Guid? consumerId,
        Guid? messageId,
        Guid? endpointId) : ICollectionAuthorizationContext
    {
        private readonly IAuthorizationFacts _authorizationFacts =
            WebhookOwnershipAuthorizationFacts.From(ownership);

        public int OwnerKindId => (int)ownership.Kind;

        public Guid OwnerId => ownership.OwnerId;

        public int Limit => limit;

        public Guid? ConsumerId => consumerId;

        public Guid? MessageId => messageId;

        public Guid? EndpointId => endpointId;

        string ICollectionAuthorizationContext.AuthorizationResourceId => ownership.OwnerId.ToString();

        IAuthorizationFacts? ICollectionAuthorizationContext.AuthorizationFacts => _authorizationFacts;
    }
}
