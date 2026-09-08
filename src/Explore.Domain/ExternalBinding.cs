using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class ExternalBinding : IAuditableEntity
{
    public Guid Id { get; set; }

    public required string ProviderKey { get; set; }
    public required string ExternalSystem { get; set; }
    public required string ExternalType { get; set; }
    public required string ExternalId { get; set; }

    public required string InternalType { get; set; }
    public Guid InternalId { get; set; }

    public Guid? ScopeTenantId { get; set; }
    public Tenant? ScopeTenant { get; set; }

    public int ExternalBindingStatusId { get; set; } = (int)ExternalBindingStatusEnum.Active;

    public string? MetadataJson { get; set; }
    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
