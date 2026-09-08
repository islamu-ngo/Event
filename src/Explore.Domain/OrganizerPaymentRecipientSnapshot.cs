namespace Explore.Domain;

public sealed class OrganizerPaymentRecipientSnapshot
{
    private OrganizerPaymentRecipientSnapshot(
        Guid tenantId,
        Guid organizerActorId,
        Guid organizerPaymentProviderConnectionId,
        string providerCode,
        string connectPlatformId,
        string externalAccountId,
        string merchantCountryCode,
        string currencyCode,
        Guid instancePolicyVersionId,
        Guid? tenantPolicyVersionId,
        DateTime snapshottedAt)
    {
        TenantId = tenantId;
        OrganizerActorId = organizerActorId;
        OrganizerPaymentProviderConnectionId = organizerPaymentProviderConnectionId;
        ProviderCode = providerCode;
        ConnectPlatformId = connectPlatformId;
        ExternalAccountId = externalAccountId;
        MerchantCountryCode = merchantCountryCode;
        CurrencyCode = currencyCode;
        ProfileCode = "OrganizerDirect";
        InstancePolicyVersionId = instancePolicyVersionId;
        TenantPolicyVersionId = tenantPolicyVersionId;
        SnapshottedAt = snapshottedAt;
    }

    public Guid TenantId { get; }
    public Guid OrganizerActorId { get; }
    public Guid OrganizerPaymentProviderConnectionId { get; }
    public string ProviderCode { get; }
    public string ConnectPlatformId { get; }
    public string ExternalAccountId { get; }
    public string MerchantCountryCode { get; }
    public string CurrencyCode { get; }
    public string ProfileCode { get; }
    public Guid InstancePolicyVersionId { get; }
    public Guid? TenantPolicyVersionId { get; }
    public DateTime SnapshottedAt { get; }

    public static OrganizerPaymentRecipientSnapshot Create(
        Guid tenantId,
        Guid organizerActorId,
        Guid organizerPaymentProviderConnectionId,
        string providerCode,
        string connectPlatformId,
        string externalAccountId,
        string merchantCountryCode,
        string currencyCode,
        Guid instancePolicyVersionId,
        Guid? tenantPolicyVersionId,
        DateTime snapshottedAt)
    {
        if (tenantId == Guid.Empty || organizerActorId == Guid.Empty || organizerPaymentProviderConnectionId == Guid.Empty || instancePolicyVersionId == Guid.Empty || tenantPolicyVersionId == Guid.Empty)
        {
            throw new ArgumentException("Recipient snapshot identities are required.");
        }

        return new OrganizerPaymentRecipientSnapshot(
            tenantId,
            organizerActorId,
            organizerPaymentProviderConnectionId,
            OrganizerPaymentProviderConnection.NormalizeProviderCode(providerCode),
            OrganizerPaymentProviderConnection.NormalizeProviderIdentity(connectPlatformId, nameof(connectPlatformId), 120, preserveCase: false),
            OrganizerPaymentProviderConnection.NormalizeProviderIdentity(externalAccountId, nameof(externalAccountId), 200, preserveCase: true),
            OrganizerPaymentProviderConnection.NormalizeCountryCode(merchantCountryCode),
            OrganizerPaymentProviderConnection.NormalizeCurrencyCode(currencyCode),
            instancePolicyVersionId,
            tenantPolicyVersionId,
            OrganizerPaymentProviderConnection.EnsureUtc(snapshottedAt, nameof(snapshottedAt)));
    }
}
