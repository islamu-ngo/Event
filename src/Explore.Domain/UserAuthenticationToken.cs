using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class UserAuthenticationToken : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public Guid Id { get; set; }

    [ForeignKey("User")]
    public Guid UserId { get; set; }
    public required User User { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public required string Provider { get; set; }
    public required string SubjectDid { get; set; }
    public required byte[] SessionCiphertext { get; set; }
    public required string EncryptionKeyId { get; set; }
    public required string OAuthClientKeyId { get; set; }
    public int EnvelopeVersion { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public string? PdsHost { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
