using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class WebhookConsumerProviderBinding : IAuditableEntity
{
    private const int MaxIdentityLength = 500;
    private Guid _configurationScopeId;

    public Guid Id { get; private set; }
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid ConfigurationScopeId
    {
        get => TenantId ?? (InstanceId != Guid.Empty ? InstanceId : _configurationScopeId);
        private set => _configurationScopeId = value;
    }
    public Guid WebhookConsumerId { get; private set; }
    public WebhookConsumer WebhookConsumer { get; private set; } = null!;
    public Guid InstanceId { get; private set; }
    public int ProviderKindId { get; private set; }
    public WebhookProviderKindLookup ProviderKindLookup { get; private set; } = null!;
    [NotMapped]
    public WebhookProviderKind ProviderKind
    {
        get => (WebhookProviderKind)ProviderKindId;
        private set => ProviderKindId = (int)value;
    }
    public string ProviderVersion { get; private set; } = string.Empty;
    public string ProviderEnvironment { get; private set; } = string.Empty;
    public string NormalizedEnvironment { get; private set; } = string.Empty;
    public string ApplicationUid { get; private set; } = string.Empty;
    public string NormalizedApplicationUid { get; private set; } = string.Empty;
    public string? ExternalApplicationId { get; private set; }
    public string? NormalizedExternalApplicationId { get; private set; }
    public int VerificationStateId { get; private set; }
    public WebhookProviderBindingVerificationStateLookup VerificationStateLookup { get; private set; } = null!;
    [NotMapped]
    public WebhookProviderBindingVerificationState VerificationState
    {
        get => (WebhookProviderBindingVerificationState)VerificationStateId;
        private set => VerificationStateId = (int)value;
    }
    public Guid? VerifiedTenantId { get; private set; }
    public Guid? VerifiedWebhookConsumerId { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }
    public WebhookProviderCapability Capabilities { get; private set; }
    public WebhookProviderCapability GovernanceAllowedCapabilities { get; private set; }
    public string CapabilityResolutionVersion { get; private set; } = string.Empty;
    public DateTimeOffset CapabilitiesResolvedAtUtc { get; private set; }
    public bool IsEnabled { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public long VerificationFence { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public WebhookProviderCapability EffectiveGovernedCapabilities =>
        Capabilities & GovernanceAllowedCapabilities;

    public bool IsVerifiedFor(Guid? tenantId, Guid webhookConsumerId) =>
        IsEnabled &&
        VerificationState == WebhookProviderBindingVerificationState.Verified &&
        TenantId == tenantId &&
        WebhookConsumerId == webhookConsumerId &&
        VerifiedTenantId == tenantId &&
        VerifiedWebhookConsumerId == webhookConsumerId &&
        !string.IsNullOrWhiteSpace(ExternalApplicationId) &&
        string.Equals(
            ApplicationUid,
            CreateApplicationUid(InstanceId, WebhookConsumerId),
            StringComparison.Ordinal);

    public bool CanIssueAppPortalFor(Guid? tenantId, Guid webhookConsumerId) =>
        IsVerifiedFor(tenantId, webhookConsumerId) &&
        SupportsGoverned(WebhookProviderCapability.AppPortal);

    public bool SupportsGoverned(WebhookProviderCapability capability)
    {
        WebhookProviderCapabilityProfile.EnsureKnownCapabilities(capability, nameof(capability));
        return capability != WebhookProviderCapability.None &&
               (EffectiveGovernedCapabilities & capability) == capability;
    }

    public static WebhookConsumerProviderBinding CreatePending(
        Guid? tenantId,
        Guid webhookConsumerId,
        Guid instanceId,
        string providerEnvironment,
        WebhookProviderCapabilityProfile capabilityProfile,
        WebhookProviderCapability governanceAllowedCapabilities)
    {
        return Create(
            tenantId,
            webhookConsumerId,
            instanceId,
            providerEnvironment,
            capabilityProfile,
            governanceAllowedCapabilities,
            WebhookProviderBindingVerificationState.Pending,
            externalApplicationId: null);
    }

    public static WebhookConsumerProviderBinding CreateLegacyUnverified(
        Guid? tenantId,
        Guid webhookConsumerId,
        Guid instanceId,
        string providerEnvironment,
        string externalApplicationId,
        WebhookProviderCapabilityProfile capabilityProfile)
    {
        return Create(
            tenantId,
            webhookConsumerId,
            instanceId,
            providerEnvironment,
            capabilityProfile,
            WebhookProviderCapability.None,
            WebhookProviderBindingVerificationState.LegacyUnverified,
            WebhookProviderCapabilityProfile.NormalizeRequired(
                externalApplicationId,
                nameof(externalApplicationId)));
    }

    public static WebhookConsumerProviderBinding CreateLegacyUnresolved(
        Guid? tenantId,
        Guid webhookConsumerId,
        Guid instanceId,
        string providerEnvironment,
        WebhookProviderCapabilityProfile capabilityProfile)
    {
        return Create(
            tenantId,
            webhookConsumerId,
            instanceId,
            providerEnvironment,
            capabilityProfile,
            WebhookProviderCapability.None,
            WebhookProviderBindingVerificationState.LegacyUnverified,
            externalApplicationId: null);
    }

    public void VerifyOwnership(
        Guid? verifiedTenantId,
        Guid verifiedWebhookConsumerId,
        string externalApplicationId,
        DateTimeOffset verifiedAtUtc)
    {
        if (VerificationState is not WebhookProviderBindingVerificationState.Pending and
            not WebhookProviderBindingVerificationState.LegacyUnverified)
        {
            throw new InvalidOperationException(
                "Only pending or legacy-unverified provider bindings can be verified.");
        }

        if (verifiedTenantId != TenantId || verifiedWebhookConsumerId != WebhookConsumerId)
        {
            throw new InvalidOperationException(
                "Provider application ownership does not match the persisted tenant and webhook consumer.");
        }

        ExternalApplicationId = WebhookProviderCapabilityProfile.NormalizeRequired(
            externalApplicationId,
            nameof(externalApplicationId));
        NormalizedExternalApplicationId = NormalizeIdentity(ExternalApplicationId, nameof(externalApplicationId));
        VerifiedTenantId = verifiedTenantId;
        VerifiedWebhookConsumerId = verifiedWebhookConsumerId;
        VerifiedAtUtc = verifiedAtUtc;
        VerificationState = WebhookProviderBindingVerificationState.Verified;
        IsEnabled = true;
        AdvanceVerificationGuard();
    }

    public void RepairAndVerifyOwnership(
        Guid instanceId,
        Guid? verifiedTenantId,
        Guid verifiedWebhookConsumerId,
        string externalApplicationId,
        WebhookProviderCapabilityProfile capabilityProfile,
        WebhookProviderCapability governanceAllowedCapabilities,
        DateTimeOffset verifiedAtUtc)
    {
        EnsureRequired(instanceId, nameof(instanceId));
        ArgumentNullException.ThrowIfNull(capabilityProfile);

        if (VerificationState is WebhookProviderBindingVerificationState.Rejected or
            WebhookProviderBindingVerificationState.Revoked)
        {
            throw new InvalidOperationException(
                "Rejected or revoked provider bindings cannot be repaired.");
        }

        if (verifiedTenantId != TenantId || verifiedWebhookConsumerId != WebhookConsumerId)
        {
            throw new InvalidOperationException(
                "Provider application ownership does not match the persisted tenant and webhook consumer.");
        }

        if (capabilityProfile.ProviderKind != ProviderKind)
        {
            throw new InvalidOperationException(
                "Provider binding repair cannot change the provider kind.");
        }

        WebhookProviderCapabilityProfile.EnsureKnownCapabilities(
            governanceAllowedCapabilities,
            nameof(governanceAllowedCapabilities));

        InstanceId = instanceId;
        ApplicationUid = CreateApplicationUid(instanceId, WebhookConsumerId);
        NormalizedApplicationUid = NormalizeIdentity(ApplicationUid, nameof(ApplicationUid));
        ProviderVersion = capabilityProfile.ProviderVersion;
        Capabilities = capabilityProfile.Capabilities;
        GovernanceAllowedCapabilities = governanceAllowedCapabilities;
        CapabilityResolutionVersion = capabilityProfile.ResolutionVersion;
        CapabilitiesResolvedAtUtc = capabilityProfile.ResolvedAtUtc;
        ExternalApplicationId = WebhookProviderCapabilityProfile.NormalizeRequired(
            externalApplicationId,
            nameof(externalApplicationId));
        NormalizedExternalApplicationId = NormalizeIdentity(
            ExternalApplicationId,
            nameof(externalApplicationId));
        VerifiedTenantId = verifiedTenantId;
        VerifiedWebhookConsumerId = verifiedWebhookConsumerId;
        VerifiedAtUtc = verifiedAtUtc;
        VerificationState = WebhookProviderBindingVerificationState.Verified;
        IsEnabled = true;
        AdvanceVerificationGuard();
    }

    public void RejectVerification()
    {
        if (VerificationState is not WebhookProviderBindingVerificationState.Pending and
            not WebhookProviderBindingVerificationState.LegacyUnverified)
        {
            throw new InvalidOperationException(
                "Only pending or legacy-unverified provider bindings can be rejected.");
        }

        VerificationState = WebhookProviderBindingVerificationState.Rejected;
        IsEnabled = false;
        AdvanceVerificationGuard();
    }

    public void Revoke()
    {
        if (VerificationState != WebhookProviderBindingVerificationState.Verified)
        {
            throw new InvalidOperationException("Only a verified provider binding can be revoked.");
        }

        VerificationState = WebhookProviderBindingVerificationState.Revoked;
        IsEnabled = false;
        AdvanceVerificationGuard();
    }

    public void ReplaceCapabilityResolution(
        WebhookProviderCapabilityProfile capabilityProfile,
        WebhookProviderCapability governanceAllowedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilityProfile);

        if (capabilityProfile.ProviderKind != ProviderKind ||
            !string.Equals(capabilityProfile.ProviderVersion, ProviderVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Capability resolution must target the binding's immutable provider kind and version.");
        }

        WebhookProviderCapabilityProfile.EnsureKnownCapabilities(
            governanceAllowedCapabilities,
            nameof(governanceAllowedCapabilities));

        Capabilities = capabilityProfile.Capabilities;
        GovernanceAllowedCapabilities = governanceAllowedCapabilities;
        CapabilityResolutionVersion = capabilityProfile.ResolutionVersion;
        CapabilitiesResolvedAtUtc = capabilityProfile.ResolvedAtUtc;
    }

    public void Disable()
    {
        IsEnabled = false;
        AdvanceVerificationGuard();
    }

    public void Enable()
    {
        if (VerificationState != WebhookProviderBindingVerificationState.Verified)
        {
            throw new InvalidOperationException("Only a verified provider binding can be enabled.");
        }

        IsEnabled = true;
        AdvanceVerificationGuard();
    }

    public static string CreateApplicationUid(Guid instanceId, Guid webhookConsumerId)
    {
        EnsureRequired(instanceId, nameof(instanceId));
        EnsureRequired(webhookConsumerId, nameof(webhookConsumerId));
        return $"islamu-{instanceId:N}-consumer-{webhookConsumerId:N}";
    }

    public static string NormalizeIdentity(string value, string parameterName)
    {
        var normalized = WebhookProviderCapabilityProfile.NormalizeRequired(value, parameterName)
            .Normalize(NormalizationForm.FormKC);
        if (normalized.Length > MaxIdentityLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {MaxIdentityLength} characters.");
        }

        return normalized.ToUpperInvariant();
    }

    private static WebhookConsumerProviderBinding Create(
        Guid? tenantId,
        Guid webhookConsumerId,
        Guid instanceId,
        string providerEnvironment,
        WebhookProviderCapabilityProfile capabilityProfile,
        WebhookProviderCapability governanceAllowedCapabilities,
        WebhookProviderBindingVerificationState verificationState,
        string? externalApplicationId)
    {
        EnsureRequired(webhookConsumerId, nameof(webhookConsumerId));
        EnsureRequired(instanceId, nameof(instanceId));
        ArgumentNullException.ThrowIfNull(capabilityProfile);
        WebhookProviderCapabilityProfile.EnsureKnownCapabilities(
            governanceAllowedCapabilities,
            nameof(governanceAllowedCapabilities));

        var normalizedEnvironment = WebhookProviderCapabilityProfile.NormalizeRequired(
            providerEnvironment,
            nameof(providerEnvironment));
        var applicationUid = CreateApplicationUid(instanceId, webhookConsumerId);

        return new WebhookConsumerProviderBinding
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            WebhookConsumerId = webhookConsumerId,
            InstanceId = instanceId,
            ProviderKind = capabilityProfile.ProviderKind,
            ProviderVersion = capabilityProfile.ProviderVersion,
            ProviderEnvironment = normalizedEnvironment,
            NormalizedEnvironment = NormalizeIdentity(normalizedEnvironment, nameof(providerEnvironment)),
            ApplicationUid = applicationUid,
            NormalizedApplicationUid = NormalizeIdentity(applicationUid, nameof(applicationUid)),
            ExternalApplicationId = externalApplicationId,
            NormalizedExternalApplicationId = externalApplicationId is null
                ? null
                : NormalizeIdentity(externalApplicationId, nameof(externalApplicationId)),
            VerificationState = verificationState,
            Capabilities = capabilityProfile.Capabilities,
            GovernanceAllowedCapabilities = governanceAllowedCapabilities,
            CapabilityResolutionVersion = capabilityProfile.ResolutionVersion,
            CapabilitiesResolvedAtUtc = capabilityProfile.ResolvedAtUtc,
            IsEnabled = false,
            ConcurrencyVersion = 1,
            VerificationFence = 1
        };
    }

    private void AdvanceVerificationGuard()
    {
        checked
        {
            ConcurrencyVersion++;
            VerificationFence++;
        }
    }

    private static void EnsureRequired(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }
}
