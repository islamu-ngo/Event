// ABOUTME: Behavioral visitor policy invariants evaluated through the real shared pure authority.
// ABOUTME: Covers provider aggregation, explicit onboarding, tenant usability, immutable facts and visitor modes.

using System.Text.Json;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Services;

public sealed class VisitorAccessCapabilityResolverTests
{
    [Test]
    public async Task LocalOnlyNeverCreatesPublicSignupEvenWhenDeclaredAllowed()
    {
        var capability = Evaluate(Provider(AuthenticationProviderKind.Local, PublicOnboardingPolicy.Allowed));

        await Assert.That(capability.AllowsNewNativeAllocation).IsTrue();
        await Assert.That(capability.AllowsAnonymousParticipation).IsTrue();
        await Assert.That(capability.AllowsAccountRequiredParticipation).IsFalse();
        await Assert.That(capability.AllowsExistingAccountLogin).IsFalse();
        await Assert.That(capability.SignupDestinations).IsEmpty();
    }

    [Test]
    public async Task SecondaryAtprotoSuppliesAccountOnboardingAlongsideLocalWithoutSmtpOrPrimaryInference()
    {
        var capability = Evaluate(
            Provider(AuthenticationProviderKind.Local),
            Provider(AuthenticationProviderKind.Atproto));

        await Assert.That(capability.AllowsAnonymousParticipation).IsTrue();
        await Assert.That(capability.AllowsAccountRequiredParticipation).IsTrue();
        await Assert.That(capability.AllowsExistingAccountLogin).IsTrue();
        await Assert.That(capability.SignupDestinations.Single().Provider).IsEqualTo(AuthenticationProviderKind.Atproto);
        await Assert.That(capability.SignupDestinations.Single().Url).IsEqualTo("/login?provider=atproto");
    }

    [Test]
    [Arguments(PublicOnboardingPolicy.Unknown)]
    [Arguments(PublicOnboardingPolicy.Denied)]
    [Arguments((PublicOnboardingPolicy)99)]
    public async Task KeycloakRequiresExplicitAllowedPolicyButKeepsExistingLogin(PublicOnboardingPolicy policy)
    {
        var capability = Evaluate(Provider(AuthenticationProviderKind.Keycloak, policy));

        await Assert.That(capability.AllowsAccountRequiredParticipation).IsFalse();
        await Assert.That(capability.AllowsExistingAccountLogin).IsTrue();
        await Assert.That(capability.SignupDestinations).IsEmpty();
    }

    [Test]
    public async Task ExplicitAllowedKeycloakPublishesOnlyConfiguredDestination()
    {
        var capability = Evaluate(Provider(AuthenticationProviderKind.Keycloak, PublicOnboardingPolicy.Allowed));

        await Assert.That(capability.AllowsAccountRequiredParticipation).IsTrue();
        await Assert.That(capability.SignupDestinations.Single().Url).IsEqualTo("https://identity.example/registration");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("/registration")]
    [Arguments("//identity.example/registration")]
    [Arguments("http://identity.example/registration")]
    [Arguments("javascript:alert(1)")]
    [Arguments("https://operator@identity.example/registration")]
    public async Task AllowedPolicyWithoutTrustedHttpsDestinationDoesNotInventOnboarding(string? url)
    {
        var capability = Evaluate(Provider(AuthenticationProviderKind.Keycloak, PublicOnboardingPolicy.Allowed) with { SignupUrl = url });

        await Assert.That(capability.AllowsAccountRequiredParticipation).IsFalse();
        await Assert.That(capability.AllowsExistingAccountLogin).IsTrue();
        await Assert.That(capability.SignupDestinations).IsEmpty();
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Atproto, false, true)]
    [Arguments(AuthenticationProviderKind.Atproto, true, false)]
    [Arguments(AuthenticationProviderKind.Keycloak, false, true)]
    [Arguments(AuthenticationProviderKind.Keycloak, true, false)]
    [Arguments(AuthenticationProviderKind.Google, false, true)]
    [Arguments(AuthenticationProviderKind.Google, true, false)]
    public async Task DisabledOrTenantUnusableProvidersContributeNeitherLoginNorOnboarding(
        AuthenticationProviderKind provider, bool enabled, bool tenantUsable)
    {
        var capability = Evaluate(Provider(provider, PublicOnboardingPolicy.Allowed) with { Enabled = enabled, TenantUsable = tenantUsable });

        await Assert.That(capability.AllowsExistingAccountLogin).IsFalse();
        await Assert.That(capability.AllowsAccountRequiredParticipation).IsFalse();
        await Assert.That(capability.SignupDestinations).IsEmpty();
    }

    [Test]
    public async Task AggregatesAllUsableProvidersRatherThanChoosingFirstOrPrimary()
    {
        var capability = Evaluate(
            Provider(AuthenticationProviderKind.Local),
            Provider(AuthenticationProviderKind.Keycloak, PublicOnboardingPolicy.Denied),
            Provider(AuthenticationProviderKind.Atproto),
            Provider(AuthenticationProviderKind.Google, PublicOnboardingPolicy.Allowed));

        await Assert.That(capability.SignupDestinations.Length).IsEqualTo(2);
        await Assert.That(capability.SignupDestinations.Select(destination => destination.Provider))
            .Contains(AuthenticationProviderKind.Atproto);
        await Assert.That(capability.SignupDestinations.Select(destination => destination.Provider))
            .Contains(AuthenticationProviderKind.Google);
    }

    [Test]
    public async Task AnonymousOnlyKeepsNewAnonymousAllocationsButSuppressesPublicAuth()
    {
        var capability = VisitorAccessCapabilityResolver.EvaluateProposedState(new(
            VisitorAccessMode.AnonymousOnly, [Provider(AuthenticationProviderKind.Atproto)]));

        await Assert.That(capability.Mode).IsEqualTo(VisitorAccessMode.AnonymousOnly);
        await Assert.That(capability.AllowsNewNativeAllocation).IsTrue();
        await Assert.That(capability.AllowsAnonymousParticipation).IsTrue();
        await Assert.That(capability.AllowsAccountRequiredParticipation).IsFalse();
        await Assert.That(capability.AllowsExistingAccountLogin).IsFalse();
        await Assert.That(capability.SignupDestinations).IsEmpty();
    }

    [Test]
    public async Task DirectoryOnlyDeniesNewNativeAllocationEvenWithCapableProviders()
    {
        var capability = VisitorAccessCapabilityResolver.EvaluateProposedState(new(
            VisitorAccessMode.DirectoryListingOnly, [Provider(AuthenticationProviderKind.Atproto)]));

        await Assert.That(capability.Mode).IsEqualTo(VisitorAccessMode.DirectoryListingOnly);
        await Assert.That(capability.AllowsNewNativeAllocation).IsFalse();
        await Assert.That(capability.AllowsAnonymousParticipation).IsFalse();
        await Assert.That(capability.AllowsAccountRequiredParticipation).IsFalse();
        await Assert.That(capability.AllowsExistingAccountLogin).IsFalse();
        await Assert.That(capability.SignupDestinations).IsEmpty();
    }

    [Test]
    public async Task CompleteProposedStateAndCapabilityAreIsolatedFromCallerMutation()
    {
        var providers = new List<VisitorAccessProviderState> { Provider(AuthenticationProviderKind.Atproto) };
        var state = new VisitorAccessPolicyState(VisitorAccessMode.FullRegistrationAndAuth, providers);
        providers.Clear();
        var before = VisitorAccessCapabilityResolver.EvaluateProposedState(state);
        var after = VisitorAccessCapabilityResolver.EvaluateProposedState(new(VisitorAccessMode.FullRegistrationAndAuth, providers));

        await Assert.That(before.AllowsAccountRequiredParticipation).IsTrue();
        await Assert.That(before.SignupDestinations.Single().Provider).IsEqualTo(AuthenticationProviderKind.Atproto);
        await Assert.That(after.AllowsAccountRequiredParticipation).IsFalse();
        await Assert.That(after.AllowsAnonymousParticipation).IsTrue();
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Development)]
    [Arguments((AuthenticationProviderKind)99)]
    public async Task UnsupportedProviderCannotManufacturePublicCapabilities(AuthenticationProviderKind provider)
    {
        var capability = Evaluate(Provider(provider, PublicOnboardingPolicy.Allowed));

        await Assert.That(capability.AllowsExistingAccountLogin).IsFalse();
        await Assert.That(capability.AllowsAccountRequiredParticipation).IsFalse();
    }

    [Test]
    public async Task LockedInstanceWinsAndUnlockRestoresTenantOverrideWithoutChangingStoredFacts()
    {
        var instance = new SystemSetting
        {
            SettingKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            Value = "\"DirectoryListingOnly\"",
            IsLocked = true
        };
        var tenant = new TenantSetting
        {
            SettingKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            Value = "\"AnonymousOnly\"",
            Tenant = new Tenant
            {
                FullName = "Community",
                Slug = "community",
                TenantStatus = new TenantStatus { MasterCode = "active", FullName = "Active", IsActiveState = true }
            }
        };

        await Assert.That(VisitorAccessCapabilityResolver.ResolveMode(instance, tenant))
            .IsEqualTo(VisitorAccessMode.DirectoryListingOnly);
        instance.IsLocked = false;
        await Assert.That(VisitorAccessCapabilityResolver.ResolveMode(instance, tenant))
            .IsEqualTo(VisitorAccessMode.AnonymousOnly);
        await Assert.That(VisitorAccessCapabilityResolver.ResolveMode(instance, null))
            .IsEqualTo(VisitorAccessMode.DirectoryListingOnly);
        await Assert.That(VisitorAccessCapabilityResolver.ResolveMode(null, tenant))
            .IsEqualTo(VisitorAccessMode.AnonymousOnly);
        await Assert.That(VisitorAccessCapabilityResolver.ResolveMode(null, null))
            .IsEqualTo(VisitorAccessMode.FullRegistrationAndAuth);
    }

    [Test]
    [Arguments(VisitorAccessMode.FullRegistrationAndAuth)]
    [Arguments(VisitorAccessMode.AnonymousOnly)]
    [Arguments(VisitorAccessMode.DirectoryListingOnly)]
    public async Task NativeStoredEnumNamesResolveExactly(VisitorAccessMode mode)
    {
        var setting = new SystemSetting
        {
            SettingKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            Value = JsonSerializer.Serialize(mode.ToString())
        };
        await Assert.That(VisitorAccessCapabilityResolver.ResolveMode(setting, null)).IsEqualTo(mode);
    }

    [Test]
    [Arguments("\"fullregistrationandauth\"")]
    [Arguments("\"0\"")]
    [Arguments("0")]
    [Arguments("\"Unknown\"")]
    [Arguments("null")]
    [Arguments("")]
    [Arguments("\"\"")]
    public async Task InvalidPersistedModeCannotFallBackToPermissiveDefault(string value)
    {
        await Assert.That(() => VisitorAccessCapabilityResolver.ResolveMode(
            new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                Value = value
            }, null)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnknownProposedModeCannotAdmitNativeAllocations()
    {
        await Assert.That(() => VisitorAccessCapabilityResolver.EvaluateProposedState(new(
            (VisitorAccessMode)99, [Provider(AuthenticationProviderKind.Atproto)])))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FullFinalProviderReplacementDoesNotEvaluateAnUnsafeIntermediateState()
    {
        var before = Evaluate(Provider(AuthenticationProviderKind.Keycloak, PublicOnboardingPolicy.Allowed));
        var proposedProviders = new[]
        {
            Provider(AuthenticationProviderKind.Keycloak, PublicOnboardingPolicy.Denied),
            Provider(AuthenticationProviderKind.Google, PublicOnboardingPolicy.Allowed)
        };
        var after = Evaluate(proposedProviders);

        await Assert.That(before.AllowsAccountRequiredParticipation).IsTrue();
        await Assert.That(after.AllowsAccountRequiredParticipation).IsTrue();
        await Assert.That(after.SignupDestinations.Single().Provider).IsEqualTo(AuthenticationProviderKind.Google);
        await Assert.That(before.SignupDestinations.Single().Provider).IsEqualTo(AuthenticationProviderKind.Keycloak);
    }

    private static VisitorAccessCapability Evaluate(params VisitorAccessProviderState[] providers) =>
        VisitorAccessCapabilityResolver.EvaluateProposedState(new(VisitorAccessMode.FullRegistrationAndAuth, providers));

    private static VisitorAccessProviderState Provider(
        AuthenticationProviderKind provider,
        PublicOnboardingPolicy policy = PublicOnboardingPolicy.Unknown) =>
        new(provider, true, true, policy, "https://identity.example/registration");
}
