using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.Management;
using Explore.Application.Management;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Settings;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Explore.Persistence.Security;

/// <summary>Reads only the fixed noncredential scheduling inventory on its selected instance owner.</summary>
internal sealed class ManagedTenantProvisioningSchedulingSource(
    ExploreDbContext context, IManagedTenantLocalEligibilitySource eligibility,
    IOptions<ManagedControlPlaneOptions> options) : IManagedTenantProvisioningSchedulingSource
{
    private IManagedTenantLocalEligibilityLease? local;
    private bool retained;
    private Task<ManagedTenantProvisioningPolicySetting[]>? policies;

    public async Task<IAsyncDisposable> RetainAdministratorAsync(Guid? subject, CancellationToken token)
    {
        context.RequireBoundTenantLease();
        if (retained || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not null
            || context.Database.CurrentTransaction is not null)
            throw Changed();
        retained = true;
        if (subject is { } id)
        {
            local = await eligibility.AcquireAsync(id, token);
            var image = local.Eligibility;
            if (image.LocalSubjectId != id || image.Generation <= 0
                || new[] { image.LocalSubjectId, image.CurrentOperationId, image.PersonalActorId, image.ExternalLoginId }
                    .Any(pointer => pointer == Guid.Empty || pointer.Version != 7)
                || image.State is not (LocalIdentityEligibilityState.Ready or LocalIdentityEligibilityState.ChangeRequired))
            {
                await local.DisposeAsync();
                local = null;
                throw Changed();
            }
        }
        return new AdministratorLease(this);
    }

    public async Task RequireCurrentAsync(CancellationToken token)
    {
        context.RequireBoundTenantLease();
        if (!retained || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not null)
            throw Changed();
        if (local is not null)
            await local.RequireCurrentAsync(token);
    }

    public async Task<DeploymentMode> ReadDeploymentModeAsync(CancellationToken token)
    {
        var current = await context.InstanceBootstrapStates.AsNoTracking()
            .OrderByDescending(row => row.Generation).ThenByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Select(row => new { row.Status, row.DeploymentMode }).FirstOrDefaultAsync(token);
        return current?.Status == InstanceBootstrapStatus.Completed ? current.DeploymentMode : DeploymentMode.SingleTenant;
    }

    public Task<ManagedTenantProvisioningRegistrationPolicy.Facts?> ReadRegistrationFactsAsync(CancellationToken token) =>
        context.ManagedControlPlaneRegistrations.AsNoTracking().OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id)
            .Select(row => new ManagedTenantProvisioningRegistrationPolicy.Facts(
                row.Status, row.ManagedInstanceId, row.ManagementApiVersion, row.DeploymentMode)).FirstOrDefaultAsync(token);

    public async Task<TenantActivationCapacityAssessment> AssessCapacityAsync(bool requireMultiTenant, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var mode = await ReadDeploymentModeAsync(cancellationToken);
        var initial = TenantActivationCapacityPolicy.Assess(configured.Enabled, requireMultiTenant, mode,
            configured.MaximumTenantCount, 0, 0);
        if (!configured.Enabled || !initial.Allowed)
            return initial;
        int active = await context.Tenants.CountAsync(tenant => context.Set<TenantStatus>()
            .Any(status => status.Id == tenant.TenantStatusId && status.IsActiveState), cancellationToken);
        int reserved = await context.ManagedTenantProvisioningOperations.CountAsync(row =>
            row.Status == ManagedTenantProvisioningStatus.Pending || row.Status == ManagedTenantProvisioningStatus.Processing,
            cancellationToken);
        return TenantActivationCapacityPolicy.Assess(true, requireMultiTenant, mode, configured.MaximumTenantCount, active, reserved);
    }

    public Task<ManagedTenantProvisioningBinding?> ReadCustomerBindingAsync(string customer, CancellationToken token) =>
        Binding(ExternalBindingTypes.External.ProviderCustomer, customer, null, token);

    public Task<ManagedTenantProvisioningBinding?> ReadOperationBindingAsync(Guid operation, Guid tenant, CancellationToken token) =>
        Binding(ExternalBindingTypes.External.ManagedTenantProvisioningOperation, operation.ToString("D"), tenant, token);

    private Task<ManagedTenantProvisioningBinding?> Binding(string kind, string external, Guid? tenant, CancellationToken token) =>
        context.ExternalBindings.AsNoTracking().Where(row => row.ProviderKey == "islamu-event-control-plane"
                && row.ExternalSystem == "control-plane" && row.ExternalType == kind && row.ExternalId == external
                && row.ScopeTenantId == tenant)
            .Select(row => new ManagedTenantProvisioningBinding(row.InternalType, row.InternalId)).FirstOrDefaultAsync(token);

    public Task<bool> TenantMatchesSlugAsync(Guid tenant, string slug, CancellationToken token) =>
        context.Tenants.AnyAsync(row => row.Id == tenant && row.Slug == slug, token);

    public Task<bool> TenantSlugExistsAsync(string slug, CancellationToken token) =>
        context.Tenants.AnyAsync(row => row.Slug == slug, token);

    public Task<TenantPlanVersion?> GetPlanVersionAsync(Guid version, CancellationToken token) =>
        context.TenantPlanVersions.AsNoTracking().Include(row => row.TenantPlan).Include(row => row.Settings).Include(row => row.Quotas)
            .SingleOrDefaultAsync(row => row.Id == version, token);

    public async Task<IReadOnlyList<ModuleDefinition>> GetActiveModulesAsync(IReadOnlyCollection<string> keys, CancellationToken token) =>
        await context.ModuleDefinitions.AsNoTracking().Where(row => row.IsActive && keys.Contains(row.ModuleKey))
            .OrderBy(row => row.ModuleKey).ToArrayAsync(token);

    public async Task<bool> DomainHostExistsAsync(string host, CancellationToken token)
    {
        string canonical = host.Trim().TrimEnd('.').ToLowerInvariant();
        return await ManagedTenantDomainQuery.ReadAsync(context,
            JsonSerializer.Serialize(canonical), JsonSerializer.Serialize(canonical + "."), token) is not null;
    }

    private async Task<ManagedTenantProvisioningPolicySetting[]> ReadPoliciesAsync(CancellationToken token)
    {
        policies ??= context.Set<ManagedTenantProvisioningPolicySetting>().AsNoTracking().OrderBy(row => row.SettingKey).ToArrayAsync(token);
        var rows = await policies;
        ManagedTenantProvisioningPolicyInventory.RequireComplete(rows);
        return rows;
    }

    public async Task<SystemSetting?> ReadPolicySettingAsync(string key, CancellationToken token)
    {
        if (!ManagedTenantProvisioningPolicyInventory.ValueKeys.Contains(key))
            throw Changed();
        var row = (await ReadPoliciesAsync(token)).Single(row => row.SettingKey == key);
        return row.IsPresent ? new() { Id = row.Id, SettingKey = row.SettingKey, Value = row.Value!, IsLocked = row.IsLocked } : null;
    }

    public async Task<bool> IsPolicyLockedAsync(string key, CancellationToken token) =>
        (await ReadPoliciesAsync(token)).Single(row => row.SettingKey == key).IsLocked;

    public Task<LocalIdentityBinding?> ReadLocalAdministratorAsync(Guid subject, CancellationToken token)
    {
        var image = local?.Eligibility;
        return Task.FromResult(image?.LocalSubjectId == subject
            ? new LocalIdentityBinding(subject, image.PersonalActorId, image.ExternalLoginId,
                image.State == LocalIdentityEligibilityState.Ready ? LocalCredentialState.Ready : LocalCredentialState.ChangeRequired)
            : null);
    }

    public async Task<string?> ValidateStorageQuotasAsync(IEnumerable<TenantPlanVersionQuota> quotas, CancellationToken token) =>
        TenantPlanStorageQuotaCeilingPolicy.Validate(quotas,
            (await ReadPoliciesAsync(token)).Single(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes).Value);

    public async Task<TenantBrandingSettingsDocumentLockState> ReadBrandingLocksAsync(CancellationToken token)
    {
        var rows = await ReadPoliciesAsync(token);
        string? Value(string key) => rows.Single(row => row.SettingKey == key).Value;
        bool Locked(string key) => rows.Single(row => row.SettingKey == key).IsLocked;
        return TenantBrandingSettingsDocumentLockService.AssessLockState(
            Value(GovernanceSettingKeys.Deployment.Mode), Value(GovernanceSettingKeys.Tenants.WhiteLabelingEnabled),
            Locked(GovernanceSettingKeys.Branding.DisplayName), Locked(GovernanceSettingKeys.Branding.LogoUrl),
            Locked(GovernanceSettingKeys.Branding.FaviconUrl), Locked(GovernanceSettingKeys.Branding.CustomCssUrl));
    }

    private static UnauthorizedAccessException Changed() => new("managed_scheduling_source_invalid");

    private sealed class AdministratorLease(ManagedTenantProvisioningSchedulingSource owner) : IAsyncDisposable
    {
        private bool disposed;
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            owner.retained = false;
            if (owner.local is { } lease)
            {
                owner.local = null;
                await lease.DisposeAsync();
            }
        }
    }
}
