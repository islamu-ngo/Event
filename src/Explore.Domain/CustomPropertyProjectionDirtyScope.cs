using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class CustomPropertyProjectionDirtyScope : ITenantEntity
{
    public long Id { get; set; }

    public required string ProjectionName { get; set; }
    public int ProjectionVersion { get; set; }

    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public CustomPropertyProjectionScopeType ScopeType { get; set; }
    public Guid ScopeId { get; set; }
    public Guid? DefinitionId { get; set; }
    public required string Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DrainedAt { get; set; }
}
