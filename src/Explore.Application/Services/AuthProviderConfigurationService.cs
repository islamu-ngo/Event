using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.DTOs.Instance;
using Explore.Application.Models;
using Explore.Domain.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Services;

public class AuthProviderConfigurationService : IAuthProviderConfigurationService
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IConfiguration _configuration;
    private readonly IPolicyMutationExecutionScopeFactory _scopeFactory;
    private readonly KeycloakConnectionResolver _keycloakConnectionResolver;
    private readonly ISecretResolver _secretResolver;

    public AuthProviderConfigurationService(
        ISystemSettingRepository systemSettingRepository,
        IConfiguration configuration,
        IPolicyMutationExecutionScopeFactory scopeFactory,
        KeycloakConnectionResolver keycloakConnectionResolver,
        ISecretResolver secretResolver)
    {
        _systemSettingRepository = systemSettingRepository;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _keycloakConnectionResolver = keycloakConnectionResolver;
        _secretResolver = secretResolver;
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
        AuthenticationProviderKind primary;
        if (!string.IsNullOrWhiteSpace(deploymentProvider))
        {
            primary = deploymentProvider.ParseAuthenticationProviderKind();
        }
        else
        {
            string stored = Stored(GovernanceSettingKeys.Authentication.PrimaryProviderId);
            if (string.IsNullOrWhiteSpace(stored) || stored is "null" or "\"\"")
            {
                primary = AuthenticationProviderKind.Local;
            }
            else
            {
                int id = JsonSerializer.Deserialize<int>(stored);
                primary = id > 0 ? RequireSupportedPrimaryProvider(id) : AuthenticationProviderKind.Local;
            }
        }
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
        KeycloakConnectionResolution connection =
            await _keycloakConnectionResolver.ResolveRuntimeAsync();
        return await ReadConfigurationCoreAsync(new(connection.Status, connection.Authority, connection.BlazorClientId));
    }

    private async Task<AuthProviderConfigurationDto> ReadConfigurationCoreAsync(
        ProviderKeycloakMaterial connection, ProviderConfigurationState? original = null,
        IConfiguration? deployment = null, SecretResolutionStatus? googleOriginal = null,
        ProviderAuthenticationMaterialRequirements? requirements = null)
    {
        Task<SystemSetting?> Read(string key) => original is null
            ? _systemSettingRepository.GetByKey(key) : Task.FromResult(original.System[key]?.ToEntity());
        var configuration = deployment ?? _configuration;
        var primaryProviderSetting = await Read(
            GovernanceSettingKeys.Authentication.PrimaryProviderId);
        AuthenticationProviderKind primaryProvider = requirements?.OriginalPrimary
            ?? ResolvePrimaryProvider(primaryProviderSetting, configuration);
        var keycloak = ProjectKeycloakConfiguration(primaryProvider,
            await Read(GovernanceSettingKeys.Authentication.KeycloakAuthority),
            await Read(GovernanceSettingKeys.Authentication.KeycloakClientId), configuration);
        if (connection.Status == KeycloakConnectionStatus.Resolved)
        {
            keycloak = (
                primaryProvider == AuthenticationProviderKind.Keycloak,
                connection.Authority!.AbsoluteUri.TrimEnd('/'),
                connection.ClientId!,
                true);
        }
        var atprotoLoginEnabled = await Read(GovernanceSettingKeys.Authentication.AtprotoLoginEnabled);
        var atprotoPublicUrl = await Read(GovernanceSettingKeys.Authentication.AtprotoPublicUrl);
        var googleSsoEnabled = await Read(GovernanceSettingKeys.Authentication.GoogleSsoEnabled);
        var googleClientId = await Read(GovernanceSettingKeys.Authentication.GoogleClientId);
        SecretResolutionStatus googleStatus = googleOriginal ?? (await _secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret, tenantId: null)).Status;
        return new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)primaryProvider,
            PrimaryProviderCode = primaryProvider.ToAuthenticationProviderCode(),
            PrimaryProviderName = GetProviderDisplayName(primaryProvider),
            LockPrimaryProvider = IsPrimaryProviderDeploymentManaged(configuration)
                                  || primaryProviderSetting?.IsLocked == true,
            KeycloakAuthority = keycloak.Authority,
            KeycloakClientId = keycloak.ClientId,
            KeycloakDetectedFromEnvironment = keycloak.DetectedFromEnvironment,
            KeycloakClientSecretOwnership = CreateOwnershipMetadata(
                deploymentManaged: true,
                configured: connection.Status == KeycloakConnectionStatus.Resolved,
                bootstrapAvailable: false,
                applicationManagedDescription: "Keycloak runtime credentials must be bound through the selected deployment secret authority.",
                deploymentManagedDescription: "Keycloak client credentials are deployment-owned. Event never stores or rotates an existing client secret.")
                with
            { ResolutionStatus = connection.Status.ToString() },
            AtprotoLoginEnabled = primaryProvider == AuthenticationProviderKind.Atproto
                                  || DeserializeBoolean(atprotoLoginEnabled?.Value, false),
            AtprotoPublicUrl = DeserializeString(atprotoPublicUrl?.Value, string.Empty),
            GoogleSsoEnabled = requirements?.OriginalGoogleEnabled ?? (primaryProvider != AuthenticationProviderKind.Atproto
                && DeserializeBoolean(googleSsoEnabled?.Value, false)),
            GoogleClientId = DeserializeString(googleClientId?.Value, string.Empty),
            GoogleClientSecretOwnership = CreateOwnershipMetadata(
                deploymentManaged: true,
                configured: googleStatus == SecretResolutionStatus.Resolved,
                bootstrapAvailable: false,
                applicationManagedDescription: "Google runtime credentials must be bound through the selected deployment secret authority.",
                deploymentManagedDescription: "Google client credentials are deployment-owned. Configure and rotate them through the selected secret authority.")
                with
            { ResolutionStatus = googleStatus.ToString() },
            LockAtprotoLoginEnabled = atprotoLoginEnabled?.IsLocked == true,
            LockGoogleSsoEnabled = googleSsoEnabled?.IsLocked == true,
            KeycloakPublicOnboardingPolicy = ReadOnboardingPolicy((await Read(GovernanceSettingKeys.Authentication.KeycloakPublicOnboardingPolicy))?.Value),
            KeycloakPublicSignupUrl = DeserializeString((await Read(GovernanceSettingKeys.Authentication.KeycloakPublicSignupUrl))?.Value, string.Empty),
            GooglePublicOnboardingPolicy = ReadOnboardingPolicy((await Read(GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy))?.Value),
            GooglePublicSignupUrl = DeserializeString((await Read(GovernanceSettingKeys.Authentication.GooglePublicSignupUrl))?.Value, string.Empty),
        };
    }

    public async Task ApplyConfigurationAsync(UpdateAuthProviderConfigurationCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var observation = await ObserveConfigurationAsync(command, cancellationToken);
        await ApplyConfigurationAsync(observation, cancellationToken);
    }

    public async Task ApplySetupConfigurationAsync(UpdateAuthProviderConfigurationDuringSetupCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var observation = await ObserveConfigurationAsync(command, cancellationToken);
        await ApplyConfigurationAsync(observation, cancellationToken);
    }

    public Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthProviderConfigurationCommand command, CancellationToken token) =>
        ObserveConfigurationCoreAsync(command, token);

    public Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthProviderConfigurationDuringSetupCommand command, CancellationToken token) =>
        ObserveConfigurationCoreAsync(command, token);

    public Task ApplyConfigurationAsync(ProviderConfigurationMutation observation, CancellationToken token) =>
        observation.ExecuteAsync(this, token);

    private async Task<ProviderConfigurationMutation> ObserveConfigurationCoreAsync(object command, CancellationToken token)
    {
        var observation = await ProviderConfigurationMutation.ObserveAsync(command, this, _scopeFactory, token);
        try
        {
            var core = observation.Services.GetRequiredService<AuthProviderConfigurationService>();
            var materials = await observation.Source.ObserveMaterialsAsync(observation.Before, token);
            var original = await core.ReadConfigurationCoreAsync(materials.Keycloak, observation.Before,
                observation.Source.Deployment, materials.Google, materials.Authentication);
            var rows = await core.CompileConfigurationAsync(observation.AuthenticationPatch,
                observation.Source.Deployment, observation.OccurredAt, token);
            observation.CompileAuthentication(original, rows);
            await observation.Services.GetRequiredService<IProviderConfigurationSettingsWriter>()
                .RequireUnchangedAsync(observation.Before, token);
            return observation;
        }
        catch { await observation.DisposeAsync(); throw; }
    }

    /// <summary>Uses the original complete state and requested group once to select credential relevance.</summary>
    public ProviderAuthenticationMaterialRequirements DescribeMaterialRequirements(ProviderConfigurationState original,
        AuthProviderConfigurationWriteDto requested, IConfiguration deployment)
    {
        var primary = ResolvePrimaryProvider(original.System[GovernanceSettingKeys.Authentication.PrimaryProviderId]?.ToEntity(), deployment);
        bool googleEnabled = primary != AuthenticationProviderKind.Atproto
            && DeserializeBoolean(original.System[GovernanceSettingKeys.Authentication.GoogleSsoEnabled]?.Value, false);
        return new(primary, googleEnabled,
            primary == AuthenticationProviderKind.Keycloak || requested.PrimaryProviderId == (int)AuthenticationProviderKind.Keycloak,
            googleEnabled || requested.PrimaryProviderId != (int)AuthenticationProviderKind.Atproto && requested.GoogleSsoEnabled);
    }

    private async Task<IReadOnlyList<SystemSetting>> CompileConfigurationAsync(
        AuthProviderConfigurationWriteDto configuration, IConfiguration deployment, DateTime occurred,
        CancellationToken cancellationToken)
    {
        var writes = new List<SystemSetting>();
        Task UpsertSettingAsync(string key, string value, SettingValueType valueType, bool isLocked,
            string category, int displayOrder, string description, CancellationToken token)
        {
            writes.Add(new SystemSetting
            {
                SettingKey = key,
                Value = value,
                ValueType = valueType,
                IsLocked = isLocked,
                Category = category,
                DisplayOrder = displayOrder,
                Description = description,
                CreatedAt = occurred,
                UpdatedAt = occurred
            });
            return Task.CompletedTask;
        }
        // Validation of the retained proposed projection precedes every write. Keeping
        // the supplied value here lets the actual handler retain its typed validation result.
        AuthenticationProviderKind primaryProvider = (AuthenticationProviderKind)configuration.PrimaryProviderId;
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

        if (!IsKeycloakAuthorityDeploymentManaged(deployment))
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

        if (!IsKeycloakClientIdDeploymentManaged(deployment))
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

        foreach (var (key, value) in new[] {
            (GovernanceSettingKeys.Authentication.KeycloakPublicOnboardingPolicy, configuration.KeycloakPublicOnboardingPolicy.ToString()),
            (GovernanceSettingKeys.Authentication.KeycloakPublicSignupUrl, configuration.KeycloakPublicSignupUrl),
            (GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy, configuration.GooglePublicOnboardingPolicy.ToString()),
            (GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, configuration.GooglePublicSignupUrl) })
            await UpsertSettingAsync(key, JsonSerializer.Serialize(value), SettingValueType.String, true,
                "Authentication", 0, "Public provider onboarding policy", cancellationToken);

        return writes;
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

    private AuthenticationProviderKind ResolvePrimaryProvider(SystemSetting? storedSetting, IConfiguration? deployment = null)
    {
        string? deploymentProvider = new[] { "Authentication:Provider", "AUTHENTICATION_PROVIDER" }
            .Select(key => (deployment ?? _configuration)[key]).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        if (!string.IsNullOrWhiteSpace(deploymentProvider))
        {
            return RequireSupportedPrimaryProvider(
                (int)deploymentProvider.ParseAuthenticationProviderKind());
        }

        if (string.IsNullOrWhiteSpace(storedSetting?.Value) || storedSetting.Value is "null" or "\"\"")
        {
            return AuthenticationProviderKind.Local;
        }

        try
        {
            int providerId = JsonSerializer.Deserialize<int>(storedSetting.Value);
            return providerId > 0
                ? RequireSupportedPrimaryProvider(providerId)
                : AuthenticationProviderKind.Local;
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

    private bool IsPrimaryProviderDeploymentManaged(IConfiguration? deployment = null)
    {
        var configuration = deployment ?? _configuration;
        return new[] { "Authentication:Provider", "AUTHENTICATION_PROVIDER" }.Any(key => !string.IsNullOrWhiteSpace(configuration[key]))
               || IsDeploymentManaged(configuration, GovernanceSettingKeys.Authentication.PrimaryProviderId)
               || IsDeploymentManaged(configuration, "Authentication:Provider");
    }

    private static string GetProviderDisplayName(AuthenticationProviderKind provider) =>
        provider switch
        {
            AuthenticationProviderKind.Local => "Local Identity",
            AuthenticationProviderKind.Keycloak => "Keycloak",
            AuthenticationProviderKind.Atproto => "AT Protocol",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };

    private bool IsKeycloakAuthorityDeploymentManaged(IConfiguration? deployment = null)
    {
        var configuration = deployment ?? _configuration;
        return IsDeploymentManaged(configuration, GovernanceSettingKeys.Authentication.KeycloakAuthority)
               || IsDeploymentManaged(configuration, SecretDefinitionRegistry.Keys.Keycloak.Endpoint)
               || IsDeploymentManaged(configuration, "Keycloak:Authority");
    }

    private bool IsKeycloakClientIdDeploymentManaged(IConfiguration? deployment = null)
    {
        var configuration = deployment ?? _configuration;
        return IsDeploymentManaged(configuration, GovernanceSettingKeys.Authentication.KeycloakClientId)
               || IsDeploymentManaged(configuration, SecretDefinitionRegistry.Keys.Keycloak.ClientId)
               || IsDeploymentManaged(configuration, "Keycloak:ClientId");
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
