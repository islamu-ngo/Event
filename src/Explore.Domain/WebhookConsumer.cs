using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class WebhookConsumer : IAuditableEntity
{
    private Guid _configurationScopeId;

    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid? InstanceId { get; set; }
    public InstanceBootstrapState? Instance { get; set; }

    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }
    public Guid? OwnerUserId { get; set; }
    public TenantUser? OwnerTenantUser { get; set; }
    public Guid ConfigurationScopeId
    {
        get => TenantId ?? InstanceId ?? _configurationScopeId;
        private set => _configurationScopeId = value;
    }

    public int ConsumerKindId { get; set; }
    public WebhookConsumerKindLookup ConsumerKindLookup { get; set; } = null!;
    [NotMapped]
    public WebhookConsumerKind ConsumerKind
    {
        get => (WebhookConsumerKind)ConsumerKindId;
        set => ConsumerKindId = (int)value;
    }

    [NotMapped]
    public WebhookOwnershipScope Ownership => WebhookOwnershipScope.FromConsumer(this);

    [NotMapped]
    public Guid OwnerId => Ownership.OwnerId;
    public required string Name { get; set; }
    public int StatusId { get; set; }
    public WebhookConsumerStatusLookup StatusLookup { get; set; } = null!;
    [NotMapped]
    public WebhookConsumerStatus Status
    {
        get => (WebhookConsumerStatus)StatusId;
        set => StatusId = (int)value;
    }
    public int ProviderModeId { get; set; }
    public WebhookProviderModeLookup ProviderModeLookup { get; set; } = null!;
    [NotMapped]
    public WebhookProviderMode ProviderMode
    {
        get => (WebhookProviderMode)ProviderModeId;
        set => ProviderModeId = (int)value;
    }
    public string? ExternalProviderAppId { get; set; }
    public int ConfigurationVersion { get; set; }

    public ICollection<WebhookConsumerProviderBinding> ProviderBindings { get; private set; } = [];

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static WebhookConsumer Create(
        WebhookOwnershipScope ownership,
        string name,
        WebhookProviderMode providerMode,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Webhook consumer name is required.", nameof(name));
        }

        if (!Enum.IsDefined(providerMode))
        {
            throw new ArgumentOutOfRangeException(nameof(providerMode));
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Webhook consumer creation requires a UTC timestamp.", nameof(createdAtUtc));
        }

        return new WebhookConsumer
        {
            Id = Guid.CreateVersion7(),
            TenantId = ownership.TenantId,
            InstanceId = ownership.InstanceId,
            OrganizationId = ownership.OrganizationId,
            GroupId = ownership.GroupId,
            OwnerUserId = ownership.UserId,
            ConsumerKind = ownership.Kind,
            Name = name.Trim(),
            Status = WebhookConsumerStatus.Active,
            ProviderMode = providerMode,
            ConfigurationVersion = 1,
            CreatedAt = createdAtUtc
        };
    }

    public WebhookConsumerProviderBinding? GetVerifiedProviderBinding(WebhookProviderKind providerKind)
    {
        var matchingBindings = ProviderBindings
            .Where(binding =>
                binding.ProviderKind == providerKind &&
                binding.IsVerifiedFor(TenantId, Id))
            .Take(2)
            .ToArray();

        return matchingBindings.Length == 1 ? matchingBindings[0] : null;
    }

    public void ChangeProviderMode(WebhookProviderMode providerMode, DateTime changedAtUtc)
    {
        if (!Enum.IsDefined(providerMode))
        {
            throw new ArgumentOutOfRangeException(nameof(providerMode));
        }

        if (changedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Provider-mode changes require a UTC timestamp.", nameof(changedAtUtc));
        }

        if (ProviderMode == providerMode)
        {
            throw new InvalidOperationException("The webhook consumer already uses the requested provider mode.");
        }

        if (ConfigurationVersion < 1)
        {
            throw new InvalidOperationException("The webhook consumer configuration version is invalid.");
        }

        ProviderMode = providerMode;
        ConfigurationVersion = checked(ConfigurationVersion + 1);
        UpdatedAt = changedAtUtc;
    }

}
