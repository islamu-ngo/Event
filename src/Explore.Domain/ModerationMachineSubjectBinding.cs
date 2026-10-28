using Explore.Domain.Enums;

namespace Explore.Domain;

/// <summary>
/// Host-established association of exact authenticated credential coordinates with an existing
/// canonical service principal. The host verifies existence and ownership; this row confers no permission.
/// Replacement credentials may select the same principal without changing historical bindings.
/// </summary>
public sealed class ModerationMachineSubjectBinding
{
    private ModerationMachineSubjectBinding()
    {
    }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public ModerationMachineCredentialSource CredentialSource { get; private set; }
    public string KeyId { get; private set; } = null!;
    public Guid? AuthorityTenantId { get; private set; }
    public ExternalApiKeyOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid ServicePrincipalId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public long Revision { get; private set; }

    public static ModerationMachineSubjectBinding Establish(
        Guid id,
        Guid hostBindingId,
        ModerationMachineCredentialSource credentialSource,
        string keyId,
        Guid? authorityTenantId,
        ExternalApiKeyOwnerType ownerType,
        Guid ownerId,
        Guid servicePrincipalId)
    {
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        ModerationHostBinding.RequireUuidV7(hostBindingId, nameof(hostBindingId));
        if (!Enum.IsDefined(credentialSource))
        {
            throw new ArgumentException("Credential source must be a known native source.", nameof(credentialSource));
        }
        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 64)
        {
            throw new ArgumentException("Key identifier must contain between 1 and 64 characters.", nameof(keyId));
        }
        if (authorityTenantId is { } tenantId)
        {
            ModerationHostBinding.RequireUuidV7(tenantId, nameof(authorityTenantId));
        }
        if (!Enum.IsDefined(ownerType))
        {
            throw new ArgumentException("Credential owner type must be a known native owner.", nameof(ownerType));
        }
        ModerationHostBinding.RequireUuidV7(ownerId, nameof(ownerId));
        ModerationHostBinding.RequireUuidV7(servicePrincipalId, nameof(servicePrincipalId));

        return new ModerationMachineSubjectBinding
        {
            Id = id,
            HostBindingId = hostBindingId,
            CredentialSource = credentialSource,
            KeyId = keyId,
            AuthorityTenantId = authorityTenantId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            ServicePrincipalId = servicePrincipalId,
            RevokedAt = null,
            Revision = 1
        };
    }

    /// <summary>
    /// Verifies an exact repeated assertion without rewriting coordinates or restoring a revoked binding.
    /// Key identifiers are opaque ordinal strings and are never normalized.
    /// </summary>
    public void RequireMatchingCoordinates(
        Guid hostBindingId,
        ModerationMachineCredentialSource credentialSource,
        string keyId,
        Guid? authorityTenantId,
        ExternalApiKeyOwnerType ownerType,
        Guid ownerId,
        Guid servicePrincipalId)
    {
        if (HostBindingId != hostBindingId
            || CredentialSource != credentialSource
            || !string.Equals(KeyId, keyId, StringComparison.Ordinal)
            || AuthorityTenantId != authorityTenantId
            || OwnerType != ownerType
            || OwnerId != ownerId
            || ServicePrincipalId != servicePrincipalId)
        {
            throw new InvalidOperationException("Host-established machine subject binding coordinates are immutable.");
        }
    }

    /// <summary>
    /// Permanently revokes this credential association at the exact positive expected revision.
    /// Persistence must fence concurrent writes; revocation does not remove sanctions on the principal.
    /// </summary>
    public void Revoke(long expectedRevision, DateTimeOffset revokedAt)
    {
        if (RevokedAt is not null || Revision <= 0 || expectedRevision != Revision)
        {
            throw new InvalidOperationException("Revocation requires an active binding and its exact positive revision.");
        }

        long nextRevision = checked(Revision + 1);
        RevokedAt = revokedAt;
        Revision = nextRevision;
    }
}
