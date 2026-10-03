using System.Security.Cryptography;
using System.Text;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum EventDiscoverySourceKind
{
    LocalEvent = 1,
    AtprotoRecord = 2
}

/// <summary>A discovery-only source binding; it never replaces the source's resource identifiers.</summary>
public sealed class EventDiscoveryIdentity : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public EventDiscoverySourceKind SourceKind { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public string SourceKeyHash { get; private set; } = string.Empty;
    public EventDiscoveryAlias? Alias { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static EventDiscoveryIdentity Create(
        Guid tenantId, EventDiscoverySourceKind sourceKind, string sourceKey)
    {
        if (tenantId == Guid.Empty || !Enum.IsDefined(sourceKind))
            throw new ArgumentException("A tenant and supported source kind are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        if (sourceKey.Length > 1024 || sourceKey != sourceKey.Trim())
            throw new ArgumentException("The authoritative source key must be bounded and exact.", nameof(sourceKey));
        return new EventDiscoveryIdentity
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SourceKind = sourceKind,
            SourceKey = sourceKey,
            SourceKeyHash = HashSourceKey(sourceKey)
        };
    }

    public static string HashSourceKey(string sourceKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey)));
}
