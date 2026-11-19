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

    public override async Task Update(CategoryTypeCategories entity)
    {
        if (entity.TenantId == Guid.Empty
            || _dbContext.TenantFilterTenantId != entity.TenantId
            || _dbContext.IsTenantFilterBypassed)
        {
            throw new InvalidOperationException("tracked_tenant_authority_mismatch");
        }

        var tracked = _dbContext.CategoryTypeCategories.Local.FirstOrDefault(item => item.Id == entity.Id);
        var entry = tracked is not null ? _dbContext.Entry(tracked) : _dbContext.Entry(entity);
        if (tracked is not null)
        {
            tracked.CategoryId = entity.CategoryId;
            tracked.CategoryTypeId = entity.CategoryTypeId;
        }
        else if (entry.State == EntityState.Detached)
        {
            _dbContext.CategoryTypeCategories.Attach(entity);
        }

        entry.Property(item => item.CategoryId).IsModified = true;
        entry.Property(item => item.CategoryTypeId).IsModified = true;
        entry.Property(item => item.TenantId).IsModified = false;
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
