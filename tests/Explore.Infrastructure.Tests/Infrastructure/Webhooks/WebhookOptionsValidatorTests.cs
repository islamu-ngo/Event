using Explore.Domain.Secrets;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookOptionsValidatorTests
{
    private readonly WebhookOptionsValidator _validator = new();

    [Test]
    public async Task Validate_WhenDefaultLocalOptions_ReturnsSuccess()
    {
        var result = _validator.Validate(null, new WebhookOptions());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WhenSvixPortalEnabledWithoutAuthSecretRef_ReturnsFailure()
    {
        var result = _validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new WebhookSvixOptions
            {
                AppPortalEnabled = true,
                AuthTokenSecretRef = ""
            }
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("Webhooks:Svix:AuthTokenSecretRef is required");
    }

    [Test]
    public async Task Validate_WhenSvixEventTypeSyncUsesUnknownAuthSecretRef_ReturnsFailure()
    {
        var result = _validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderComposite,
            Svix = new WebhookSvixOptions
            {
                SyncEventTypesOnStartup = true,
                AuthTokenSecretRef = "webhooks.svix.missing"
            }
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("Webhooks:Svix:AuthTokenSecretRef must reference a known secret definition");
    }

    [Test]
    public async Task Validate_WhenSvixUsesDifferentKnownSecretDefinition_ReturnsFailure()
    {
        var options = SupportedSelfHostedOptions();
        options.AuthTokenSecretRef = SecretDefinitionRegistry.Keys.Storage.SecretAccessKey;

        var result = _validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = options
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("dedicated Svix auth-token secret definition");
    }

    [Test]
    public async Task Validate_WhenSvixBaseUrlUsesNonHttpScheme_ReturnsFailure()
    {
        var result = _validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new WebhookSvixOptions
            {
                BaseUrl = "file:///tmp/svix",
                AuthTokenSecretRef = SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken
            }
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("Webhooks:Svix:BaseUrl must use http or https");
    }

    [Test]
    public async Task Validate_WhenSvixAuthorityProfileIsIncomplete_ReturnsFailure()
    {
        var result = _validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new WebhookSvixOptions
            {
                Environment = "",
                ProviderVersion = "",
                CapabilityPolicyVersion = "",
                AuthTokenSecretRef = SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken
            }
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("Webhooks:Svix:Environment is required");
        await Assert.That(result.FailureMessage).Contains("Webhooks:Svix:ProviderVersion is required");
        await Assert.That(result.FailureMessage).Contains("Webhooks:Svix:CapabilityPolicyVersion is required");
    }

    [Test]
    public async Task Validate_WhenSelfHostedProfileHasExecutedEvidence_ReturnsSuccess()
    {
        var result = _validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = SupportedSelfHostedOptions()
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WhenManagedProfileHasNoExecutedEvidence_ReturnsFailure()
    {
        var result = _validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new WebhookSvixOptions
            {
                BaseUrl = null,
                Environment = SvixConformanceProfileRegistry.ManagedEnvironment,
                ProviderVersion = SvixConformanceProfileRegistry.ManagedProviderVersion,
                CapabilityPolicyVersion = SvixConformanceProfileRegistry.ManagedCapabilityPolicyVersion,
                AuthTokenSecretRef = SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken
            }
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("no executed conformance evidence");
    }

    [Test]
    public async Task Validate_WhenProfileTupleIsUnknown_ReturnsFailure()
    {
        var svix = SupportedSelfHostedOptions();
        svix.ProviderVersion = "unsupported";

        var result = _validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = svix
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("not present in the conformance matrix");
    }

    private static WebhookSvixOptions SupportedSelfHostedOptions() =>
        new()
        {
            BaseUrl = "http://svix:8071",
            Environment = SvixConformanceProfileRegistry.SelfHostedEnvironment,
            ProviderVersion = SvixConformanceProfileRegistry.SelfHostedProviderVersion,
            CapabilityPolicyVersion = SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion,
            AuthTokenSecretRef = SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken
        };
}
