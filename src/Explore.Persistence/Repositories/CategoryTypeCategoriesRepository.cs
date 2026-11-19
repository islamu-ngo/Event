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
        var entry = TrackForNarrowUpdate(entity);
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
