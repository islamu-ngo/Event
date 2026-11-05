using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Event.Testing.Settings;

/// <summary>In-memory authority facts for native reader tests using the actual Application policy.</summary>
internal sealed class VisitorReadPolicyTestAuthority :
    IInstanceTenantSettingsReader, ITenantLifecycleAccessService, IUserContext
{
    private readonly Guid _tenantId;
    private readonly VisitorAccessMode _mode;

    public VisitorReadPolicyTestAuthority(Guid tenantId,
        VisitorAccessMode mode = VisitorAccessMode.FullRegistrationAndAuth, bool authenticated = false)
    {
        _tenantId = tenantId;
        _mode = mode;
        IsAuthenticated = authenticated;
        UserId = authenticated ? Guid.CreateVersion7() : null;
        Policy = new VisitorTenantReadAccessPolicy(this, this, this);
    }

    public IVisitorTenantReadAccessPolicy Policy { get; }
    public Guid? UserId { get; }
    public string? Email => null;
    public string? Username => null;
    public bool IsAuthenticated { get; }
    public Guid GetRequiredUserId() => UserId ?? throw new UnauthorizedAccessException();

    public Task<(IReadOnlyList<SystemSetting> System, IReadOnlyList<TenantSetting> Tenant)> ReadAsync(
        Guid? tenantId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SystemSetting> settings = keys.Contains(GovernanceSettingKeys.PublicExperience.VisitorAccessMode)
            ? [new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                Value = JsonSerializer.Serialize(_mode.ToString())
            }]
            : [];
        return Task.FromResult<(IReadOnlyList<SystemSetting>, IReadOnlyList<TenantSetting>)>((settings, []));
    }

    public Task<bool> IsPublicAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tenantId != Guid.Empty && tenantId == _tenantId);
    }

    public Task<bool> CanManageAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> CanAttemptConfiguredAdministratorSyncAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<Guid?> ResolveConfiguredAdministratorTenantAsync(string tenantSlug, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(null);
}
