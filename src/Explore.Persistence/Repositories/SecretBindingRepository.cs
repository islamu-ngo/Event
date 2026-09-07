// resolver hot path; inherits Create/Update/Delete/Exists semantics from GenericRepository.

namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;

public class SecretBindingRepository : GenericRepository<SecretBinding, Guid>, ISecretBindingRepository
{
    private readonly ExploreDbContext _dbContext;

    public SecretBindingRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    private static int ToSettingScopeId(SecretScope scope) => scope switch
    {
        SecretScope.Instance => (int)ConfigurationScopeEnum.Instance,
        SecretScope.Tenant => (int)ConfigurationScopeEnum.Tenant,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported secret scope.")
    };

    public Task<SecretBinding?> GetByKeyAndScopeAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SecretBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.SettingKey == settingKey
                      && b.Qualifier == string.Empty
                      && b.SettingScopeId == ToSettingScopeId(scope)
                     && b.ScopeId == scopeId,
                cancellationToken);
    }

    public Task<SecretBinding?> GetByKeyScopeAndQualifierAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string qualifier,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SecretBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.SettingKey == settingKey
                      && b.Qualifier == qualifier
                      && b.SettingScopeId == ToSettingScopeId(scope)
                      && b.ScopeId == scopeId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<SecretBinding>> GetByScopeAsync(
        SecretScope scope,
        Guid? scopeId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.SecretBindings
            .AsNoTracking()
            .Where(b => b.SettingScopeId == ToSettingScopeId(scope) && b.ScopeId == scopeId)
            .OrderBy(b => b.SettingKey)
            .ToListAsync(cancellationToken);
    }

    public Task<SecretBinding?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid bindingId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SecretBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                binding => binding.Id == bindingId &&
                           binding.SettingScopeId == ToSettingScopeId(SecretScope.Tenant) &&
                           binding.ScopeId == tenantId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<SecretBinding>> GetAllForKeyAsync(
        string settingKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.SecretBindings
            .AsNoTracking()
            .Where(b => b.SettingKey == settingKey)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsForScopeAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SecretBindings
            .AsNoTracking()
            .AnyAsync(
                b => b.SettingKey == settingKey
                      && b.Qualifier == string.Empty
                      && b.SettingScopeId == ToSettingScopeId(scope)
                     && b.ScopeId == scopeId,
                cancellationToken);
    }

    public Task<bool> ExistsForScopeAndQualifierAsync(
        string settingKey,
        SecretScope scope,
        Guid? scopeId,
        string qualifier,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SecretBindings
            .AsNoTracking()
            .AnyAsync(
                b => b.SettingKey == settingKey
                      && b.Qualifier == qualifier
                      && b.SettingScopeId == ToSettingScopeId(scope)
                      && b.ScopeId == scopeId,
                cancellationToken);
    }
}
