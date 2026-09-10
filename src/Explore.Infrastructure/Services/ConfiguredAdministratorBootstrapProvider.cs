using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Models;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Services;

public sealed class ConfiguredAdministratorBootstrapProvider(
    IConfiguration configuration,
    IInstanceOperatorIdentity instanceOperatorIdentity,
    IInstanceBootstrapStateRepository bootstrapRepository)
    : IConfiguredAdministratorBootstrapProvider
{
    private const int MaximumSubjectLength = 2048;
    private const int MaximumIssuerLength = 2048;
    private const int MaximumEmailLength = 320;
    private const int MaximumProfileNameLength = 128;

    public async Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
        ProviderAccountKey authenticatedAccount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authenticatedAccount);
        ConfigurationSnapshot snapshot;
        try
        {
            snapshot = ReadConfiguration();
        }
        catch (ConfiguredAdministratorBootstrapException)
        {
            return null;
        }

        if (snapshot.Mode == InstanceBootstrapMode.Interactive
            || snapshot.AccountKey != authenticatedAccount)
        {
            return null;
        }

        var current = await bootstrapRepository.GetCurrent(cancellationToken);
        if (current is null
            || current.Mode != InstanceBootstrapMode.ConfiguredAdministrator
            || current.Status is not (InstanceBootstrapStatus.Pending or InstanceBootstrapStatus.Completed)
            || current.ProviderKind != snapshot.ProviderKind
            || current.DeploymentMode != snapshot.DeploymentMode
            || current.Generation != snapshot.Generation
            || !string.Equals(current.ConfigurationFingerprint, snapshot.ConfigurationFingerprint, StringComparison.Ordinal)
            || !string.Equals(current.SelectorFingerprint, snapshot.SelectorFingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        return new ConfiguredAdministratorBootstrapBinding(
            snapshot.AccountKey!,
            snapshot.Generation,
            snapshot.SelectorFingerprint!,
            snapshot.Settings!,
            snapshot.AdministratorProfile!);
    }

    internal ConfigurationSnapshot ReadConfiguration()
    {
        string? mode = configuration["INSTANCE_BOOTSTRAP_MODE"];
        if (mode is null or "")
        {
            throw Failure("instance_bootstrap_mode_missing");
        }

        if (mode == "Interactive")
        {
            if (ConfiguredValues().Any(HasValue))
            {
                throw Failure("instance_bootstrap_interactive_matrix_invalid");
            }

            return ConfigurationSnapshot.Interactive(ResolveDeploymentMode());
        }

        if (mode != "ConfiguredAdministrator")
        {
            throw Failure("instance_bootstrap_mode_invalid");
        }

        string providerText = Required("INSTANCE_BOOTSTRAP_ADMIN_PROVIDER");
        string subject = Required("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT");
        string generationText = Required("INSTANCE_BOOTSTRAP_BINDING_GENERATION");
        string? configuredEmail = Optional("INSTANCE_BOOTSTRAP_ADMIN_EMAIL");
        string? email = configuredEmail is null ? null : NormalizeEmail(configuredEmail);
        if (providerText == "local" && email?.Length > 254)
            throw Failure("instance_bootstrap_email_invalid");
        string? firstName = NormalizeProfileName(Optional("INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"));
        string? lastName = NormalizeProfileName(Optional("INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"));
        if ((firstName is null) != (lastName is null))
        {
            throw Failure("instance_bootstrap_profile_matrix_invalid");
        }

        AuthenticationProviderKind providerKind = providerText switch
        {
            "local" => AuthenticationProviderKind.Local,
            "keycloak" => AuthenticationProviderKind.Keycloak,
            "atproto" => AuthenticationProviderKind.Atproto,
            _ => throw Failure("instance_bootstrap_provider_invalid")
        };
        if (!long.TryParse(generationText, NumberStyles.None, CultureInfo.InvariantCulture, out long generation)
            || generation <= 0)
        {
            throw Failure("instance_bootstrap_generation_invalid");
        }

        ValidateSubject(subject);
        ProviderAccountKey accountKey = BuildAccountKey(providerKind, subject);
        DeploymentMode deploymentMode = ResolveDeploymentMode();
        CompleteInstanceOnboardingRequest settings = BuildSettings(deploymentMode);
        if (!new CompleteInstanceOnboardingRequestValidator().Validate(settings).IsValid)
        {
            throw Failure("instance_bootstrap_onboarding_settings_invalid");
        }

        string selectorFingerprint = Fingerprint(
            "configured-administrator-selector-v1",
            "provider", providerText,
            "account-key", accountKey.Value);
        string configurationFingerprint = Fingerprint(
            "configured-administrator-configuration-v1",
            "mode", mode,
            "provider", providerText,
            "account-key", accountKey.Value,
            "generation", generation.ToString(CultureInfo.InvariantCulture),
            "administrator-email", email ?? string.Empty,
            "administrator-first-name", firstName ?? string.Empty,
            "administrator-last-name", lastName ?? string.Empty,
            "deployment-mode", deploymentMode.ToString(),
            "site-name", settings.SiteProfile.SiteName,
            "support-email", settings.SiteProfile.SupportEmail ?? string.Empty,
            "canonical-url", settings.SiteProfile.CanonicalUrl ?? string.Empty,
            "locale", settings.SiteProfile.Locale,
            "time-zone", settings.SiteProfile.TimeZone,
            "purpose", settings.SiteProfile.Purpose ?? string.Empty,
            "administration-access-mode", settings.AdministrationAccessMode,
            "admin-host", settings.AdminHost ?? string.Empty,
            "instance-name", settings.InstanceName ?? string.Empty,
            "directory-public-name", settings.DirectoryOperatorIdentity?.PublicName ?? string.Empty,
            "directory-legal-name", settings.DirectoryOperatorIdentity?.LegalName ?? string.Empty,
            "directory-operator-kind", settings.DirectoryOperatorIdentity?.OperatorKindCode ?? string.Empty,
            "directory-jurisdiction", settings.DirectoryOperatorIdentity?.JurisdictionCountryCode ?? string.Empty,
            "directory-registration-id", settings.DirectoryOperatorIdentity?.RegistrationIdentifier ?? string.Empty,
            "directory-contact-email", settings.DirectoryOperatorIdentity?.PublicContactEmail ?? string.Empty,
            "directory-legal-notice-url", settings.DirectoryOperatorIdentity?.LegalNoticeUrl ?? string.Empty,
            "directory-terms-url", settings.DirectoryOperatorIdentity?.TermsUrl ?? string.Empty,
            "directory-privacy-url", settings.DirectoryOperatorIdentity?.PrivacyUrl ?? string.Empty);

        return ConfigurationSnapshot.Configured(
            providerKind,
            deploymentMode,
            generation,
            configurationFingerprint,
            selectorFingerprint,
            accountKey,
            settings,
            new ConfiguredAdministratorProfile(email, firstName, lastName));
    }

    private CompleteInstanceOnboardingRequest BuildSettings(DeploymentMode deploymentMode)
    {
        TenantDirectoryOperatorIdentityInputDto? directoryIdentity = deploymentMode == DeploymentMode.SingleTenant
            ? new TenantDirectoryOperatorIdentityInputDto
            {
                PublicName = instanceOperatorIdentity.PublicName,
                LegalName = instanceOperatorIdentity.LegalName,
                OperatorKindCode = instanceOperatorIdentity.OperatorKindCode,
                JurisdictionCountryCode = instanceOperatorIdentity.JurisdictionCountryCode,
                RegistrationIdentifier = instanceOperatorIdentity.RegistrationIdentifier,
                PublicContactEmail = instanceOperatorIdentity.PublicContactEmail,
                LegalNoticeUrl = instanceOperatorIdentity.LegalNoticeUrl,
                TermsUrl = instanceOperatorIdentity.TermsUrl,
                PrivacyUrl = instanceOperatorIdentity.PrivacyUrl
            }
            : null;

        return new CompleteInstanceOnboardingRequest
        {
            DeploymentMode = deploymentMode,
            SiteProfile = new SelfHostOnboardingProfileDto
            {
                SiteName = instanceOperatorIdentity.PublicName,
                SupportEmail = instanceOperatorIdentity.PublicContactEmail,
                CanonicalUrl = instanceOperatorIdentity.OfficialOrigin,
                Locale = "en",
                TimeZone = "UTC"
            },
            DirectoryOperatorIdentity = directoryIdentity,
            AdministrationAccessMode = CompleteInstanceOnboardingRequest.EmbeddedAdministrationAccess,
            InstanceName = instanceOperatorIdentity.PublicName
        };
    }

    private ProviderAccountKey BuildAccountKey(AuthenticationProviderKind providerKind, string subject)
    {
        try
        {
            if (providerKind == AuthenticationProviderKind.Local)
            {
                if (!Guid.TryParseExact(subject, "D", out Guid localSubject)
                    || localSubject.Version != 7 || localSubject.Variant is < 8 or > 11
                    || subject != localSubject.ToString("D"))
                    throw Failure("instance_bootstrap_local_subject_invalid");
                return new ProviderAccountKey(AuthenticationProviderKind.Local, subject);
            }

            if (providerKind == AuthenticationProviderKind.Atproto)
            {
                return AtprotoDid.TryParse(subject, out AtprotoDid did)
                    ? PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(did)
                    : throw Failure("instance_bootstrap_atproto_did_invalid");
            }

            string issuer = configuration["Keycloak:Authority"] ?? string.Empty;
            if (issuer.Length is 0 or > MaximumIssuerLength)
            {
                throw Failure("instance_bootstrap_keycloak_authority_invalid");
            }

            return PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(issuer, subject);
        }
        catch (ConfiguredAdministratorBootstrapException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw Failure("instance_bootstrap_provider_selector_invalid");
        }
    }

    private DeploymentMode ResolveDeploymentMode() => configuration["Deployment:Mode"] switch
    {
        null or "" => DeploymentMode.SingleTenant,
        "SingleTenant" => DeploymentMode.SingleTenant,
        "MultiTenant" => DeploymentMode.MultiTenant,
        _ => throw Failure("instance_bootstrap_deployment_mode_invalid")
    };

    private string Required(string key)
    {
        string? value = configuration[key];
        return HasValue(value)
            ? value!
            : throw Failure("instance_bootstrap_configured_matrix_incomplete");
    }

    private string? Optional(string key)
    {
        string? value = configuration[key];
        return HasValue(value) ? value : null;
    }

    private IEnumerable<string?> ConfiguredValues()
    {
        yield return configuration["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"];
        yield return configuration["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"];
        yield return configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"];
        yield return configuration["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"];
        yield return configuration["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"];
        yield return configuration["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"];
    }

    private static bool HasValue(string? value) => !string.IsNullOrEmpty(value);

    private static void ValidateSubject(string value)
    {
        if (value.Length > MaximumSubjectLength
            || value != value.Trim()
            || value.Any(char.IsControl))
        {
            throw Failure("instance_bootstrap_subject_invalid");
        }
    }

    private static string NormalizeEmail(string value)
    {
        if (value.Length is < 3 or > MaximumEmailLength
            || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw Failure("instance_bootstrap_email_invalid");
        }

        int separator = value.IndexOf('@');
        if (separator <= 0 || separator != value.LastIndexOf('@') || separator >= value.Length - 1)
        {
            throw Failure("instance_bootstrap_email_invalid");
        }

        return value.ToLowerInvariant();
    }

    private static string? NormalizeProfileName(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length is < 1 or > MaximumProfileNameLength
            || normalized.Any(char.IsControl))
        {
            throw Failure("instance_bootstrap_profile_name_invalid");
        }

        return normalized;
    }

    private static string Fingerprint(params string[] fields)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> prefix = stackalloc byte[sizeof(int)];
        foreach (string field in fields)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(prefix, bytes.Length);
            hash.AppendData(prefix);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static ConfiguredAdministratorBootstrapException Failure(string reasonCode) => new(reasonCode);

    internal sealed record ConfigurationSnapshot(
        InstanceBootstrapMode Mode,
        AuthenticationProviderKind? ProviderKind,
        DeploymentMode DeploymentMode,
        long Generation,
        string? ConfigurationFingerprint,
        string? SelectorFingerprint,
        ProviderAccountKey? AccountKey,
        CompleteInstanceOnboardingRequest? Settings,
        ConfiguredAdministratorProfile? AdministratorProfile)
    {
        public static ConfigurationSnapshot Interactive(DeploymentMode deploymentMode) =>
            new(InstanceBootstrapMode.Interactive, null, deploymentMode, 0, null, null, null, null, null);

        public static ConfigurationSnapshot Configured(
            AuthenticationProviderKind providerKind,
            DeploymentMode deploymentMode,
            long generation,
            string configurationFingerprint,
            string selectorFingerprint,
            ProviderAccountKey accountKey,
            CompleteInstanceOnboardingRequest settings,
            ConfiguredAdministratorProfile administratorProfile) =>
            new(
                InstanceBootstrapMode.ConfiguredAdministrator,
                providerKind,
                deploymentMode,
                generation,
                configurationFingerprint,
                selectorFingerprint,
                accountKey,
                settings,
                administratorProfile);
    }
}

public sealed class ConfiguredAdministratorBootstrapException(string reasonCode)
    : InvalidOperationException(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}
