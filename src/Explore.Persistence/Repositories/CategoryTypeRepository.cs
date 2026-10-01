using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class CategoryTypeRepository : GenericRepository<CategoryType, int>, ICategoryTypeRepository
{
    private readonly ExploreDbContext _dbContext;

    public CategoryTypeRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CategoryType?> GetCategoryTypeWithDetails(int id)
    {
        return await _dbContext.CategoryTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<List<CategoryType>> GetCategoryTypesWithDetails()
    {
        return await _dbContext.CategoryTypes
            .AsNoTracking()
            .ToListAsync();
    }
}
