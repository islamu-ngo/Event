using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class AdmissionTicketCredential : ITenantEntity
{
    private Guid _tenantId;

    private AdmissionTicketCredential()
    {
    }

    internal AdmissionTicketCredential(
        Guid id,
        Guid tenantId,
        Guid admissionTicketId,
        int credentialVersion,
        int lookupKeyVersion,
        string lookupDigest,
        DateTime createdAt)
    {
        Id = id;
        TenantId = tenantId;
        AdmissionTicketId = admissionTicketId;
        CredentialVersion = credentialVersion;
        LookupKeyVersion = lookupKeyVersion;
        LookupDigest = lookupDigest;
        AdmissionTicketCredentialStatusId = (int)AdmissionTicketCredentialStatusEnum.Active;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionTicketCredential));
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(AdmissionTicketCredential));
    }
    public Guid AdmissionTicketId { get; private set; }
    public int CredentialVersion { get; private set; }
    public int LookupKeyVersion { get; private set; }
    public string LookupDigest { get; private set; } = string.Empty;
    public int AdmissionTicketCredentialStatusId { get; private set; }
    public AdmissionTicketCredentialStatus? AdmissionTicketCredentialStatus { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    internal void Revoke(DateTime revokedAtUtc)
    {
        if (AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Revoked)
        {
            return;
        }

        AdmissionTicketCredentialStatusId = (int)AdmissionTicketCredentialStatusEnum.Revoked;
        RevokedAt = revokedAtUtc;
    }

    public override string ToString() => $"AdmissionTicketCredential({Id}, v{CredentialVersion}, <redacted>)";
}
