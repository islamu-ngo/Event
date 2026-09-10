using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Models;
using Explore.Domain.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Services;

public class AuthProviderConfigurationService : IAuthProviderConfigurationService
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IVisitorAccessSettingsWriter _visitorSettings;
    private readonly MediatR.IMediator _mediator;

    public AuthProviderConfigurationService(
        ISystemSettingRepository systemSettingRepository,
        IConfiguration configuration,
        IUnitOfWork unitOfWork,
        ISettingMutationLock mutationLock,
        IVisitorAccessSettingsWriter visitorSettings,
        MediatR.IMediator mediator)
    {
        _systemSettingRepository = systemSettingRepository;
        _configuration = configuration;
        _unitOfWork = unitOfWork;
        _mutationLock = mutationLock;
        _visitorSettings = visitorSettings;
        _mediator = mediator;
    }

    // This single projection is shared by authoritative reads and detached proposed-state validation.
    public static IReadOnlyList<VisitorAccessProviderState> ProjectVisitorProviders(
        IReadOnlyDictionary<string, SystemSetting> settings, IConfiguration configuration)
    {
        string Stored(string key) => settings.GetValueOrDefault(key)?.Value
            ?? SettingRegistry.Get(key)?.DefaultValue ?? "null";
        string Text(string key) => DeserializeString(Stored(key), string.Empty);
        string Configured(params string[] keys) => keys.Select(key => configuration[key])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        PublicOnboardingPolicy Onboarding(string key) =>
            Enum.TryParse(Text(key), out PublicOnboardingPolicy policy) && Enum.IsDefined(policy)
                ? policy : PublicOnboardingPolicy.Unknown;

        string deploymentProvider = Configured("Authentication:Provider", "AUTHENTICATION_PROVIDER");
        AuthenticationProviderKind primary = !string.IsNullOrWhiteSpace(deploymentProvider)
            ? deploymentProvider.ParseAuthenticationProviderKind()
            : RequireSupportedPrimaryProvider(JsonSerializer.Deserialize<int>(Stored(GovernanceSettingKeys.Authentication.PrimaryProviderId)));
        bool atprotoEnabled = primary == AuthenticationProviderKind.Atproto
            || DeserializeBoolean(Stored(GovernanceSettingKeys.Authentication.AtprotoLoginEnabled), false);
        bool googleEnabled = primary != AuthenticationProviderKind.Atproto
            && DeserializeBoolean(Stored(GovernanceSettingKeys.Authentication.GoogleSsoEnabled), false);
        var keycloak = ProjectKeycloakConfiguration(primary,
            settings.GetValueOrDefault(GovernanceSettingKeys.Authentication.KeycloakAuthority),
            settings.GetValueOrDefault(GovernanceSettingKeys.Authentication.KeycloakClientId), configuration);
        bool atprotoUsable = Uri.TryCreate(Text(GovernanceSettingKeys.Authentication.AtprotoPublicUrl), UriKind.Absolute, out var atprotoUrl)
            && atprotoUrl.Scheme == Uri.UriSchemeHttps;
        return [
            new(AuthenticationProviderKind.Local, primary == AuthenticationProviderKind.Local, true, PublicOnboardingPolicy.Denied),
            new(AuthenticationProviderKind.Keycloak, primary == AuthenticationProviderKind.Keycloak, keycloak.Enabled,
                Onboarding(GovernanceSettingKeys.Authentication.KeycloakPublicOnboardingPolicy),
                Text(GovernanceSettingKeys.Authentication.KeycloakPublicSignupUrl)),
            new(AuthenticationProviderKind.Atproto, atprotoEnabled, atprotoUsable, PublicOnboardingPolicy.Allowed),
            new(AuthenticationProviderKind.Google, googleEnabled,
                !string.IsNullOrWhiteSpace(Text(GovernanceSettingKeys.Authentication.GoogleClientId)),
                Onboarding(GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy),
                Text(GovernanceSettingKeys.Authentication.GooglePublicSignupUrl))
        ];
    }

    public async Task<AuthProviderConfigurationDto> ReadConfigurationAsync()
    {
        var primaryProviderSetting = await _systemSettingRepository.GetByKey(
            GovernanceSettingKeys.Authentication.PrimaryProviderId);
        AuthenticationProviderKind primaryProvider = ResolvePrimaryProvider(primaryProviderSetting);
        var keycloak = await ResolveKeycloakConfigurationAsync(primaryProvider);
        var atprotoLoginEnabled = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.AtprotoLoginEnabled);
        var atprotoPublicUrl = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.AtprotoPublicUrl);
        var googleSsoEnabled = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.GoogleSsoEnabled);
        var googleClientId = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.GoogleClientId);
        var keycloakSecret = await _systemSettingRepository.GetByKey(InfrastructureSecretSettingKeys.Authentication.KeycloakClientSecret);

        var keycloakSecretDeploymentManaged = IsKeycloakClientSecretDeploymentManaged();
        var storedKeycloakSecretConfigured = !string.IsNullOrWhiteSpace(DeserializeString(keycloakSecret?.Value, string.Empty));
        var configuredKeycloakSecretConfigured = !string.IsNullOrWhiteSpace(ReadConfiguredKeycloakClientSecret());

        return new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)primaryProvider,
            PrimaryProviderCode = primaryProvider.ToAuthenticationProviderCode(),
            PrimaryProviderName = GetProviderDisplayName(primaryProvider),
            LockPrimaryProvider = IsPrimaryProviderDeploymentManaged()
                                  || primaryProviderSetting?.IsLocked == true,
            KeycloakAuthority = keycloak.Authority,
            KeycloakClientId = keycloak.ClientId,
            KeycloakClientSecret = string.Empty, // Never return secrets on read
            KeycloakDetectedFromEnvironment = keycloak.DetectedFromEnvironment,
            KeycloakClientSecretOwnership = CreateOwnershipMetadata(
                keycloakSecretDeploymentManaged,
                configured: keycloakSecretDeploymentManaged
                    ? configuredKeycloakSecretConfigured
                    : storedKeycloakSecretConfigured || configuredKeycloakSecretConfigured,
                bootstrapAvailable: !keycloakSecretDeploymentManaged
                    && !storedKeycloakSecretConfigured
                    && configuredKeycloakSecretConfigured,
                applicationManagedDescription: "Keycloak client secret can be rotated and stored by the platform. Deployment values only seed runtime configuration until an application-managed secret is saved.",
                deploymentManagedDescription: "Keycloak client secret is deployment-managed. Rotate it in the deployment secret provider and update the matching Keycloak client outside the Admin UI."),
            AtprotoLoginEnabled = primaryProvider == AuthenticationProviderKind.Atproto
                                  || DeserializeBoolean(atprotoLoginEnabled?.Value, false),
            AtprotoPublicUrl = DeserializeString(atprotoPublicUrl?.Value, string.Empty),
            GoogleSsoEnabled = primaryProvider != AuthenticationProviderKind.Atproto
                && DeserializeBoolean(googleSsoEnabled?.Value, false),
            GoogleClientId = DeserializeString(googleClientId?.Value, string.Empty),
            GoogleClientSecret = string.Empty, // Never return secrets on read
            LockAtprotoLoginEnabled = atprotoLoginEnabled?.IsLocked == true,
            LockGoogleSsoEnabled = googleSsoEnabled?.IsLocked == true,
            KeycloakPublicOnboardingPolicy = ReadOnboardingPolicy((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.KeycloakPublicOnboardingPolicy))?.Value),
            KeycloakPublicSignupUrl = DeserializeString((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.KeycloakPublicSignupUrl))?.Value, string.Empty),
            GooglePublicOnboardingPolicy = ReadOnboardingPolicy((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy))?.Value),
            GooglePublicSignupUrl = DeserializeString((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.GooglePublicSignupUrl))?.Value, string.Empty),
        };
    }

    public async Task ApplyConfigurationAsync(AuthProviderConfigurationDto configuration,
        IReadOnlySet<string>? suppliedKeys = null, CancellationToken cancellationToken = default)
    {
        var notifications = await _mutationLock.ExecuteOrderedGroupsAsync(
            [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
            token => _unitOfWork.ExecuteSerializableAsync(
                innerToken => ApplyConfigurationInCurrentTransactionAsync(configuration, suppliedKeys, innerToken), token),
            cancellationToken);
        foreach (var notification in notifications)
            await _mediator.Publish(notification, CancellationToken.None);
    }

    private async Task<System.Collections.Immutable.ImmutableArray<Explore.Application.Notifications.SettingChangedNotification>> ApplyConfigurationInCurrentTransactionAsync(
        AuthProviderConfigurationDto configuration,
        IReadOnlySet<string>? suppliedKeys,
        CancellationToken cancellationToken)
    {
        var writes = new List<SystemSetting>();
        Task UpsertSettingAsync(string key, string value, SettingValueType valueType, bool isLocked,
            string category, int displayOrder, string description, CancellationToken token)
        {
            if (suppliedKeys is null || suppliedKeys.Contains(key))
                writes.Add(new SystemSetting
                {
                    SettingKey = key,
                    Value = value,
                    ValueType = valueType,
                    IsLocked = isLocked,
                    Category = category,
                    DisplayOrder = displayOrder,
                    Description = description,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            return Task.CompletedTask;
        }
        AuthenticationProviderKind primaryProvider =
            RequireSupportedPrimaryProvider(configuration.PrimaryProviderId);
        bool atprotoLoginEnabled =
            primaryProvider == AuthenticationProviderKind.Atproto
            || configuration.AtprotoLoginEnabled;
        bool googleSsoEnabled = primaryProvider != AuthenticationProviderKind.Atproto
            && configuration.GoogleSsoEnabled;
        await UpsertSettingAsync(
            GovernanceSettingKeys.Authentication.PrimaryProviderId,
            JsonSerializer.Serialize((int)primaryProvider),
            SettingValueType.Integer,
            configuration.LockPrimaryProvider,
            "Authentication",
            1,
            "Normalized lookup identifier for the active primary authentication provider",
            cancellationToken);

        if (!IsKeycloakAuthorityDeploymentManaged())
        {
            await UpsertSettingAsync(
                GovernanceSettingKeys.Authentication.KeycloakAuthority,
                JsonSerializer.Serialize(configuration.KeycloakAuthority),
                SettingValueType.String,
                true,
                "Authentication",
                2,
                "Keycloak realm authority URL",
                cancellationToken);
        }

        if (!IsKeycloakClientIdDeploymentManaged())
        {
            await UpsertSettingAsync(
                GovernanceSettingKeys.Authentication.KeycloakClientId,
                JsonSerializer.Serialize(configuration.KeycloakClientId),
                SettingValueType.String,
                true,
                "Authentication",
                3,
                "Keycloak OIDC client ID",
                cancellationToken);
        }

        if (!string.IsNullOrEmpty(configuration.KeycloakClientSecret)
            && !IsKeycloakClientSecretDeploymentManaged())
        {
            await UpsertSettingAsync(
                InfrastructureSecretSettingKeys.Authentication.KeycloakClientSecret,
                JsonSerializer.Serialize(configuration.KeycloakClientSecret),
                SettingValueType.String,
                true,
                "Authentication",
                4,
                "Keycloak OIDC client secret",
                cancellationToken);
        }

        await UpsertSettingAsync(
            GovernanceSettingKeys.Authentication.AtprotoLoginEnabled,
            JsonSerializer.Serialize(atprotoLoginEnabled),
            SettingValueType.Boolean,
            configuration.LockAtprotoLoginEnabled,
            "Authentication",
            5,
            "Whether ATProto DID-based authentication is enabled",
            cancellationToken);

        await UpsertSettingAsync(
            GovernanceSettingKeys.Authentication.AtprotoPublicUrl,
            JsonSerializer.Serialize(configuration.AtprotoPublicUrl),
            SettingValueType.String,
            true,
            "Authentication",
            6,
            "Publicly accessible URL for ATProto OAuth client metadata",
            cancellationToken);

        await UpsertSettingAsync(
            GovernanceSettingKeys.Authentication.GoogleSsoEnabled,
            JsonSerializer.Serialize(googleSsoEnabled),
            SettingValueType.Boolean,
            configuration.LockGoogleSsoEnabled,
            "Authentication",
            7,
            "Whether Google SSO authentication is enabled",
            cancellationToken);

        await UpsertSettingAsync(
            GovernanceSettingKeys.Authentication.GoogleClientId,
            JsonSerializer.Serialize(configuration.GoogleClientId),
            SettingValueType.String,
            true,
            "Authentication",
            8,
            "Google OAuth client ID",
            cancellationToken);

        if (!string.IsNullOrEmpty(configuration.GoogleClientSecret))
        {
            await UpsertSettingAsync(
                InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret,
                JsonSerializer.Serialize(configuration.GoogleClientSecret),
                SettingValueType.String,
                true,
                "Authentication",
                9,
                "Google OAuth client secret",
                cancellationToken);
        }

        foreach (var (key, value) in new[] {
            (GovernanceSettingKeys.Authentication.KeycloakPublicOnboardingPolicy, configuration.KeycloakPublicOnboardingPolicy.ToString()),
            (GovernanceSettingKeys.Authentication.KeycloakPublicSignupUrl, configuration.KeycloakPublicSignupUrl),
            (GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy, configuration.GooglePublicOnboardingPolicy.ToString()),
            (GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, configuration.GooglePublicSignupUrl) })
            await UpsertSettingAsync(key, JsonSerializer.Serialize(value), SettingValueType.String, true,
                "Authentication", 0, "Public provider onboarding policy", cancellationToken);

        var result = await _visitorSettings.ApplyAsync(
            [.. writes.Where(setting => VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(setting.SettingKey))
                .Select(setting => new VisitorAccessSettingMutation(null, setting.SettingKey,
                    VisitorAccessSettingMutationKind.SetValue, setting.Value, suppliedKeys is null ? setting.IsLocked : null))], null, cancellationToken);
        if (!result.Success)
            throw new Explore.Application.Exceptions.ConcurrencyConflictException(result.FailureCode!,
                "The change would remove public signup required by an event participation configuration.");
        foreach (var setting in writes.Where(setting => !VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(setting.SettingKey)))
            await _systemSettingRepository.UpsertInCurrentTransactionAsync(setting, cancellationToken);
        return result.DeferredNotifications;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var primaryProviderSetting = await _systemSettingRepository.GetByKey(
            GovernanceSettingKeys.Authentication.PrimaryProviderId);
        AuthenticationProviderKind primaryProvider = ResolvePrimaryProvider(primaryProviderSetting);
        var keycloak = await ResolveKeycloakConfigurationAsync(primaryProvider);
        var atprotoEnabled = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.AtprotoLoginEnabled);
        var googleEnabled = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.GoogleSsoEnabled);

        return primaryProvider is AuthenticationProviderKind.Local
                   or AuthenticationProviderKind.Atproto
               || keycloak.Enabled
               || DeserializeBoolean(atprotoEnabled?.Value, false)
               || DeserializeBoolean(googleEnabled?.Value, false);
    }

    public async Task<AuthProviderConfigurationDto> ReadConfigurationWithSecretsAsync()
    {
        var dto = await ReadConfigurationAsync();

        var keycloakSecret = await _systemSettingRepository.GetByKey(InfrastructureSecretSettingKeys.Authentication.KeycloakClientSecret);
        var googleSecret = await _systemSettingRepository.GetByKey(InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret);

        var storedKeycloakSecret = DeserializeString(keycloakSecret?.Value, string.Empty);
        var configuredKeycloakSecret = ReadConfiguredKeycloakClientSecret();
        dto.KeycloakClientSecret = IsKeycloakClientSecretDeploymentManaged()
            ? configuredKeycloakSecret
            : string.IsNullOrWhiteSpace(storedKeycloakSecret)
                ? configuredKeycloakSecret
                : storedKeycloakSecret;
        dto.GoogleClientSecret = DeserializeString(googleSecret?.Value, string.Empty);

        return dto;
    }

    private async Task<(bool Enabled, string Authority, string ClientId, bool DetectedFromEnvironment)>
        ResolveKeycloakConfigurationAsync(AuthenticationProviderKind primaryProvider)
    {
        var authoritySetting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.KeycloakAuthority);
        var clientIdSetting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Authentication.KeycloakClientId);
        return ProjectKeycloakConfiguration(primaryProvider, authoritySetting, clientIdSetting, _configuration);
    }

    private static (bool Enabled, string Authority, string ClientId, bool DetectedFromEnvironment)
        ProjectKeycloakConfiguration(AuthenticationProviderKind primaryProvider, SystemSetting? authoritySetting,
            SystemSetting? clientIdSetting, IConfiguration configuration)
    {
        var storedAuthority = DeserializeString(authoritySetting?.Value, string.Empty);
        var storedClientId = DeserializeString(clientIdSetting?.Value, string.Empty);
        var storedUsable = !string.IsNullOrWhiteSpace(storedAuthority)
                           && !string.IsNullOrWhiteSpace(storedClientId);
        var deploymentAuthority = configuration["Keycloak:Authority"]?.Trim() ?? string.Empty;
        var deploymentClientId = configuration["Keycloak:ClientId"]?.Trim() ?? string.Empty;
        var deploymentUsable = !string.IsNullOrWhiteSpace(deploymentAuthority)
                               && !string.IsNullOrWhiteSpace(deploymentClientId);
        var authorityDeploymentManaged = IsDeploymentManaged(configuration, GovernanceSettingKeys.Authentication.KeycloakAuthority)
            || IsDeploymentManaged(configuration, SecretDefinitionRegistry.Keys.Keycloak.Endpoint)
            || IsDeploymentManaged(configuration, "Keycloak:Authority");
        var clientIdDeploymentManaged = IsDeploymentManaged(configuration, GovernanceSettingKeys.Authentication.KeycloakClientId)
            || IsDeploymentManaged(configuration, SecretDefinitionRegistry.Keys.Keycloak.ClientId)
            || IsDeploymentManaged(configuration, "Keycloak:ClientId");

        if (authorityDeploymentManaged || clientIdDeploymentManaged)
        {
            var effectiveAuthority = authorityDeploymentManaged
                ? deploymentAuthority
                : string.IsNullOrWhiteSpace(storedAuthority)
                    ? deploymentAuthority
                    : storedAuthority;
            var effectiveClientId = clientIdDeploymentManaged
                ? deploymentClientId
                : string.IsNullOrWhiteSpace(storedClientId)
                    ? deploymentClientId
                    : storedClientId;
            var effectiveUsable = !string.IsNullOrWhiteSpace(effectiveAuthority)
                                  && !string.IsNullOrWhiteSpace(effectiveClientId);
            var enabled = primaryProvider == AuthenticationProviderKind.Keycloak
                          && effectiveUsable;
            var usesDeploymentMetadata = authorityDeploymentManaged
                                         || clientIdDeploymentManaged
                                         || string.IsNullOrWhiteSpace(storedAuthority)
                                         || string.IsNullOrWhiteSpace(storedClientId);

            return (enabled, effectiveAuthority, effectiveClientId, enabled && usesDeploymentMetadata);
        }

        if (storedUsable)
        {
            return (
                primaryProvider == AuthenticationProviderKind.Keycloak,
                storedAuthority,
                storedClientId,
                false);
        }

        return deploymentUsable
            ? (
                primaryProvider == AuthenticationProviderKind.Keycloak,
                deploymentAuthority,
                deploymentClientId,
                true)
            : (false, storedAuthority, storedClientId, false);
    }

    private AuthenticationProviderKind ResolvePrimaryProvider(SystemSetting? storedSetting)
    {
        string? deploymentProvider = ReadFirstConfigured(
            "Authentication:Provider",
            "AUTHENTICATION_PROVIDER");
        if (!string.IsNullOrWhiteSpace(deploymentProvider))
        {
            return RequireSupportedPrimaryProvider(
                (int)deploymentProvider.ParseAuthenticationProviderKind());
        }

        if (string.IsNullOrWhiteSpace(storedSetting?.Value))
        {
            return AuthenticationProviderKind.Local;
        }

        try
        {
            int providerId = JsonSerializer.Deserialize<int>(storedSetting.Value);
            return RequireSupportedPrimaryProvider(providerId);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The persisted primary authentication provider is invalid.",
                exception);
        }
    }

    private static AuthenticationProviderKind RequireSupportedPrimaryProvider(int providerId)
    {
        AuthenticationProviderKind provider = (AuthenticationProviderKind)providerId;
        return provider is AuthenticationProviderKind.Local
            or AuthenticationProviderKind.Keycloak
            or AuthenticationProviderKind.Atproto
            ? provider
            : throw new InvalidOperationException(
                "Primary authentication provider must be Local Identity, Keycloak, or AT Protocol.");
    }

    private bool IsPrimaryProviderDeploymentManaged()
    {
        return !string.IsNullOrWhiteSpace(ReadFirstConfigured(
                   "Authentication:Provider",
                   "AUTHENTICATION_PROVIDER"))
               || IsDeploymentManaged(GovernanceSettingKeys.Authentication.PrimaryProviderId)
               || IsDeploymentManaged("Authentication:Provider");
    }

    private static string GetProviderDisplayName(AuthenticationProviderKind provider) =>
        provider switch
        {
            AuthenticationProviderKind.Local => "Local Identity",
            AuthenticationProviderKind.Keycloak => "Keycloak",
            AuthenticationProviderKind.Atproto => "AT Protocol",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };

    private bool IsKeycloakAuthorityDeploymentManaged()
    {
        return IsDeploymentManaged(GovernanceSettingKeys.Authentication.KeycloakAuthority)
               || IsDeploymentManaged(SecretDefinitionRegistry.Keys.Keycloak.Endpoint)
               || IsDeploymentManaged("Keycloak:Authority");
    }

    private bool IsKeycloakClientIdDeploymentManaged()
    {
        return IsDeploymentManaged(GovernanceSettingKeys.Authentication.KeycloakClientId)
               || IsDeploymentManaged(SecretDefinitionRegistry.Keys.Keycloak.ClientId)
               || IsDeploymentManaged("Keycloak:ClientId");
    }

    private bool IsKeycloakClientSecretDeploymentManaged()
    {
        return IsDeploymentManaged(InfrastructureSecretSettingKeys.Authentication.KeycloakClientSecret)
               || IsDeploymentManaged(SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret)
               || IsDeploymentManaged("Keycloak:ClientSecret")
               || IsDeploymentManaged("Keycloak:BlazorClientSecret")
               || IsDeploymentManaged("Authentication:Keycloak:ClientSecret");
    }

    private string ReadConfiguredKeycloakClientSecret()
    {
        return ReadFirstConfigured(
            "Keycloak:ClientSecret",
            "Keycloak:BlazorClientSecret",
            "Authentication:Keycloak:ClientSecret",
            "KEYCLOAK_BLAZOR_CLIENT_SECRET");
    }

    private string ReadFirstConfigured(params string[] keys)
    {
        return keys
            .Select(key => _configuration[key])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.Trim() ?? string.Empty;
    }

    private bool IsDeploymentManaged(string key) => IsDeploymentManaged(_configuration, key);

    private static bool IsDeploymentManaged(IConfiguration configuration, string key)
    {
        var configuredKeys = configuration.GetSection("Secrets:Ownership:DeploymentManagedKeys")
            .GetChildren()
            .Select(section => section.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        return configuredKeys.Any(candidate =>
            candidate.Equals("*", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private static DTOs.Secrets.SecretOwnershipDto CreateOwnershipMetadata(
        bool deploymentManaged,
        bool configured,
        bool bootstrapAvailable,
        string applicationManagedDescription,
        string deploymentManagedDescription)
    {
        if (deploymentManaged)
        {
            return new DTOs.Secrets.SecretOwnershipDto
            {
                Mode = "deployment-managed",
                Source = "deployment",
                Badge = "Managed by Deployment",
                Description = deploymentManagedDescription,
                Editable = false,
                Configured = configured,
                BootstrapAvailable = false
            };
        }

        return new DTOs.Secrets.SecretOwnershipDto
        {
            Mode = "application-managed",
            Source = bootstrapAvailable ? "deployment-bootstrap" : "application",
            Badge = bootstrapAvailable ? "Bootstrap from Deployment" : "Managed by Application",
            Description = bootstrapAvailable
                ? "This secret was detected from deployment configuration. If you rotate it from the Admin UI, saved application settings will be used from then on."
                : applicationManagedDescription,
            Editable = true,
            Configured = configured,
            BootstrapAvailable = bootstrapAvailable
        };
    }

    private static PublicOnboardingPolicy ReadOnboardingPolicy(string? rawValue) =>
        Enum.TryParse(DeserializeString(rawValue, string.Empty), out PublicOnboardingPolicy policy) && Enum.IsDefined(policy)
            ? policy : PublicOnboardingPolicy.Unknown;

    private static bool DeserializeBoolean(string? rawValue, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return defaultValue;

        try
        {
            return JsonSerializer.Deserialize<bool>(rawValue);
        }
        catch
        {
            return bool.TryParse(rawValue, out var parsed) ? parsed : defaultValue;
        }
    }

    private static string DeserializeString(string? rawValue, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return defaultValue;

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(rawValue);
            return string.IsNullOrWhiteSpace(deserialized) ? defaultValue : deserialized;
        }
        catch
        {
            return rawValue.Trim('"');
        }
    }
}
