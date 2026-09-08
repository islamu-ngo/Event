namespace Explore.Domain;

public sealed record WebhookOwnershipScope
{
    private WebhookOwnershipScope(
        WebhookConsumerKind kind,
        Guid? tenantId,
        Guid? instanceId,
        Guid? organizationId,
        Guid? groupId,
        Guid? userId)
    {
        Kind = kind;
        TenantId = tenantId;
        InstanceId = instanceId;
        OrganizationId = organizationId;
        GroupId = groupId;
        UserId = userId;
    }

    public WebhookConsumerKind Kind { get; }
    public Guid? TenantId { get; }
    public Guid? InstanceId { get; }
    public Guid? OrganizationId { get; }
    public Guid? GroupId { get; }
    public Guid? UserId { get; }

    public Guid OwnerId => Kind switch
    {
        WebhookConsumerKind.Instance => InstanceId!.Value,
        WebhookConsumerKind.Tenant => TenantId!.Value,
        WebhookConsumerKind.Organization => OrganizationId!.Value,
        WebhookConsumerKind.Group => GroupId!.Value,
        WebhookConsumerKind.User => UserId!.Value,
        _ => throw new InvalidOperationException("Webhook ownership kind is invalid.")
    };

    public WebhookAuditScopeKind AuditScopeKind => Kind switch
    {
        WebhookConsumerKind.Instance => WebhookAuditScopeKind.Instance,
        WebhookConsumerKind.Tenant => WebhookAuditScopeKind.Tenant,
        WebhookConsumerKind.Organization => WebhookAuditScopeKind.Organization,
        WebhookConsumerKind.Group => WebhookAuditScopeKind.Group,
        WebhookConsumerKind.User => WebhookAuditScopeKind.User,
        _ => throw new InvalidOperationException("Webhook ownership kind is invalid.")
    };

    public static WebhookOwnershipScope Create(
        WebhookConsumerKind kind,
        Guid? tenantId,
        Guid? instanceId,
        Guid? organizationId,
        Guid? groupId,
        Guid? userId)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var isValid = kind switch
        {
            WebhookConsumerKind.Instance => IsSet(instanceId) && tenantId is null && organizationId is null && groupId is null && userId is null,
            WebhookConsumerKind.Tenant => IsSet(tenantId) && instanceId is null && organizationId is null && groupId is null && userId is null,
            WebhookConsumerKind.Organization => IsSet(tenantId) && instanceId is null && IsSet(organizationId) && groupId is null && userId is null,
            WebhookConsumerKind.Group => IsSet(tenantId) && instanceId is null && organizationId is null && IsSet(groupId) && userId is null,
            WebhookConsumerKind.User => IsSet(tenantId) && instanceId is null && organizationId is null && groupId is null && IsSet(userId),
            _ => false
        };

        return isValid
            ? new WebhookOwnershipScope(kind, tenantId, instanceId, organizationId, groupId, userId)
            : throw new ArgumentException("Webhook ownership references do not match the owner kind.");
    }

    public static WebhookOwnershipScope FromConsumer(WebhookConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        return Create(
            consumer.ConsumerKind,
            consumer.TenantId,
            consumer.InstanceId,
            consumer.OrganizationId,
            consumer.GroupId,
            consumer.OwnerUserId);
    }

    private static bool IsSet(Guid? value) => value is { } id && id != Guid.Empty;
}
