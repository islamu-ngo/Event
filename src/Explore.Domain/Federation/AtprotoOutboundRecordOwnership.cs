using Explore.Domain.Interfaces;

namespace Explore.Domain.Federation;

public sealed class AtprotoOutboundRecordOwnership : ITenantEntity
{
    public Guid AtprotoRecordId { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string SourceEntityType { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid SourceVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AtprotoRecord? AtprotoRecord { get; set; }
    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
    public TenantUser? TenantUser { get; set; }
}
