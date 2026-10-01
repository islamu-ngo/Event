using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ICategoryTypeRepository : IGenericRepository<CategoryType, int>
{
    Task<CategoryType?> GetCategoryTypeWithDetails(int id);
    Task<List<CategoryType>> GetCategoryTypesWithDetails();
}
