using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class CategoryTypeCategoriesRepository : GenericRepository<CategoryTypeCategories, Guid>, ICategoryTypeCategoriesRepository
{
    private readonly ExploreDbContext _dbContext;

    public CategoryTypeCategoriesRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Persists only the columns Runtime may mutate under <c>PostgresCategoryMutationPermissions</c>:
    /// the identity and tenant columns are immutable and carry no UPDATE grant.
    /// </summary>
    public override async Task Update(CategoryTypeCategories entity)
    {
        if (entity.TenantId == Guid.Empty
            || _dbContext.TenantFilterTenantId != entity.TenantId
            || _dbContext.IsTenantFilterBypassed)
        {
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        }

        var categoryId = entity.CategoryId;
        var categoryTypeId = entity.CategoryTypeId;
        var tracked = _dbContext.CategoryTypeCategories.Local.FirstOrDefault(item => item.Id == entity.Id);
        var entry = tracked is not null ? _dbContext.Entry(tracked) : _dbContext.Entry(entity);
        if (tracked is not null && (tracked.TenantId != entity.TenantId
            || entry.Property(item => item.TenantId).OriginalValue != entity.TenantId))
        {
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        }
        entry.State = EntityState.Unchanged;
        entry.Entity.CategoryId = categoryId;
        entry.Entity.CategoryTypeId = categoryTypeId;
        entry.Property(item => item.CategoryId).IsModified = true;
        entry.Property(item => item.CategoryTypeId).IsModified = true;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<Category>> GetCategoriesByCategoryType(int categoryTypeId)
    {
        return await _dbContext.CategoryTypeCategories
            .AsNoTracking()
            .Include(ct => ct.Category)
            .Where(ct => ct.CategoryTypeId == categoryTypeId)
            .Select(ct => ct.Category)
            .ToListAsync();
    }

    public async Task<List<CategoryType>> GetCategoryTypesForCategory(Guid categoryId)
    {
        return await _dbContext.CategoryTypeCategories
            .AsNoTracking()
            .Include(ct => ct.CategoryType)
            .Where(ct => ct.CategoryId == categoryId)
            .Select(ct => ct.CategoryType)
            .ToListAsync();
    }

    public async Task<bool> Exists(Guid categoryId, int categoryTypeId)
    {
        return await _dbContext.CategoryTypeCategories
            .AsNoTracking()
            .AnyAsync(ct => ct.CategoryId == categoryId && ct.CategoryTypeId == categoryTypeId);
    }

    public async Task<List<(CategoryType CategoryType, List<Category> Categories)>> GetAllCategoriesGroupedByCategoryType()
    {
        var allEntries = await _dbContext.CategoryTypeCategories
            .AsNoTracking()
            .Include(ct => ct.CategoryType)
            .Include(ct => ct.Category)
            .OrderBy(ct => ct.CategoryType.FullName)
            .ThenBy(ct => ct.Category.FullName)
            .ToListAsync();

        var lookup = allEntries.ToLookup(ct => ct.CategoryTypeId);
        return lookup.Select(g =>
        {
            var first = g.First();
            return (first.CategoryType, g.Select(ct => ct.Category).ToList());
        }).ToList();
    }
}
