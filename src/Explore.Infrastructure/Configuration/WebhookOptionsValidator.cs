using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Configuration;

public sealed class WebhookOptionsValidator : IValidateOptions<WebhookOptions>
{
    private static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        WebhookOptions.ProviderDisabled,
        WebhookOptions.ProviderLocal,
        WebhookOptions.ProviderSvix,
        WebhookOptions.ProviderComposite,
        WebhookOptions.ProviderDryRun
    };

    public ValidateOptionsResult Validate(string? name, WebhookOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Provider) || !SupportedProviders.Contains(options.Provider.Trim()))
        {
            failures.Add("Webhooks:Provider must be Disabled, Local, Svix, Composite, or DryRun.");
        }

        if (options.DefaultPayloadRetentionDays is < 1 or > 365)
        {
            failures.Add("Webhooks:DefaultPayloadRetentionDays must be between 1 and 365.");
        }

        ValidateSvixOperationalOptions(options.Svix, failures);
        ValidateStripeOptions(options.Stripe, failures);

        if (!options.IsDisabled && (options.IsProvider(WebhookOptions.ProviderSvix) || options.IsProvider(WebhookOptions.ProviderComposite)))
        {
            ValidateSvixOptions(options.Svix, failures);
        }

        if (options.Local.MaxAttempts is < 1 or > 20)
        {
            failures.Add("Webhooks:Local:MaxAttempts must be between 1 and 20.");
        }

        if (options.Local.InitialRetryDelaySeconds is < 1 or > 3600)
        {
            failures.Add("Webhooks:Local:InitialRetryDelaySeconds must be between 1 and 3600.");
        }

        if (options.Local.MaxRetryDelaySeconds is < 1 or > 7 * 24 * 60 * 60)
        {
            failures.Add("Webhooks:Local:MaxRetryDelaySeconds must be between 1 second and 7 days.");
        }

        if (options.Local.MaxRetryDelaySeconds < options.Local.InitialRetryDelaySeconds)
        {
            failures.Add("Webhooks:Local:MaxRetryDelaySeconds cannot be less than Webhooks:Local:InitialRetryDelaySeconds.");
        }

        if (options.Local.MaxRetryAfterSeconds is < 0 or > 24 * 60 * 60)
        {
            failures.Add("Webhooks:Local:MaxRetryAfterSeconds must be between 0 and 86400.");
        }

        if (options.Local.TimeoutSeconds is < 1 or > 60)
        {
            failures.Add("Webhooks:Local:TimeoutSeconds must be between 1 and 60.");
        }

        if (options.Local.ConnectTimeoutSeconds is < 1 or > 30)
        {
            failures.Add("Webhooks:Local:ConnectTimeoutSeconds must be between 1 and 30.");
        }

        if (options.Local.ConnectTimeoutSeconds > options.Local.TimeoutSeconds)
        {
            failures.Add("Webhooks:Local:ConnectTimeoutSeconds cannot exceed Webhooks:Local:TimeoutSeconds.");
        }

        if (options.Local.MaxPayloadBytes is < 1024 or > 2 * 1024 * 1024)
        {
            failures.Add("Webhooks:Local:MaxPayloadBytes must be between 1 KiB and 2 MiB.");
        }

        if (options.Local.MaxResponsePreviewBytes is < 0 or > 16 * 1024)
        {
            failures.Add("Webhooks:Local:MaxResponsePreviewBytes must be between 0 and 16 KiB.");
        }

        foreach (var cidr in options.Local.AllowedPrivateCidrs)
        {
            if (!WebhookIpNetwork.TryParse(cidr, out _))
            {
                failures.Add($"Webhooks:Local:AllowedPrivateCidrs contains invalid CIDR '{cidr}'.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSvixOptions(WebhookSvixOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.Environment) || options.Environment.Trim().Length > 100)
        {
            failures.Add("Webhooks:Svix:Environment is required and cannot exceed 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.ProviderVersion) || options.ProviderVersion.Trim().Length > 100)
        {
            failures.Add("Webhooks:Svix:ProviderVersion is required and cannot exceed 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.CapabilityPolicyVersion) ||
            options.CapabilityPolicyVersion.Trim().Length > 100)
        {
            failures.Add("Webhooks:Svix:CapabilityPolicyVersion is required and cannot exceed 100 characters.");
        }

        var normalizedBaseUrl = options.BaseUrl?.Trim();
        var baseUrlConfigured = !string.IsNullOrWhiteSpace(normalizedBaseUrl);
        if (!SvixConformanceProfileRegistry.TryResolve(
                options.Environment,
                options.ProviderVersion,
                options.CapabilityPolicyVersion,
                baseUrlConfigured,
                out var profile))
        {
            failures.Add(
                "Webhooks:Svix provider environment/version/capability policy is not present in the conformance matrix.");
        }
        else if (profile is null || !profile.IsVerified)
        {
            failures.Add(
                "Webhooks:Svix provider profile has no executed conformance evidence and cannot be selected.");
        }
        else
        {
            ValidateEnabledSvixCapabilities(options, profile, failures);
        }

        if (baseUrlConfigured)
        {
            if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var baseUrl))
            {
                failures.Add("Webhooks:Svix:BaseUrl must be an absolute URL when configured.");
            }
            else if (!string.Equals(baseUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(baseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Webhooks:Svix:BaseUrl must use http or https when configured.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.AuthTokenSecretRef))
        {
            failures.Add("Webhooks:Svix:AuthTokenSecretRef is required when Svix or Composite provider mode is configured.");
            return;
        }

        var authTokenSecretRef = options.AuthTokenSecretRef.Trim();
        if (!SecretDefinitionRegistry.IsKnown(authTokenSecretRef))
        {
            failures.Add("Webhooks:Svix:AuthTokenSecretRef must reference a known secret definition.");
        }
        else if (!string.Equals(
                     authTokenSecretRef,
                     SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken,
                     StringComparison.Ordinal))
        {
            failures.Add("Webhooks:Svix:AuthTokenSecretRef must reference the dedicated Svix auth-token secret definition.");
        }
    }

    private static void ValidateEnabledSvixCapabilities(
        WebhookSvixOptions options,
        SvixConformanceProfile profile,
        List<string> failures)
    {
        if (options.AppPortalEnabled &&
            !Supports(profile.Capabilities, WebhookProviderCapability.AppPortal))
        {
            failures.Add(
                "Webhooks:Svix:AppPortalEnabled requires the AppPortal capability in the verified provider profile.");
        }

        if (options.SyncEventTypesOnStartup &&
            !Supports(profile.Capabilities, WebhookProviderCapability.EventCatalog))
        {
            failures.Add(
                "Webhooks:Svix:SyncEventTypesOnStartup requires the EventCatalog capability in the verified provider profile.");
        }
    }

    private static bool Supports(
        WebhookProviderCapability available,
        WebhookProviderCapability required) =>
        (available & required) == required;

    private static void ValidateSvixOperationalOptions(WebhookSvixOptions options, List<string> failures)
    {
        if (options.OperationalWebhookMaxBodyBytes <= 0)
        {
            failures.Add("Webhooks:Svix:OperationalWebhookMaxBodyBytes must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(options.OperationalWebhookSecretRef))
        {
            var operationalSecretRef = options.OperationalWebhookSecretRef.Trim();
            if (!SecretDefinitionRegistry.IsKnown(operationalSecretRef))
            {
                failures.Add("Webhooks:Svix:OperationalWebhookSecretRef must reference a known secret definition when configured.");
            }
            else if (!string.Equals(
                         operationalSecretRef,
                         SecretDefinitionRegistry.Keys.Webhooks.SvixOperationalWebhookSecret,
                         StringComparison.Ordinal))
            {
                failures.Add("Webhooks:Svix:OperationalWebhookSecretRef must reference the dedicated Svix operational-webhook secret definition.");
            }
        }
    }

    private static void ValidateStripeOptions(WebhookStripeOptions options, List<string> failures)
    {
        if (options.ConnectWebhookMaxBodyBytes <= 0)
        {
            failures.Add("Webhooks:Stripe:ConnectWebhookMaxBodyBytes must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectWebhookSecretRef))
        {
            failures.Add("Webhooks:Stripe:ConnectWebhookSecretRef is required.");
            return;
        }

        var secretRef = options.ConnectWebhookSecretRef.Trim();
        if (!SecretDefinitionRegistry.IsKnown(secretRef))
        {
            failures.Add("Webhooks:Stripe:ConnectWebhookSecretRef must reference a known secret definition.");
        }
        else if (!string.Equals(secretRef, SecretDefinitionRegistry.Keys.Stripe.WebhookSecret, StringComparison.Ordinal))
        {
            failures.Add("Webhooks:Stripe:ConnectWebhookSecretRef must reference the dedicated Stripe webhook secret definition.");
        }
    }
}
