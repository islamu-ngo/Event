// for user-specific setting overrides.

namespace Explore.Persistence.Repositories;

using Explore.Persistence.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;

public class UserPreferenceRepository : GenericRepository<UserPreference, Guid>, IUserPreferenceRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly HumanSettingsDataBoundary? _humanBoundary;

    private UserPreferenceRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Creates an observation-only repository; every mutation requires a separate admitted owner.</summary>
    public static UserPreferenceRepository CreateReadOnly(ExploreDbContext context) => new(context);

    private HumanSettingsDataBoundary HumanBoundary =>
        _humanBoundary ?? throw new InvalidOperationException("This settings observation cannot mutate data.");

    public UserPreferenceRepository(ExploreDbContext dbContext, HumanSettingsDataBoundary humanBoundary) : base(dbContext)
    {
        _dbContext = dbContext;
        _humanBoundary = humanBoundary;
    }

    public async Task<UserPreference?> GetByUserAndKey(Guid tenantId, Guid userId, string key)
    {
        return await _dbContext.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.UserId == userId && s.SettingKey == key);
    }

    public async Task<List<UserPreference>> GetAllForUser(Guid tenantId, Guid userId)
    {
        return await _dbContext.UserPreferences
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.UserId == userId)
            .ToListAsync();
    }

    public async Task<bool> RemoveOverride(Guid tenantId, Guid userId, string key)
    {
        await HumanBoundary.RequireAsync(_dbContext, SettingScope.User, tenantId, userId, key,
            HumanSettingsMutationKind.Remove);
        if (HumanBoundary.OrdinarySource is { } ordinary)
            return await HumanBoundary.OrdinaryStore.WriteUserAsync(
                ordinary, tenantId, userId, key, OrdinaryUserPreferenceWrite.Remove, null, CancellationToken.None) is not null;
        var setting = await _dbContext.UserPreferences
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.UserId == userId && s.SettingKey == key);

        if (setting == null)
        {
            await HumanBoundary.RequireAsync(_dbContext, SettingScope.User, tenantId, userId, key,
                HumanSettingsMutationKind.Remove);
            return false;
        }

        _dbContext.UserPreferences.Remove(setting);
        await _dbContext.SaveChangesAsync();
        await HumanBoundary.RequireAsync(_dbContext, SettingScope.User, tenantId, userId, key,
            HumanSettingsMutationKind.Remove);
        return true;
    }

    public override async Task<UserPreference> Create(UserPreference entity)
    {
        var setting = Snapshot(entity);
        await RequireAsync(setting, HumanSettingsMutationKind.SetValue);
        if (HumanBoundary.OrdinarySource is { } ordinary)
            return (await HumanBoundary.OrdinaryStore.WriteUserAsync(ordinary, setting.TenantId, setting.UserId,
                setting.SettingKey, OrdinaryUserPreferenceWrite.Create, setting.Value, CancellationToken.None))!;
        var created = await base.Create(setting);
        await RequireAsync(setting, HumanSettingsMutationKind.SetValue);
        return created;
    }

    public override async Task Update(UserPreference entity)
    {
        var setting = Snapshot(entity);
        await RequireAsync(setting, HumanSettingsMutationKind.SetValue);
        await RequireStoredTargetAsync(setting);
        if (HumanBoundary.OrdinarySource is { } ordinary)
        {
            await HumanBoundary.OrdinaryStore.WriteUserAsync(ordinary, setting.TenantId, setting.UserId,
                setting.SettingKey, OrdinaryUserPreferenceWrite.Update, setting.Value, CancellationToken.None);
            return;
        }
        await base.Update(setting);
        await RequireAsync(setting, HumanSettingsMutationKind.SetValue);
    }

    public override async Task Delete(UserPreference entity)
    {
        var setting = Snapshot(entity);
        await RequireAsync(setting, HumanSettingsMutationKind.Remove);
        await RequireStoredTargetAsync(setting);
        if (HumanBoundary.OrdinarySource is { } ordinary)
        {
            await HumanBoundary.OrdinaryStore.WriteUserAsync(ordinary, setting.TenantId, setting.UserId,
                setting.SettingKey, OrdinaryUserPreferenceWrite.Remove, null, CancellationToken.None);
            return;
        }
        // Do not attach the caller's entity: its mutable key/owner and navigation are not authority.
        await _dbContext.UserPreferences
            .Where(row => row.Id == setting.Id && row.TenantId == setting.TenantId
                && row.UserId == setting.UserId && row.SettingKey == setting.SettingKey)
            .ExecuteDeleteAsync();
        await RequireAsync(setting, HumanSettingsMutationKind.Remove);
    }

    private Task RequireAsync(UserPreference setting, HumanSettingsMutationKind kind) =>
        HumanBoundary.RequireAsync(_dbContext, SettingScope.User, setting.TenantId, setting.UserId,
            setting.SettingKey, kind, value: kind == HumanSettingsMutationKind.SetValue ? setting.Value : null,
            auditActorId: setting.UpdatedBy ?? setting.CreatedBy);

    private async Task RequireStoredTargetAsync(UserPreference setting)
    {
        bool matches = await _dbContext.UserPreferences.AsNoTracking()
            .AnyAsync(row => row.Id == setting.Id && row.TenantId == setting.TenantId
                && row.UserId == setting.UserId && row.SettingKey == setting.SettingKey);
        if (!matches)
            throw new UnauthorizedAccessException("The stored preference must match the admitted user, tenant and key.");
    }

    private static UserPreference Snapshot(UserPreference setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return new()
        {
            Id = setting.Id,
            TenantId = setting.TenantId,
            Tenant = null!,
            UserId = setting.UserId,
            SettingKey = setting.SettingKey,
            Value = setting.Value,
            CreatedAt = setting.CreatedAt,
            CreatedBy = setting.CreatedBy,
            UpdatedAt = setting.UpdatedAt,
            UpdatedBy = setting.UpdatedBy
        };
    }
}
