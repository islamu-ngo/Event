using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class CategoryRepository : GenericRepository<Category, Guid>, ICategoryRepository
{
    private readonly ExploreDbContext _dbContext;

    public CategoryRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public new async Task<Category?> GetById(Guid id)
    {
        return await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Category?> GetCategoryWithDetails(Guid id)
    {
        return await _dbContext.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
            .Include(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<Category>> GetCategoriesWithDetails()
    {
        return await _dbContext.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
            .Include(c => c.Tenant)
            .ToListAsync();
    }

    public async Task<(List<Category> Items, int TotalCount)> GetCategoriesWithDetailsPaged(int pageNumber, int pageSize)
    {
        var query = _dbContext.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
            .OrderBy(c => c.FullName);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}
