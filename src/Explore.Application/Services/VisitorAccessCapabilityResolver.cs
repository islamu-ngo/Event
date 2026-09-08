
using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class VisitorAccessCapabilityResolver(
    ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings,
    IVisitorAccessProviderReader providerReader) : IVisitorAccessCapabilityResolver
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
        GovernanceSettingKeys.PublicExperience.VisitorAccessMode);

    public async Task<VisitorAccessCapability> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        SystemSetting? instance = await systemSettings.GetByKey(
            GovernanceSettingKeys.PublicExperience.VisitorAccessMode, cancellationToken);
        TenantSetting? tenant = await tenantSettings.GetByTenantAndKey(
            tenantId, GovernanceSettingKeys.PublicExperience.VisitorAccessMode, cancellationToken);
        var mode = ResolveMode(instance, tenant);
        var providers = await providerReader.ReadProvidersAsync(tenantId, cancellationToken);
        return EvaluateProposedState(new VisitorAccessPolicyState(mode, providers));
    }

    /// <summary>Resolves scalar mode inheritance for current or complete proposed native settings.</summary>
    public static VisitorAccessMode ResolveMode(SystemSetting? instanceSetting, TenantSetting? tenantSetting)
    {
        string? raw = instanceSetting?.IsLocked == true
            ? instanceSetting.Value
            : tenantSetting?.Value ?? instanceSetting?.Value;
        if (raw is null)
        {
            return VisitorAccessMode.FullRegistrationAndAuth;
        }

        string? value;
        try
        {
            value = JsonSerializer.Deserialize<string>(raw);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The persisted visitor access mode is invalid.", exception);
        }

        return Enum.TryParse(value, out VisitorAccessMode mode)
               && Enum.IsDefined(mode)
               && string.Equals(mode.ToString(), value, StringComparison.Ordinal)
            ? mode
            : throw new InvalidOperationException("The persisted visitor access mode is invalid.");
    }

    public VisitorAccessCapability Evaluate(VisitorAccessPolicyState proposedState) => EvaluateProposedState(proposedState);

    /// <summary>Evaluates complete final facts without reads, writes, discovery or SMTP inference.</summary>
    public static VisitorAccessCapability EvaluateProposedState(VisitorAccessPolicyState proposedState)
    {
        if (!Enum.IsDefined(proposedState.Mode))
        {
            throw new InvalidOperationException("The proposed visitor access mode is invalid.");
        }

        if (proposedState.Mode != VisitorAccessMode.FullRegistrationAndAuth)
        {
            return new(proposedState.Mode, false, []);
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
                case AuthenticationProviderKind.Atproto:
                    // Provider-selected handle authorization leads to passwordless platform
                    // enrollment through AtprotoJitAccountProvisioningOperation, not Local signup.
                    allowsExistingAccountLogin = true;
                    destinations.Add(new(provider.Provider, "/login?provider=atproto"));
                    break;
                case AuthenticationProviderKind.Keycloak:
                case AuthenticationProviderKind.Google:
                    allowsExistingAccountLogin = true;
                    if (provider.PublicOnboardingPolicy == PublicOnboardingPolicy.Allowed
                        && Uri.TryCreate(provider.SignupUrl, UriKind.Absolute, out var destination)
                        && destination.Scheme == Uri.UriSchemeHttps
                        && string.IsNullOrEmpty(destination.UserInfo))
                    {
                        destinations.Add(new(provider.Provider, destination.AbsoluteUri));
                    }
                    break;
                // Local is operator-only; unknown authorities cannot create public capabilities.
            }
        }

        return new(proposedState.Mode, allowsExistingAccountLogin, destinations);
    }
}
