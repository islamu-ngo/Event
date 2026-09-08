using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class CustomPropertyProjectionStatus : ITenantEntity, IConcurrencyAware
{
    public required string ProjectionName { get; set; }
    public int ProjectionVersion { get; set; }

    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public CustomPropertyProjectionState State { get; set; }
    public DateTimeOffset? LastRebuildStartedAt { get; set; }
    public DateTimeOffset? LastRebuildCompletedAt { get; set; }
    public long RowsProcessed { get; set; }
    public long RowsFailed { get; set; }
    public string? LastCheckpoint { get; set; }
    public string? LastErrorMessage { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}
