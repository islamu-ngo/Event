using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceOnboarding.Common;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Models.PublicExperience;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public sealed class InstanceOnboardingCompletionOperation(
    IInstanceBootstrapStateRepository bootstrapRepository,
    IPlatformUserRoleRepository platformRoleRepository,
    ITenantUserRoleGrantRepository tenantRoleRepository,
    ITenantUserRepository tenantUserRepository,
    IRoleRepository roleRepository,
    IUserRepository userRepository,
    IActorRepository actorRepository,
    IUserExternalLoginRepository externalLoginRepository,
    ITenantRepository tenantRepository,
    ITenantCreationService tenantCreationService,
    ITenantSettingsDocumentRepository tenantSettingsRepository,
    ISystemSettingRepository systemSettingRepository,
    IEnumerable<IConfiguredAdministratorBootstrapProvider> configuredProviders,
    ISetupSecretProvider setupSecretProvider,
    IInstanceBootstrapAuditLogger auditLogger,
    IAdminCacheInvalidator cacheInvalidator,
    IDeploymentModeProvider deploymentModeProvider,
    IJwtAuthorityRefreshNotifier jwtRefreshNotifier,
    ITenantBrandingSettingsDocumentProvisioningService brandingProvisioner,
    ILogger<InstanceOnboardingCompletionOperation> logger,
    IUnitOfWork unitOfWork)
{
    public Task<BaseCommandResponse<Guid>> CompleteInteractiveAsync(
        CompleteInstanceOnboardingCommand command,
        DeploymentMode deploymentMode,
        SelfHostOnboardingProfileDto siteProfile,
        CancellationToken cancellationToken) =>
        CompleteAsync(CompletionInput.Interactive(command, deploymentMode, siteProfile), cancellationToken);

    public Task<BaseCommandResponse<Guid>> ClaimConfiguredAsync(
        ClaimConfiguredInstanceAdministratorCommand command,
        CancellationToken cancellationToken) =>
        CompleteAsync(CompletionInput.Configured(command), cancellationToken);

    private async Task<BaseCommandResponse<Guid>> CompleteAsync(
        CompletionInput input,
        CancellationToken cancellationToken)
    {
        if (input.ConfiguredCommand is not null && input.UserId == Guid.Empty)
        {
            return ConfiguredFailure(
                "configured_administrator_identity_incomplete",
                "Configured administrator identity is not ready.");
        }

        PersistenceOutcome outcome;
        try
        {
            outcome = await unitOfWork.ExecuteSerializableAsync(
                token => PersistAsync(input, token),
                cancellationToken);
        }
        catch (TenantDirectoryOperatorIdentityReadinessException exception)
        {
            return BaseCommandResponse.Failure<Guid>(
                exception.FailureCode,
                exception.Message,
                exception.ReasonCodes);
        }

        if (!outcome.RequiresPostCommitReconciliation)
        {
            return outcome.Response;
        }

        setupSecretProvider.Lock();
        cacheInvalidator.InvalidateUser(input.UserId);
        await deploymentModeProvider.InvalidateCacheAsync();
        await jwtRefreshNotifier.ReloadAsync(CancellationToken.None);
        auditLogger.Log(new InstanceBootstrapAuditEvent(
            InstanceBootstrapAuditEventType.SetupModeDisabled,
            Operation: outcome.AuditOperation,
            Outcome: "disabled",
            ActorUserId: input.UserId,
            DeploymentMode: outcome.DeploymentMode.ToString()));
        return outcome.Response;
    }

    private async Task<PersistenceOutcome> PersistAsync(
        CompletionInput input,
        CancellationToken cancellationToken)
    {
        InstanceBootstrapState? bootstrap =
            await bootstrapRepository.GetCurrentForUpdate(cancellationToken);
        Admission admission = input.ConfiguredCommand is null
            ? AdmitInteractive(input, bootstrap)
            : await AdmitConfiguredAsync(input, bootstrap, cancellationToken);

        if (admission.Response is not null)
        {
            return new(
                admission.Response,
                admission.ReconcilePostCommit,
                admission.DeploymentMode,
                admission.AuditOperation);
        }

        ConfiguredAdministratorProfile? administratorProfile =
            admission.Binding?.AdministratorProfile;
        User? user = await userRepository.GetById(input.UserId);
        if (user is null
            && string.IsNullOrWhiteSpace(administratorProfile?.Email ?? input.Email))
        {
            BaseCommandResponse<Guid> response = input.ConfiguredCommand is null
                ? BaseCommandResponse.Validation<Guid>(
                    ["No user found and no email claim available to create one."],
                    "User identity data is required to complete onboarding.")
                : ConfiguredFailure(
                    "configured_administrator_identity_incomplete",
                    "Configured administrator identity is not ready.");
            return new(response, false, admission.DeploymentMode, admission.AuditOperation);
        }

        CompleteInstanceOnboardingRequest settings = admission.Settings!;
        bool singleTenant = admission.DeploymentMode == DeploymentMode.SingleTenant;
        Guid? defaultTenantId = null;
        if (singleTenant)
        {
            Tenant tenant = await EnsureDefaultTenantAsync(
                settings.DirectoryOperatorIdentity!,
                admission.SiteProfile!.SiteName,
                input.UserId,
                cancellationToken);
            defaultTenantId = tenant.Id;
        }

        user ??= await CreateUserAsync(input, administratorProfile, defaultTenantId);
        await PersistDeploymentModeAsync(admission.DeploymentMode);
        await PersistSiteProfileAsync(admission.SiteProfile!, singleTenant, cancellationToken);
        await PersistAdministrationAccessAsync(settings, singleTenant);
        await EnsurePlatformAdministratorAsync(input.UserId);
        logger.LogInformation("Onboarding: Assigned Platform Admin role");

        if (singleTenant && defaultTenantId.HasValue)
        {
            await brandingProvisioner.EnsureTenantBrandingDocumentAsync(
                defaultTenantId.Value,
                admission.SiteProfile!.SiteName,
                cancellationToken);
            await EnsureDefaultTenantAdministratorAsync(defaultTenantId.Value, user);
            logger.LogInformation("Onboarding: Assigned Tenant Admin role for default tenant");
        }

        if (input.ConfiguredCommand is null)
        {
            if (bootstrap is null)
            {
                bootstrap = InstanceBootstrapState.CreateInteractivePending(
                    input.BootstrapId,
                    admission.DeploymentMode,
                    input.CompletedAt);
                bootstrap.CompleteInteractive(input.UserId, input.CompletedAt);
                bootstrap = await bootstrapRepository.Create(bootstrap);
            }
            else
            {
                bootstrap.CompleteInteractive(input.UserId, input.CompletedAt);
                await bootstrapRepository.Update(bootstrap);
            }
        }
        else
        {
            ConfiguredAdministratorBootstrapBinding binding = admission.Binding!;
            bootstrap!.CompleteConfiguredAdministrator(
                binding.AccountKey.ProviderKind,
                binding.Generation,
                binding.IdentityFingerprint,
                input.UserId,
                input.CompletedAt);
            await bootstrapRepository.Update(bootstrap);
        }

        string message = input.ConfiguredCommand is null
            ? "Instance onboarding completed successfully."
            : "Configured instance administrator claimed successfully.";
        return new(
            BaseCommandResponse.Success(bootstrap.Id, message),
            true,
            admission.DeploymentMode,
            admission.AuditOperation);
    }

    private static Admission AdmitInteractive(
        CompletionInput input,
        InstanceBootstrapState? bootstrap)
    {
        if (bootstrap?.Status == InstanceBootstrapStatus.Completed)
        {
            const string message = "Instance onboarding has already been completed.";
            return Admission.Terminal(
                BaseCommandResponse.Validation<Guid>([message], message),
                input.DeploymentMode,
                "instance_onboarding_complete");
        }

        if (bootstrap is not null
            && (bootstrap.Status != InstanceBootstrapStatus.Pending
                || bootstrap.Mode != InstanceBootstrapMode.Interactive))
        {
            const string message = "Instance onboarding is not available for interactive completion.";
            return Admission.Terminal(
                BaseCommandResponse.Validation<Guid>([message], message),
                input.DeploymentMode,
                "instance_onboarding_complete");
        }

        return Admission.Allow(
            input.Settings!,
            input.SiteProfile!,
            input.DeploymentMode,
            "instance_onboarding_complete");
    }

    private async Task<Admission> AdmitConfiguredAsync(
        CompletionInput input,
        InstanceBootstrapState? bootstrap,
        CancellationToken cancellationToken)
    {
        IConfiguredAdministratorBootstrapProvider? provider = configuredProviders.SingleOrDefault();
        if (provider is null)
        {
            return ConfiguredTerminal(
                "configured_administrator_provider_unavailable",
                "Configured administrator claim is unavailable.");
        }

        ClaimConfiguredInstanceAdministratorCommand command = input.ConfiguredCommand!;
        ConfiguredAdministratorBootstrapBinding? binding =
            await provider.GetVerifiedBindingAsync(command.AuthenticatedAccount, cancellationToken);
        if (binding is null || binding.AccountKey != command.AuthenticatedAccount)
        {
            return ConfiguredTerminal(
                "configured_administrator_claim_mismatch",
                "Configured administrator claim did not match.");
        }

        if (bootstrap?.Status == InstanceBootstrapStatus.Completed)
        {
            bool sameClaim = bootstrap.Mode == InstanceBootstrapMode.ConfiguredAdministrator
                && bootstrap.ProviderKind == binding.AccountKey.ProviderKind
                && bootstrap.Generation == binding.Generation
                && bootstrap.CompletedByUserId == input.UserId
                && string.Equals(
                    bootstrap.CompletedIdentityFingerprint,
                    binding.IdentityFingerprint,
                    StringComparison.Ordinal);
            return sameClaim
                ? Admission.Terminal(
                    BaseCommandResponse.Success(
                        bootstrap.Id,
                        "Configured instance administrator already claimed."),
                    binding.Settings.DeploymentMode,
                    "configured_instance_administrator_claim",
                    reconcilePostCommit: true)
                : ConfiguredTerminal(
                    "configured_administrator_claim_conflict",
                    "Configured administrator claim conflicts with completed onboarding.");
        }

        if (bootstrap is null
            || bootstrap.Status != InstanceBootstrapStatus.Pending
            || bootstrap.Mode != InstanceBootstrapMode.ConfiguredAdministrator
            || bootstrap.ProviderKind != binding.AccountKey.ProviderKind
            || bootstrap.ProviderKind != command.AuthenticatedAccount.ProviderKind
            || bootstrap.Generation != binding.Generation
            || !string.Equals(
                bootstrap.SelectorFingerprint,
                binding.IdentityFingerprint,
                StringComparison.Ordinal))
        {
            return ConfiguredTerminal(
                "configured_administrator_claim_mismatch",
                "Configured administrator claim did not match.");
        }

        CompleteInstanceOnboardingRequest settings = binding.Settings;
        if (string.IsNullOrWhiteSpace(settings.SiteProfile.SiteName)
            && !string.IsNullOrWhiteSpace(settings.InstanceName))
        {
            settings.SiteProfile.SiteName = settings.InstanceName;
        }

        if (settings.DeploymentMode == DeploymentMode.SingleTenant
            && settings.DirectoryOperatorIdentity is null)
        {
            return ConfiguredTerminal(
                "configured_administrator_configuration_incomplete",
                "Configured administrator onboarding is not ready.");
        }

        var validator = new CompleteInstanceOnboardingRequestValidator();
        var validation = await validator.ValidateAsync(settings, cancellationToken);
        if (!validation.IsValid)
        {
            return ConfiguredTerminal(
                "configured_administrator_configuration_invalid",
                "Configured administrator onboarding is invalid.");
        }

        SelfHostOnboardingProfileDto siteProfile =
            InstanceOnboardingProfileSettingHelpers.Normalize(
                settings.SiteProfile,
                settings.InstanceName);
        return Admission.Allow(
            settings,
            siteProfile,
            settings.DeploymentMode,
            "configured_instance_administrator_claim",
            binding);
    }

    private static Admission ConfiguredTerminal(string code, string message) =>
        Admission.Terminal(
            ConfiguredFailure(code, message),
            default,
            "configured_instance_administrator_claim");

    private static BaseCommandResponse<Guid> ConfiguredFailure(string code, string message) =>
        BaseCommandResponse.Failure<Guid>(code, message);

    private async Task<User> CreateUserAsync(
        CompletionInput input,
        ConfiguredAdministratorProfile? administratorProfile,
        Guid? tenantId)
    {
        string email = (administratorProfile?.Email ?? input.Email!).Trim().ToLowerInvariant();
        string? suppliedFirstName = administratorProfile?.FirstName ?? input.FirstName;
        string? suppliedLastName = administratorProfile?.LastName ?? input.LastName;
        string firstName = string.IsNullOrWhiteSpace(suppliedFirstName)
            ? "User"
            : suppliedFirstName.Trim();
        string lastName = string.IsNullOrWhiteSpace(suppliedLastName)
            ? string.Empty
            : suppliedLastName.Trim();
        AuthenticationProviderKind providerKind;
        string provider;
        string providerKey;
        bool emailVerified;

        if (input.ConfiguredCommand is not null)
        {
            providerKind = input.ConfiguredCommand.AuthenticatedAccount.ProviderKind;
            provider = providerKind.ToAuthenticationProviderCode();
            providerKey = input.ConfiguredCommand.AuthenticatedAccount.Value;
            emailVerified = false;
        }
        else
        {
            CompleteInstanceOnboardingCommand command = input.InteractiveCommand!;
            string providerCode = string.IsNullOrWhiteSpace(command.AuthProvider)
                ? AuthSchemeNames.Keycloak.ToLowerInvariant()
                : command.AuthProvider.Trim().ToLowerInvariant();
            providerKind = providerCode.ParseAuthenticationProviderKind();
            provider = providerKind.ToAuthenticationProviderCode();
            providerKey = string.IsNullOrWhiteSpace(command.AuthProviderId)
                ? input.UserId.ToString()
                : command.AuthProviderId.Trim();
            emailVerified = input.EmailVerified ?? (provider is "keycloak" or "google");
        }

        var user = new User
        {
            Id = input.UserId,
            Pii = new UserPii
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName
            },
            EmailVerified = emailVerified
        };
        user = await userRepository.Create(user);

        await actorRepository.Create(new Actor
        {
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = $"{firstName} {lastName}".Trim() },
            Description = null,
            UserId = user.Id
        });
        await externalLoginRepository.Create(new UserExternalLogin
        {
            Id = input.ExternalLoginId,
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)providerKind,
            AuthenticationProvider = null!,
            ProviderKey = providerKey,
            ProviderDisplayName = provider
        });
        return user;
    }

    private async Task<Tenant> EnsureDefaultTenantAsync(
        TenantDirectoryOperatorIdentityInputDto identityInput,
        string displayName,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        Tenant? tenant = await tenantRepository.GetById(PlatformDefaults.DefaultTenantId);
        if (tenant is not null)
        {
            await UpsertDefaultTenantIdentityAsync(identityInput, actorUserId, cancellationToken);
            return tenant;
        }

        DateTime occurredAt = DateTime.UtcNow;
        TenantSettingsDocument branding = TenantBrandingSettingsDocumentDefaults.Create(
            PlatformDefaults.DefaultTenantId,
            displayName);
        TenantSettingsDocument identity = TenantDirectoryOperatorIdentityDocumentDefaults.Create(
            PlatformDefaults.DefaultTenantId,
            identityInput.ToPayload());
        TenantCreationOutcome outcome = await tenantCreationService.CreateInCurrentTransactionAsync(
            new TenantCreationRequest(
                PlatformDefaults.DefaultTenantId,
                PlatformDefaults.DefaultTenantName,
                PlatformDefaults.DefaultTenantSlug,
                (int)TenantStatusEnum.Active,
                actorUserId,
                occurredAt,
                new TenantBrandingDocumentSeed(
                    Guid.CreateVersion7(),
                    branding.SchemaVersion,
                    branding.DefaultsVersion,
                    branding.PayloadJson),
                new TenantDirectoryOperatorIdentityDocumentSeed(
                    Guid.CreateVersion7(),
                    identity.SchemaVersion,
                    identity.DefaultsVersion,
                    identity.PayloadJson)),
            cancellationToken);
        return outcome.Tenant;
    }

    private async Task UpsertDefaultTenantIdentityAsync(
        TenantDirectoryOperatorIdentityInputDto identityInput,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        TenantSettingsDocument replacement = TenantDirectoryOperatorIdentityDocumentDefaults.Create(
            PlatformDefaults.DefaultTenantId,
            identityInput.ToPayload());
        TenantSettingsDocument? existing =
            await tenantSettingsRepository.GetTrackedByTenantAndDocumentKey(
                PlatformDefaults.DefaultTenantId,
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                cancellationToken);
        DateTime changedAt = DateTime.UtcNow;
        if (existing is null)
        {
            replacement.Id = Guid.CreateVersion7();
            replacement.CreatedAt = changedAt;
            replacement.CreatedBy = actorUserId;
            await tenantSettingsRepository.Create(replacement);
            return;
        }

        existing.UpdatePayload(
            replacement.SchemaVersion,
            replacement.DefaultsVersion,
            replacement.PayloadJson);
        existing.UpdatedAt = changedAt;
        existing.UpdatedBy = actorUserId;
        await tenantSettingsRepository.Update(existing);
    }

    private async Task EnsurePlatformAdministratorAsync(Guid userId)
    {
        Role? role = await roleRepository.GetByMasterCodeAsync("platform.admin")
            ?? await roleRepository.GetByIdAsync((int)RoleEnum.Admin);
        if (role is null)
        {
            throw new InvalidOperationException(
                "Critical system error: Platform Admin role not found in database.");
        }
        if (role.Scope != RoleScopeEnum.Platform)
        {
            throw new InvalidOperationException(
                $"Critical system error: Role '{role.MasterCode}' has incorrect scope {role.Scope}. Expected Platform.");
        }
        if (await platformRoleRepository.GetByUserAndRole(userId, role.Id) is not null)
        {
            return;
        }

        await platformRoleRepository.Create(new PlatformUserRole
        {
            UserId = userId,
            User = null!,
            RoleId = role.Id,
            Role = null!,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = userId
        });
    }

    private Task PersistDeploymentModeAsync(DeploymentMode mode) =>
        systemSettingRepository.UpsertAsync(new SystemSetting
        {
            SettingKey = GovernanceSettingKeys.Deployment.Mode,
            Value = JsonSerializer.Serialize(mode.ToString()),
            ValueType = SettingValueType.String,
            IsLocked = true,
            Category = "System",
            DisplayOrder = 1,
            Description = "Deployment mode of the application",
            AllowedValues = "[\"SingleTenant\", \"MultiTenant\"]",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

    private async Task PersistSiteProfileAsync(
        SelfHostOnboardingProfileDto siteProfile,
        bool singleTenant,
        CancellationToken cancellationToken)
    {
        await InstanceOnboardingProfileSettingHelpers.PersistAsync(
            systemSettingRepository,
            siteProfile,
            cancellationToken);
        if (singleTenant)
        {
            await PersistSingleTenantPublicExperienceDefaultsAsync(siteProfile.SiteName);
        }
    }

    private async Task PersistAdministrationAccessAsync(
        CompleteInstanceOnboardingRequest settings,
        bool singleTenant)
    {
        if (singleTenant
            || !settings.AdministrationAccessMode.Equals(
                CompleteInstanceOnboardingRequest.DedicatedAdminHostAdministrationAccess,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? adminHost = NormalizeOptionalHost(settings.AdminHost);
        if (!string.IsNullOrWhiteSpace(adminHost))
        {
            await PersistSettingAsync(
                GovernanceSettingKeys.Domains.AdminHost,
                JsonSerializer.Serialize(adminHost),
                SettingValueType.String,
                "Domains",
                2,
                "Dedicated admin/control-plane host for multi-tenant operator access");
        }
    }

    private async Task PersistSingleTenantPublicExperienceDefaultsAsync(string siteName)
    {
        await PersistSettingAsync(
            GovernanceSettingKeys.PublicExperience.Mode,
            JsonSerializer.Serialize(PublicExperienceMode.DiscoveryCentric.ToString()),
            SettingValueType.String,
            "PublicExperience",
            1,
            "Anonymous public experience posture");
        await PersistSettingAsync(
            GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
            JsonSerializer.Serialize("Events"),
            SettingValueType.String,
            "PublicExperience",
            2,
            "Display label for the public event catalog entry point");
        await PersistSettingAsync(
            GovernanceSettingKeys.Routing.DefaultPublicHomePage,
            JsonSerializer.Serialize("EventList"),
            SettingValueType.String,
            "Routing",
            1,
            "Default anonymous public home page");

        var blocks = new PublicExperienceHomeBlocksConfig(
            Blocks: [new PublicExperienceHomeBlockConfig(
                Id: "hero",
                Kind: PublicExperienceHomeBlockKind.Hero,
                Title: siteName,
                Subtitle: "Discover upcoming events.",
                LinkText: "Browse events",
                LinkUrl: "/events",
                SortOrder: 0)]);
        await PersistSettingAsync(
            GovernanceSettingKeys.PublicExperience.HomeBlocks,
            JsonSerializer.Serialize(blocks),
            SettingValueType.Json,
            "PublicExperience",
            3,
            "Versioned public home block configuration document");

        var ctas = new PublicExperienceCtasConfig(
            Ctas: [new PublicExperienceCtaConfig(
                Id: "browse-events",
                Label: "Browse events",
                Url: "/events",
                Placement: PublicExperienceCtaPlacement.Hero,
                Style: PublicExperienceCtaStyle.Primary,
                SortOrder: 0)]);
        await PersistSettingAsync(
            GovernanceSettingKeys.PublicExperience.Ctas,
            JsonSerializer.Serialize(ctas),
            SettingValueType.Json,
            "PublicExperience",
            4,
            "Versioned public call-to-action configuration document");
    }

    private Task PersistSettingAsync(
        string key,
        string value,
        SettingValueType valueType,
        string category,
        int displayOrder,
        string description) =>
        systemSettingRepository.UpsertAsync(new SystemSetting
        {
            SettingKey = key,
            Value = value,
            ValueType = valueType,
            IsLocked = false,
            Category = category,
            DisplayOrder = displayOrder,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

    private static string? NormalizeOptionalHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return uri.Host.Trim().ToLowerInvariant();
        }
        return trimmed
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('/')
            .ToLowerInvariant();
    }

    private async Task EnsureDefaultTenantAdministratorAsync(Guid tenantId, User user)
    {
        TenantUser tenantUser = await EnsureActiveTenantUserAsync(tenantId, user);
        TenantUserRoleGrant? grant =
            await tenantRoleRepository.GetByTenantAndUser(tenantId, user.Id);
        Role? role = await roleRepository.GetByMasterCodeAsync("tenant.admin")
            ?? await roleRepository.GetByIdAsync((int)RoleEnum.TenantAdmin);
        if (role is null)
        {
            throw new InvalidOperationException(
                "Critical system error: Tenant Admin role not found in database.");
        }

        if (grant is null)
        {
            await tenantRoleRepository.Create(new TenantUserRoleGrant
            {
                TenantId = tenantId,
                Tenant = null!,
                TenantUserId = tenantUser.Id,
                TenantUser = null!,
                RoleId = role.Id,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow,
                GrantedBy = user.Id
            });
        }
        else if (grant.RoleId != role.Id)
        {
            grant.RoleId = role.Id;
            await tenantRoleRepository.Update(grant);
        }
    }

    private async Task<TenantUser> EnsureActiveTenantUserAsync(Guid tenantId, User user)
    {
        TenantUser? tenantUser = await tenantUserRepository.GetByTenantAndUserAsync(tenantId, user.Id);
        if (tenantUser is null)
        {
            Actor? actor = user.Actor ?? await actorRepository.GetActorByUserId(user.Id);
            return await tenantUserRepository.Create(new TenantUser
            {
                TenantId = tenantId,
                Tenant = null!,
                UserId = user.Id,
                User = null!,
                ActorId = actor?.Id,
                Actor = null,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = user.Id
            });
        }
        if (tenantUser.StatusId == (int)TenantUserStatusEnum.Active && !tenantUser.IsDeleted)
        {
            return tenantUser;
        }

        tenantUser.StatusId = (int)TenantUserStatusEnum.Active;
        tenantUser.IsDeleted = false;
        tenantUser.DeletedAt = null;
        tenantUser.DeletedBy = null;
        tenantUser.UpdatedAt = DateTime.UtcNow;
        tenantUser.UpdatedBy = user.Id;
        await tenantUserRepository.Update(tenantUser);
        return tenantUser;
    }

    private sealed record CompletionInput(
        CompleteInstanceOnboardingCommand? InteractiveCommand,
        ClaimConfiguredInstanceAdministratorCommand? ConfiguredCommand,
        CompleteInstanceOnboardingRequest? Settings,
        SelfHostOnboardingProfileDto? SiteProfile,
        DeploymentMode DeploymentMode,
        DateTime CompletedAt,
        Guid BootstrapId,
        Guid ExternalLoginId)
    {
        public Guid UserId => InteractiveCommand?.UserId ?? ConfiguredCommand!.UserId;
        public string? Email => InteractiveCommand?.Email ?? ConfiguredCommand?.Email;
        public string? FirstName => InteractiveCommand?.FirstName ?? ConfiguredCommand?.FirstName;
        public string? LastName => InteractiveCommand?.LastName ?? ConfiguredCommand?.LastName;
        public bool? EmailVerified => InteractiveCommand?.EmailVerified ?? ConfiguredCommand?.EmailVerified;

        public static CompletionInput Interactive(
            CompleteInstanceOnboardingCommand command,
            DeploymentMode mode,
            SelfHostOnboardingProfileDto profile) =>
            new(command, null, command.Settings, profile, mode, DateTime.UtcNow,
                Guid.CreateVersion7(), Guid.CreateVersion7());

        public static CompletionInput Configured(
            ClaimConfiguredInstanceAdministratorCommand command) =>
            new(null, command, null, null, default, DateTime.UtcNow,
                Guid.CreateVersion7(), Guid.CreateVersion7());
    }

    private sealed record Admission(
        CompleteInstanceOnboardingRequest? Settings,
        SelfHostOnboardingProfileDto? SiteProfile,
        DeploymentMode DeploymentMode,
        string AuditOperation,
        ConfiguredAdministratorBootstrapBinding? Binding,
        BaseCommandResponse<Guid>? Response,
        bool ReconcilePostCommit)
    {
        public static Admission Allow(
            CompleteInstanceOnboardingRequest settings,
            SelfHostOnboardingProfileDto profile,
            DeploymentMode mode,
            string operation,
            ConfiguredAdministratorBootstrapBinding? binding = null) =>
            new(settings, profile, mode, operation, binding, null, false);

        public static Admission Terminal(
            BaseCommandResponse<Guid> response,
            DeploymentMode mode,
            string operation,
            bool reconcilePostCommit = false) =>
            new(null, null, mode, operation, null, response, reconcilePostCommit);
    }

    private sealed record PersistenceOutcome(
        BaseCommandResponse<Guid> Response,
        bool RequiresPostCommitReconciliation,
        DeploymentMode DeploymentMode,
        string AuditOperation);
}
