using Explore.Domain.Interfaces;

namespace Explore.Domain.Federation;

public sealed class AtprotoRecordTenantPresentation : ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AtprotoRecordId { get; set; }
    public bool IsVisible { get; set; }
    public long SourceVersion { get; set; }
    public DateTime EvaluatedAt { get; set; }

    public Tenant? Tenant { get; set; }
    public AtprotoRecord? AtprotoRecord { get; set; }
}
