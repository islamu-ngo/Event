using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.Management;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Settings;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Original noncredential observations; no source object issues creator authority.</summary>
internal sealed class ManagedTenantWorkerCreationSources : IManagedTenantProvisioningPreflightSource
{
    private readonly ExploreDbContext context;
    private readonly ManagedTenantWorkerCreationSnapshot original;
    private readonly ManagedTenantLocalEligibility? local;
    private readonly Dictionary<string, Guid?> domains = new(StringComparer.Ordinal);
    private readonly string immutableImage;
    internal TenantImage[] Tenants { get; }
    internal StatusImage[] Statuses { get; }
    internal BootstrapImage? Bootstrap { get; }
    internal RegistrationImage[] Registrations { get; }
    internal ReservationImage[] Reservations { get; }
    internal RoleImage[] Roles { get; }
    internal ManagedTenantProvisioningPolicySetting[] Policies { get; }
    internal TenantPlanVersion? Version { get; }
    internal ModuleDefinition[] Modules { get; }
    internal BindingImage[] CustomerBindings { get; }
    internal AdministratorImage? Administrator { get; }

    private ManagedTenantWorkerCreationSources(ExploreDbContext context,
        ManagedTenantWorkerCreationSnapshot original, ManagedTenantLocalEligibility? local,
        TenantImage[] tenants, StatusImage[] statuses, BootstrapImage? bootstrap, RegistrationImage[] registrations,
        ReservationImage[] reservations, RoleImage[] roles, ManagedTenantProvisioningPolicySetting[] policies,
        TenantPlanVersion? version, ModuleDefinition[] modules, BindingImage[] customerBindings,
        AdministratorImage? administrator)
    {
        this.context = context; this.original = original; this.local = local;
        Tenants = tenants; Statuses = statuses; Bootstrap = bootstrap; Registrations = registrations;
        Reservations = reservations; Roles = roles; Policies = policies; Version = version;
        Modules = modules; CustomerBindings = customerBindings; Administrator = administrator;
        immutableImage = Image(null, Administrator);
    }

    internal static async Task<ManagedTenantWorkerCreationSources> ReadAsync(ExploreDbContext context,
        ManagedTenantWorkerCreationSnapshot original, ManagedTenantLocalEligibility? local, CancellationToken token)
    {
        var request = original.ReadRequest();
        var tenants = await context.Set<Tenant>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new TenantImage(row.Id, row.Slug, row.TenantStatusId)).ToArrayAsync(token);
        var statuses = await context.Set<TenantStatus>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new StatusImage(row.Id, row.IsActiveState)).ToArrayAsync(token);
        var bootstrap = await context.Set<InstanceBootstrapState>().AsNoTracking()
            .OrderByDescending(row => row.Generation).ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Select(row => new BootstrapImage(row.Id, row.Status, row.DeploymentMode, row.Generation, row.CreatedAt))
            .FirstOrDefaultAsync(token);
        var registrations = await context.Set<ManagedControlPlaneRegistration>().AsNoTracking()
            .OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id)
            .Select(row => new RegistrationImage(row.Id, row.ManagedInstanceId, row.Status,
                row.ManagementApiVersion, row.DeploymentMode, row.RowVersion, row.CreatedAt)).ToArrayAsync(token);
        var reservations = await context.Set<ManagedTenantProvisioningOperation>().AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new ReservationImage(row.Id, row.Status)).ToArrayAsync(token);
        var roles = await context.Set<Role>().AsNoTracking().Where(row => row.MasterCode == "tenant.admin")
            .OrderBy(row => row.Id).Select(row => new RoleImage(row.Id, row.MasterCode, row.RoleScopeId)).ToArrayAsync(token);
        var policies = await context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking()
            .OrderBy(row => row.SettingKey).ToArrayAsync(token);
        ManagedTenantProvisioningPolicyInventory.RequireComplete(policies);
        var version = await context.Set<TenantPlanVersion>().AsNoTracking().Include(row => row.TenantPlan)
            .Include(row => row.Settings).Include(row => row.Quotas)
            .SingleOrDefaultAsync(row => row.Id == request.Plan.VersionId, token);
        var modules = await context.Set<ModuleDefinition>().AsNoTracking()
            .Where(row => request.ApprovedModules.Contains(row.ModuleKey)).OrderBy(row => row.ModuleKey).ToArrayAsync(token);
        var bindings = await context.Set<ExternalBinding>().AsNoTracking()
            .Where(row => row.ProviderKey == "islamu-event-control-plane" && row.ExternalSystem == "control-plane"
                && row.ExternalType == ExternalBindingTypes.External.ProviderCustomer
                && row.ExternalId == request.ExternalCustomerReference && row.ScopeTenantId == null)
            .OrderBy(row => row.Id).Select(row => new BindingImage(row.Id, row.ProviderKey, row.ExternalSystem,
                row.ExternalType, row.ExternalId, row.InternalType, row.InternalId, row.ScopeTenantId,
                row.ExternalBindingStatusId)).ToArrayAsync(token);
        var account = ManagedTenantWorkerGraphPlan.Account(original);
        var logins = await context.Set<UserExternalLogin>().AsNoTracking()
            .Where(row => row.AuthenticationProviderId == (int)account.ProviderKind && row.ProviderKey == account.Value)
            .Select(row => new LoginImage(row.Id, row.UserId, row.AuthenticationProviderId, row.ProviderKey)).ToArrayAsync(token);
        if (logins.Length > 1) throw Changed();
        AdministratorImage? administrator = null;
        if (logins.SingleOrDefault() is { } login)
        {
            var user = await context.Set<User>().AsNoTracking().Where(row => row.Id == login.UserId)
                .Select(row => new UserImage(row.Id, row.EmailVerified, row.ConcurrencyStamp, row.IsDeleted,
                    row.DeletedAt, row.DeletedBy, row.UpdatedAt, row.UpdatedBy)).SingleOrDefaultAsync(token)
                ?? throw Changed();
            var actors = await context.Set<Actor>().AsNoTracking().Where(row => row.UserId == user.Id)
                .Select(row => new ActorImage(row.Id, row.UserId, row.ActorTypeId, row.IsDeleted,
                    row.IsSuspended, row.ConcurrencyStamp)).ToArrayAsync(token);
            if (user.IsDeleted || actors.Length > 1 || actors.Any(row => row.IsDeleted || row.IsSuspended
                || row.ActorTypeId != (int)ActorTypeEnum.User)) throw Changed();
            administrator = new(user, actors.SingleOrDefault(), login);
        }
        if (local is not null && (administrator?.User.Id != local.LocalSubjectId
            || administrator.Actor?.Id != local.PersonalActorId || administrator.Login.Id != local.ExternalLoginId))
            throw Changed();
        return new(context, original, local, tenants, statuses, bootstrap, registrations, reservations, roles,
            policies, version, modules, bindings, administrator);
    }

    internal async Task RequireCurrentAsync(TenantImage createdTenant, AdministratorImage? completedAdministrator,
        BindingImage[] completedCustomerBindings, CancellationToken token)
    {
        var current = await ReadAsync(context, original, local, token);
        if (current.Administrator != completedAdministrator
            || !current.CustomerBindings.SequenceEqual(completedCustomerBindings)
            || current.Tenants.Count(row => row == createdTenant) != 1
            || current.Image(createdTenant.Id, Administrator, CustomerBindings) != immutableImage) throw Changed();
        foreach (var (host, id) in domains)
            if (await current.ReadDomainAsync(host, token) != id) throw Changed();
    }

    internal async Task RequireUnchangedAsync(CancellationToken token)
    {
        var current = await ReadAsync(context, original, local, token);
        if (current.Image(null, current.Administrator) != immutableImage) throw Changed();
        foreach (var (host, id) in domains)
            if (await current.ReadDomainAsync(host, token) != id) throw Changed();
    }

    private string Image(Guid? createdTenant, AdministratorImage? originalAdministrator,
        BindingImage[]? originalCustomerBindings = null)
    {
        var version = Version;
        return JsonSerializer.Serialize(new
        {
            tenants = Tenants.Where(row => row.Id != createdTenant),
            Statuses,
            Bootstrap,
            Registrations,
            reservations = Reservations.Where(row => row.Id != original.OperationId),
            Roles,
            policies = Policies.Select(row => new { row.Id, row.SettingKey, row.IsPresent, row.IsLocked, row.Value, row.Revision }),
            version = version is null ? null : new
            {
                version.Id,
                version.TenantPlanId,
                version.VersionNumber,
                version.TenantPlanStatusId,
                version.PriceAmount,
                version.CurrencyCode,
                version.BillingPeriod,
                version.IsActiveForProvisioning,
                version.CreatedAt,
                version.CreatedBy,
                version.UpdatedAt,
                version.UpdatedBy,
                plan = new
                {
                    version.TenantPlan.Id,
                    version.TenantPlan.Key,
                    version.TenantPlan.DisplayName,
                    version.TenantPlan.Description,
                    version.TenantPlan.CreatedAt,
                    version.TenantPlan.CreatedBy,
                    version.TenantPlan.UpdatedAt,
                    version.TenantPlan.UpdatedBy
                },
                settings = version.Settings.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id,
                    row.TenantPlanVersionId,
                    row.SettingKey,
                    row.JsonValue,
                    row.IsLocked,
                    row.CreatedAt,
                    row.CreatedBy,
                    row.UpdatedAt,
                    row.UpdatedBy
                }),
                quotas = version.Quotas.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id,
                    row.TenantPlanVersionId,
                    row.QuotaKey,
                    row.Limit,
                    row.CreatedAt,
                    row.CreatedBy,
                    row.UpdatedAt,
                    row.UpdatedBy
                })
            },
            Modules,
            CustomerBindings = originalCustomerBindings ?? CustomerBindings,
            administrator = originalAdministrator
        });
    }

    public Task<bool> TenantSlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        Task.FromResult(Tenants.Any(row => row.Slug == slug));
    public Task<TenantPlanVersion?> GetPlanVersionAsync(Guid versionId, CancellationToken cancellationToken) =>
        Task.FromResult(Version?.Id == versionId ? Version : null);
    public Task<IReadOnlyList<ModuleDefinition>> GetActiveModulesAsync(IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModuleDefinition>>(
            Modules.Where(row => row.IsActive && keys.Contains(row.ModuleKey)).ToArray());
    public async Task<bool> DomainHostExistsAsync(string host, CancellationToken cancellationToken) =>
        await ReadDomainAsync(host, cancellationToken) is not null;
    private async Task<Guid?> ReadDomainAsync(string host, CancellationToken token)
    {
        string canonical = host.Trim().TrimEnd('.').ToLowerInvariant();
        var row = await ManagedTenantDomainQuery.ReadAsync(context, JsonSerializer.Serialize(canonical),
            JsonSerializer.Serialize(canonical + "."), token);
        domains[host] = row?.Id;
        return row?.Id;
    }
    public Task<SystemSetting?> ReadPolicySettingAsync(string key, CancellationToken cancellationToken)
    {
        if (!ManagedTenantProvisioningPolicyInventory.ValueKeys.Contains(key)) throw Changed();
        var row = Policies.Single(row => row.SettingKey == key);
        return Task.FromResult(row.IsPresent ? new SystemSetting
        { Id = row.Id, SettingKey = row.SettingKey, Value = row.Value!, IsLocked = row.IsLocked } : null);
    }
    public Task<bool> IsPolicyLockedAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Policies.Single(row => row.SettingKey == key).IsLocked);
    public Task<LocalIdentityBinding?> ReadLocalAdministratorAsync(Guid subject, CancellationToken cancellationToken) =>
        Task.FromResult(local?.LocalSubjectId == subject ? new LocalIdentityBinding(subject, local.PersonalActorId,
            local.ExternalLoginId, local.State == LocalIdentityEligibilityState.Ready
                ? LocalCredentialState.Ready : LocalCredentialState.ChangeRequired) : null);
    public Task<string?> ValidateStorageQuotasAsync(IEnumerable<TenantPlanVersionQuota> quotas, CancellationToken cancellationToken) =>
        Task.FromResult(TenantPlanStorageQuotaCeilingPolicy.Validate(quotas,
            Policies.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes).Value));
    public Task<TenantBrandingSettingsDocumentLockState> ReadBrandingLocksAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TenantBrandingSettingsDocumentLockService.AssessLockState(
            Policies.Single(row => row.SettingKey == GovernanceSettingKeys.Deployment.Mode).Value,
            Policies.Single(row => row.SettingKey == GovernanceSettingKeys.Tenants.WhiteLabelingEnabled).Value,
            Locked(GovernanceSettingKeys.Branding.DisplayName), Locked(GovernanceSettingKeys.Branding.LogoUrl),
            Locked(GovernanceSettingKeys.Branding.FaviconUrl), Locked(GovernanceSettingKeys.Branding.CustomCssUrl)));
    private bool Locked(string key) => Policies.Single(row => row.SettingKey == key).IsLocked;
    private static UnauthorizedAccessException Changed() => new("managed_worker_creation_sources_changed");

    internal sealed record TenantImage(Guid Id, string Slug, int StatusId);
    internal sealed record StatusImage(int Id, bool Active);
    internal sealed record BootstrapImage(Guid Id, InstanceBootstrapStatus Status, DeploymentMode Mode,
        long Generation, DateTime CreatedAt);
    internal sealed record RegistrationImage(Guid Id, Guid ManagedInstanceId, ManagedControlPlaneRegistrationStatus Status,
        string ApiVersion, DeploymentMode Mode, uint Version, DateTime CreatedAt);
    internal sealed record ReservationImage(Guid Id, ManagedTenantProvisioningStatus Status);
    internal sealed record RoleImage(int Id, string Code, int ScopeId);
    internal sealed record BindingImage(Guid Id, string Provider, string System, string Type, string ExternalId,
        string InternalType, Guid InternalId, Guid? TenantId, int Status);
    internal sealed record LoginImage(Guid Id, Guid UserId, int Provider, string Key);
    internal sealed record UserImage(Guid Id, bool? EmailVerified, Guid Stamp, bool IsDeleted,
        DateTime? DeletedAt, Guid? DeletedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
    internal sealed record ActorImage(Guid Id, Guid? UserId, int ActorTypeId, bool IsDeleted, bool IsSuspended, Guid Stamp);
    internal sealed record AdministratorImage(UserImage User, ActorImage? Actor, LoginImage Login);
}
