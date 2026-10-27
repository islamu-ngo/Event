using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>Immutable publication evidence for one binding, born with its final mapping hash.</summary>
public sealed class RegistrationProviderBindingPublication : ITenantEntity
{
    private RegistrationProviderBindingPublication() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RegistrationProviderBindingId { get; private set; }
    public string MappingRevisionHashKey { get; private set; } = string.Empty;
    public DateTime PublishedAt { get; private set; }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId) throw new InvalidOperationException("Publication tenant is immutable.");
        }
    }

    internal static RegistrationProviderBindingPublication Create(
        Guid tenantId, Guid bindingId, RegistrationEvidenceHash hash, DateTime publishedAt) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RegistrationProviderBindingId = bindingId,
            MappingRevisionHashKey = hash.Value,
            PublishedAt = RegistrationProviderConnection.EnsureUtc(publishedAt, nameof(publishedAt))
        };
}
