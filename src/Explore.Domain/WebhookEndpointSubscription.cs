using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class WebhookEndpointSubscription : IAuditableEntity
{
    private Guid _configurationScopeId;

    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid? InstanceId { get; set; }
    public InstanceBootstrapState? Instance { get; set; }
    public Guid ConfigurationScopeId
    {
        get => TenantId ?? InstanceId ?? _configurationScopeId;
        private set => _configurationScopeId = value;
    }

    public Guid EndpointId { get; set; }
    public WebhookEndpoint? Endpoint { get; set; }
    public Guid EventTypeId { get; set; }
    public WebhookEventType? EventType { get; set; }
    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
