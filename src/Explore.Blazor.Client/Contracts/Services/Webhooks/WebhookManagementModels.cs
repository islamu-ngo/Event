using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Webhooks;

public enum WebhookOwnerKind
{
    Tenant = 1,
    Organization = 2,
    Group = 3,
    User = 4,
    Instance = 5
}

public sealed record WebhookOwnerSelection
{
    private WebhookOwnerSelection(WebhookOwnerKind kind, Guid? ownerId)
    {
        Kind = kind;
        OwnerId = ownerId;
    }

    public WebhookOwnerKind Kind { get; }

    public int OwnerKindId => (int)Kind;

    public Guid? OwnerId { get; }

    public string DisplayName => Kind.ToString();

    public static WebhookOwnerSelection Tenant { get; } = new(WebhookOwnerKind.Tenant, null);

    public static WebhookOwnerSelection User { get; } = new(WebhookOwnerKind.User, null);

    public static WebhookOwnerSelection Instance { get; } = new(WebhookOwnerKind.Instance, null);

    public static WebhookOwnerSelection ForOrganization(Guid organizationId) =>
        new(WebhookOwnerKind.Organization, RequireOwnerId(organizationId, nameof(organizationId)));

    public static WebhookOwnerSelection ForGroup(Guid groupId) =>
        new(WebhookOwnerKind.Group, RequireOwnerId(groupId, nameof(groupId)));

    private static Guid RequireOwnerId(Guid ownerId, string parameterName) =>
        ownerId != Guid.Empty
            ? ownerId
            : throw new ArgumentException("Webhook owner id must not be empty.", parameterName);
}

public static class WebhookClientLinkRelations
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string RotateSecret = "rotate-secret";
    public const string Test = "test";
    public const string Retry = "retry";
    public const string OpenProviderPortal = "open-provider-portal";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Payload = "payload";
    public const string DeliveryAttempts = "delivery-attempts";
    public const string ProviderPublications = "provider-publications";
    public const string Reconcile = "reconcile";
    public const string Abandon = "abandon";
    public const string BulkReplayPreview = "bulk-replay-preview";
    public const string BulkReplays = "bulk-replays";
    public const string Cancel = "cancel";
}

public static class WebhookPendingWorkDecisionIds
{
    public const int PreserveExisting = 1;
    public const int MigrateEligible = 2;
}

public static class WebhookHal
{
    public static bool HasLink<TLink>(IDictionary<string, TLink>? links, string relation) =>
        links?.ContainsKey(relation) == true;
}

public sealed record WebhookManagementSnapshot
{
    private IReadOnlyList<WebhookEventTypeDto> _eventTypes = Array.Empty<WebhookEventTypeDto>();
    private IReadOnlyList<HalResourceOfWebhookConsumerDto> _consumers = Array.Empty<HalResourceOfWebhookConsumerDto>();
    private IReadOnlyList<HalResourceOfWebhookEndpointDto> _endpoints = Array.Empty<HalResourceOfWebhookEndpointDto>();
    private IReadOnlyList<HalResourceOfWebhookMessageDto> _messages = Array.Empty<HalResourceOfWebhookMessageDto>();
    private IReadOnlyList<HalResourceOfWebhookDeliveryAttemptDto> _deliveryAttempts = Array.Empty<HalResourceOfWebhookDeliveryAttemptDto>();

    public IReadOnlyList<WebhookEventTypeDto> EventTypes
    {
        get => _eventTypes;
        init => _eventTypes = Snapshot(value);
    }

    public IReadOnlyList<HalResourceOfWebhookConsumerDto> Consumers
    {
        get => _consumers;
        init => _consumers = Snapshot(value);
    }

    public IReadOnlyList<HalResourceOfWebhookEndpointDto> Endpoints
    {
        get => _endpoints;
        init => _endpoints = Snapshot(value);
    }

    public IReadOnlyList<HalResourceOfWebhookMessageDto> Messages
    {
        get => _messages;
        init => _messages = Snapshot(value);
    }

    public IReadOnlyList<HalResourceOfWebhookDeliveryAttemptDto> DeliveryAttempts
    {
        get => _deliveryAttempts;
        init => _deliveryAttempts = Snapshot(value);
    }

    public bool CanCreateConsumer { get; init; }

    public bool CanCreateEndpoint { get; init; }

    public bool CanViewProviderPublications { get; init; }

    public bool CanUseBulkReplay { get; init; }

    public bool IsSuccess { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public static WebhookManagementSnapshot Failed(string message) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = message
        };

    private static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

public sealed record WebhookActionResult(
    bool Success,
    string Message,
    Guid? Id = null)
{
    public static WebhookActionResult Succeeded(string message, Guid? id = null) =>
        new(true, message, id);

    public static WebhookActionResult Failed(string message) =>
        new(false, message);
}

public sealed record WebhookPortalResult(
    bool Success,
    string Message,
    string? Url = null);

public sealed record WebhookProviderPublicationSnapshot
{
    private IReadOnlyList<HalResourceOfWebhookProviderPublicationDto> _publications = Array.Empty<HalResourceOfWebhookProviderPublicationDto>();

    public IReadOnlyList<HalResourceOfWebhookProviderPublicationDto> Publications
    {
        get => _publications;
        init => _publications = Array.AsReadOnly(value.ToArray());
    }

    public bool IsSuccess { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public static WebhookProviderPublicationSnapshot Failed(string message) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = message
        };
}

public sealed record WebhookBulkReplaySnapshot
{
    private IReadOnlyList<HalResourceOfWebhookBulkReplayOperationDto> _operations = Array.Empty<HalResourceOfWebhookBulkReplayOperationDto>();

    public IReadOnlyList<HalResourceOfWebhookBulkReplayOperationDto> Operations
    {
        get => _operations;
        init => _operations = Array.AsReadOnly(value.ToArray());
    }

    public bool CanPreview { get; init; }

    public bool CanSchedule { get; init; }

    public bool IsSuccess { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public static WebhookBulkReplaySnapshot Failed(string message) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = message
        };
}

public sealed record WebhookPayloadResult(
    bool Success,
    string Message,
    WebhookMessagePayloadDto? Payload = null);

public sealed record WebhookBulkReplayPreviewResult(
    bool Success,
    string Message,
    WebhookBulkReplayPreviewDto? Preview = null);
