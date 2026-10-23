using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;

namespace Explore.Application.Features.Management;

/// <summary>Noncredential source facts; this data cannot issue graph-writing authority.</summary>
public sealed record ManagedTenantWorkerAdministratorSource(
    Guid? UserId, Guid? ActorId, Guid? LoginId, bool? EmailVerified);

/// <summary>
/// Builds one closed graph from the complete original request and admitted source
/// data. Persistence separately proves issuance, original transaction and completion.
/// </summary>
public sealed class ManagedTenantWorkerGraphPlan
{
    private ManagedTenantWorkerGraphPlan(TenantCreationOutcome creation, User? user, Actor? actor,
        UserExternalLogin? login, bool upgradeEmail, List<object> effects,
        ManagedTenantWorkerCreationResult result)
    {
        Creation = creation;
        CreatedUser = user;
        CreatedActor = actor;
        CreatedLogin = login;
        UpgradeExistingEmailVerified = upgradeEmail;
        Effects = effects.AsReadOnly();
        Result = result;
    }

    public TenantCreationOutcome Creation { get; }
    public User? CreatedUser { get; }
    public Actor? CreatedActor { get; }
    public UserExternalLogin? CreatedLogin { get; }
    public bool UpgradeExistingEmailVerified { get; }
    public IReadOnlyList<object> Effects { get; }
    public ManagedTenantWorkerCreationResult Result { get; }

    public static ManagedTenantWorkerGraphPlan Build(ManagedTenantWorkerCreationSnapshot original,
        ManagedTenantProvisioningResolvedBootstrap bootstrap, ManagedTenantWorkerAdministratorSource administrator,
        Role tenantAdministrator, DateTime occurredAt)
    {
        var request = original.ReadRequest();
        bool local = request.Administrator.LocalIdentity is not null;
        var external = request.Administrator.ExternalIdentity;
        if (occurredAt.Kind != DateTimeKind.Utc || tenantAdministrator.MasterCode != "tenant.admin"
            || tenantAdministrator.Scope != RoleScopeEnum.Tenant
            || bootstrap.Plan.Key != request.Plan.Key || bootstrap.PlanVersion.Id != request.Plan.VersionId
            || bootstrap.PlanVersion.TenantPlanId != bootstrap.Plan.Id
            || !bootstrap.Modules.Select(row => row.ModuleKey).Order(StringComparer.Ordinal)
                .SequenceEqual(request.ApprovedModules.Order(StringComparer.Ordinal))
            || bootstrap.Modules.Any(row => !row.IsActive)
            || administrator.LoginId.HasValue != administrator.UserId.HasValue
            || administrator.ActorId.HasValue && !administrator.UserId.HasValue
            || local && (administrator.UserId != request.Administrator.LocalIdentity!.LocalSubjectId
                || administrator.ActorId is null || administrator.LoginId is null))
            throw new InvalidOperationException("managed_worker_creation_plan_invalid");

        Guid tenantId = Guid.CreateVersion7();
        var branding = TenantBrandingSettingsDocumentDefaults.Create(tenantId, request.TenantName);
        var directory = TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenantId,
            request.DirectoryOperatorIdentity!.ToPayload());
        var creation = TenantCreationService.Prepare(new(tenantId, request.TenantName, request.TenantSlug,
            (int)TenantStatusEnum.Active, null, occurredAt,
            new(Guid.CreateVersion7(), branding.SchemaVersion, branding.DefaultsVersion,
                JsonSerializer.Serialize(bootstrap.Branding, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            new(Guid.CreateVersion7(), directory.SchemaVersion, directory.DefaultsVersion, directory.PayloadJson)));
        creation.Tenant.Description = $"Provisioned from control-plane customer {request.ExternalCustomerReference} by provider islamu-event-control-plane.";
        creation.BrandingDocument.ConcurrencyStamp = Guid.CreateVersion7();
        creation.DirectoryOperatorIdentityDocument.ConcurrencyStamp = Guid.CreateVersion7();
        List<object> effects = [creation.Tenant, creation.BrandingDocument, creation.DirectoryOperatorIdentityDocument];
        Guid userId = administrator.UserId ?? Guid.CreateVersion7();
        Guid actorId = administrator.ActorId ?? Guid.CreateVersion7();
        Guid loginId = administrator.LoginId ?? Guid.CreateVersion7();
        User? user = null;
        Actor? actor = null;
        UserExternalLogin? login = null;
        if (administrator.UserId is null)
        {
            user = new User
            {
                Id = userId,
                EmailVerified = external!.EmailVerified,
                ConcurrencyStamp = Guid.CreateVersion7(),
                Pii = new() { UserId = userId, Email = external.Email, FirstName = external.FirstName, LastName = external.LastName },
                CreatedAt = occurredAt
            };
            effects.Add(user);
        }
        string? displayName = external is null ? null : !string.IsNullOrWhiteSpace(external.DisplayName)
            ? external.DisplayName : $"{external.FirstName} {external.LastName}".Trim();
        if (administrator.ActorId is null)
        {
            actor = new Actor
            {
                Id = actorId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = userId,
                Pii = new() { ActorId = actorId, DisplayName = displayName! },
                ConcurrencyStamp = Guid.CreateVersion7(),
                CreatedAt = occurredAt,
                CreatedBy = userId
            };
            effects.Add(actor);
        }
        ProviderAccountKey account = Account(original);
        if (administrator.LoginId is null)
        {
            login = new UserExternalLogin
            {
                Id = loginId,
                UserId = userId,
                User = null!,
                AuthenticationProviderId = (int)account.ProviderKind,
                AuthenticationProvider = null!,
                ProviderKey = account.Value,
                ProviderDisplayName = external!.IdentityProvider,
                CreatedAt = occurredAt,
                CreatedBy = userId
            };
            effects.Add(login);
        }
        var member = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = creation.Tenant,
            UserId = userId,
            User = null!,
            ActorId = actorId,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = occurredAt,
            CreatedAt = occurredAt,
            CreatedBy = userId
        };
        var profile = new TenantUserProfile
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = creation.Tenant,
            TenantUserId = member.Id,
            TenantUser = member,
            DisplayNameOverride = displayName,
            ContactEmailOverride = external?.Email,
            CreatedAt = occurredAt,
            CreatedBy = userId
        };
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = creation.Tenant,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = tenantAdministrator.Id,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = occurredAt,
            GrantedBy = userId,
            CreatedAt = occurredAt,
            CreatedBy = userId
        };
        var assignment = new TenantPlanAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = creation.Tenant,
            TenantPlanId = bootstrap.Plan.Id,
            TenantPlanVersionId = bootstrap.PlanVersion.Id,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedByUserId = userId,
            AssignedAt = occurredAt,
            CreatedAt = occurredAt
        };
        effects.AddRange([member, profile, grant, assignment, new TenantOnboardingState
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = creation.Tenant,
            IsCompleted = false, CurrentStep = 0, TotalSteps = 4, CompletedStepsJson = "[]", CreatedAt = occurredAt
        }]);
        foreach (var setting in bootstrap.Settings)
            effects.Add(AtprotoFederationSettingPolicy.IsDedicatedKey(setting.SettingKey)
                ? AtprotoFederationTenantSetting.Create(tenantId, setting.SettingKey, setting.Value, setting.IsLocked, occurredAt, userId)
                : new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = creation.Tenant,
                    SettingKey = setting.SettingKey,
                    Value = setting.Value,
                    IsLocked = setting.IsLocked,
                    CreatedAt = occurredAt,
                    CreatedBy = userId
                });
        foreach (var module in bootstrap.Modules)
            effects.Add(new TenantCapability
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = creation.Tenant,
                ModuleId = module.Id,
                Module = null!,
                IsEnabled = true,
                EnabledAt = occurredAt,
                CreatedAt = occurredAt
            });
        void Binding(string system, string type, string externalId, string internalType, Guid internalId, Guid? scope) =>
            effects.Add(new ExternalBinding
            {
                Id = Guid.CreateVersion7(),
                ProviderKey = "islamu-event-control-plane",
                ExternalSystem = system,
                ExternalType = type,
                ExternalId = externalId,
                InternalType = internalType,
                InternalId = internalId,
                ScopeTenantId = scope,
                ExternalBindingStatusId = (int)ExternalBindingStatusEnum.Active,
                LastSeenAt = occurredAt,
                CreatedAt = occurredAt,
                CreatedBy = userId
            });
        Binding("control-plane", ExternalBindingTypes.External.ProviderCustomer, request.ExternalCustomerReference,
            ExternalBindingTypes.Internal.Tenant, tenantId, null);
        Binding("control-plane", ExternalBindingTypes.External.ManagedTenantProvisioningOperation, original.OperationId.ToString("D"),
            ExternalBindingTypes.Internal.Tenant, tenantId, tenantId);
        string identitySystem = account.ProviderKind switch
        {
            AuthenticationProviderKind.Local => "local",
            AuthenticationProviderKind.Atproto => "atproto",
            AuthenticationProviderKind.Google => "google",
            AuthenticationProviderKind.Keycloak => "keycloak",
            _ => throw new InvalidOperationException("Managed administrator provider is not supported.")
        };
        string subject = local ? userId.ToString("D") : external!.Subject;
        Binding(identitySystem, ExternalBindingTypes.External.ExternalAdminUser, subject, ExternalBindingTypes.Internal.User, userId, tenantId);
        Binding(identitySystem, ExternalBindingTypes.External.ExternalAdminTenantUser, subject, ExternalBindingTypes.Internal.TenantUser, member.Id, tenantId);
        Binding(identitySystem, ExternalBindingTypes.External.ExternalAdminTenantUserProfile, subject, ExternalBindingTypes.Internal.TenantUserProfile, profile.Id, tenantId);
        Binding(identitySystem, ExternalBindingTypes.External.ExternalAdminUserActor, subject, ExternalBindingTypes.Internal.Actor, actorId, tenantId);
        Binding(identitySystem, ExternalBindingTypes.External.ExternalAdminUserLogin, subject, ExternalBindingTypes.Internal.UserExternalLogin, loginId, tenantId);
        effects.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = creation.Tenant,
            EntityType = nameof(Tenant),
            EntityId = tenantId.ToString("D"),
            Action = "ManagedTenantProvisioned",
            NewValues = JsonSerializer.Serialize(new
            {
                managedInstanceId = original.ManagedInstanceId,
                operationId = original.OperationId,
                planVersionId = bootstrap.PlanVersion.Id,
                modules = bootstrap.Modules.Select(row => row.ModuleKey).Order(StringComparer.Ordinal),
                localAdministratorLinked = local
            }),
            AffectedColumns = JsonSerializer.Serialize(new[] { "Tenant", "TenantAdministrator", "TenantPlanAssignment", "TenantCapabilities", "TenantSettings", "TenantOnboardingState" }),
            Timestamp = occurredAt
        });
        return new(creation, user, actor, login,
            !local && administrator.UserId.HasValue && external!.EmailVerified && administrator.EmailVerified != true,
            effects, new()
            {
                TenantId = tenantId,
                UserId = userId,
                UserActorId = actorId,
                UserExternalLoginId = loginId,
                TenantUserId = member.Id,
                TenantUserProfileId = profile.Id,
                TenantUserRoleGrantId = grant.Id,
                TenantPlanAssignmentId = assignment.Id
            });
    }

    public static ProviderAccountKey Account(ManagedTenantWorkerCreationSnapshot original)
    {
        var request = original.ReadRequest();
        if (request.Administrator.LocalIdentity is { } local)
            return new(AuthenticationProviderKind.Local, local.LocalSubjectId.ToString("D"));
        var external = request.Administrator.ExternalIdentity!;
        if (external.IdentityProvider.Equals("atproto", StringComparison.OrdinalIgnoreCase))
            return PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(
                Explore.Domain.ValueObjects.AtprotoDid.Parse(external.Subject));
        if (!Uri.TryCreate(external.IdentityProvider, UriKind.Absolute, out var issuer)
            || issuer.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Managed external identity requires an absolute OIDC issuer authority.");
        return PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(external.IdentityProvider, external.Subject);
    }
}
