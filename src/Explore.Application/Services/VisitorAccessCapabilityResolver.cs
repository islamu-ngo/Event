
using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services;

namespace Explore.Application.Services;

public sealed class VisitorAccessCapabilityResolver(
    IInstanceTenantSettingsReader settings,
    IVisitorAccessProviderReader providerReader,
    ITenantLifecycleAccessService lifecycle) : IVisitorAccessCapabilityResolver
{
    /// <summary>
    /// Complete ordinal-ordered authority fence. Acquire the FULL group through
    /// ISettingMutationLock.ExecuteOrderedGroupsAsync BEFORE reads/ExecuteSerializableAsync,
    /// retaining the outer lease through commit. ExecuteManyAsync is not an outer lease.
    /// Provider metadata writers and mode mutations use the same fence as new allocations.
    /// Deployment-owned configuration must remain stable for the lifetime of that operation.
    /// </summary>
    public static IReadOnlyList<string> AuthoritySettingKeys { get; } = ImmutableArray.Create(
        GovernanceSettingKeys.Authentication.AtprotoLoginEnabled,
        GovernanceSettingKeys.Authentication.AtprotoPublicUrl,
        GovernanceSettingKeys.Authentication.GoogleClientId,
        GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy,
        GovernanceSettingKeys.Authentication.GooglePublicSignupUrl,
        GovernanceSettingKeys.Authentication.GoogleSsoEnabled,
        GovernanceSettingKeys.Authentication.KeycloakAuthority,
        GovernanceSettingKeys.Authentication.KeycloakClientId,
        GovernanceSettingKeys.Authentication.KeycloakPublicOnboardingPolicy,
        GovernanceSettingKeys.Authentication.KeycloakPublicSignupUrl,
        GovernanceSettingKeys.Authentication.PrimaryProviderId,
        GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
        GovernanceSettingKeys.Tenants.UserRegistrationEnabled);

    public async Task<VisitorAccessCapability> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (!await lifecycle.IsPublicAsync(tenantId, cancellationToken))
            return new VisitorAccessCapability(VisitorAccessMode.DirectoryListingOnly, false, [], false);

        var current = await settings.ReadAsync(tenantId,
            AuthoritySettingKeys, cancellationToken);
        SystemSetting? instance = current.System.SingleOrDefault(setting =>
            setting.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode);
        TenantSetting? tenant = current.Tenant.SingleOrDefault(setting =>
            setting.SettingKey == GovernanceSettingKeys.PublicExperience.VisitorAccessMode);
        var mode = ResolveMode(instance, tenant);
        var providers = providerReader.ReadProviders(current.System);
        return EvaluateProposedState(new VisitorAccessPolicyState(mode, providers, ResolveRegistrationEnabled(
            current.System.SingleOrDefault(setting => setting.SettingKey == GovernanceSettingKeys.Tenants.UserRegistrationEnabled),
            current.Tenant.SingleOrDefault(setting => setting.SettingKey == GovernanceSettingKeys.Tenants.UserRegistrationEnabled))));
    }

    /// <summary>Resolves scalar mode inheritance for current or complete proposed native settings.</summary>
    public static VisitorAccessMode ResolveMode(SystemSetting? instanceSetting, TenantSetting? tenantSetting)
        => VisitorReadPolicy.ResolveMode(instanceSetting?.Value, instanceSetting?.IsLocked == true,
            tenantSetting?.Value);

    public VisitorAccessCapability Evaluate(VisitorAccessPolicyState proposedState) => EvaluateProposedState(proposedState);

    public static bool ResolveRegistrationEnabled(SystemSetting? instanceSetting, TenantSetting? tenantSetting)
        => TenantUserRegistrationPolicy.ResolveEnabled(instanceSetting?.Value, instanceSetting?.IsLocked == true,
            tenantSetting?.Value);

    /// <summary>Evaluates complete final facts without reads, writes, discovery or SMTP inference.</summary>
    public static VisitorAccessCapability EvaluateProposedState(VisitorAccessPolicyState proposedState)
    {
        if (!Enum.IsDefined(proposedState.Mode))
        {
            throw new InvalidOperationException("The proposed visitor access mode is invalid.");
        }

        if (proposedState.Mode is not (VisitorAccessMode.FullRegistrationAndAuth or VisitorAccessMode.AuthenticatedOnly))
        {
            return new(proposedState.Mode, false, [], proposedState.UserRegistrationEnabled);
        }

        var destinations = new List<VisitorSignupDestination>();
        bool allowsExistingAccountLogin = false;
        foreach (var provider in proposedState.Providers)
        {
            if (!provider.Enabled || !provider.TenantUsable)
            {
                continue;
            }

            switch (provider.Provider)
            {
                case AuthenticationProviderKind.Local:
                    allowsExistingAccountLogin = true;
                    break;
                case AuthenticationProviderKind.Atproto:
                    // Provider-selected handle authorization leads to passwordless platform
                    // enrollment through AtprotoJitAccountProvisioningOperation, not Local signup.
                    allowsExistingAccountLogin = true;
                    if (proposedState.UserRegistrationEnabled)
                        destinations.Add(new(provider.Provider, "/login?provider=atproto"));
                    break;
                case AuthenticationProviderKind.Keycloak:
                    allowsExistingAccountLogin = true;
                    if (proposedState.UserRegistrationEnabled
                        && provider.PublicOnboardingPolicy == PublicOnboardingPolicy.Allowed
                        && Uri.TryCreate(provider.SignupUrl, UriKind.Absolute, out var destination)
                        && destination.Scheme == Uri.UriSchemeHttps
                        && string.IsNullOrEmpty(destination.UserInfo))
                    {
                        destinations.Add(new(provider.Provider, destination.AbsoluteUri));
                    }
                    break;
            }
        }

        return new(proposedState.Mode, allowsExistingAccountLogin, destinations, proposedState.UserRegistrationEnabled);
    }
}
