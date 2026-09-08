using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public interface ICategoryService
{
    Task<ICollection<CategoryListDto>> GetCategoriesAsync();
    Task<ICollection<CategoryListDto>> GetAllCategoriesAsync(); // Alias for GetCategoriesAsync
    Task<PaginatedResult<CategoryListDto>> GetCategoriesPagedAsync(int pageNumber, int pageSize);
    Task<CategoryDto?> GetCategoryByIdAsync(Guid categoryId);
    Task<BaseCommandResponseOfGuid?> CreateCategoryAsync(CreateCategoryDto dto);
    Task<BaseCommandResponseOfGuid?> UpdateCategoryAsync(Guid id, Guid expectedConcurrencyStamp, UpdateCategoryDto dto);
    Task<bool> DeleteCategoryAsync(Guid categoryId);
    Task<ICollection<CategoryTypeWithCategoriesDto>> GetCategoriesGroupedByCategoryTypeAsync();
}

public class CategoryService : ICategoryService
{
    private readonly ICategoryClient _apiClient;
    private readonly ICategoryTypeClient _categoryTypeClient;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(
        ICategoryClient apiClient,
        ICategoryTypeClient categoryTypeClient,
        ILogger<CategoryService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _categoryTypeClient = categoryTypeClient ?? throw new ArgumentNullException(nameof(categoryTypeClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ICollection<CategoryListDto>> GetCategoriesAsync()
    {
        try
        {
            var result = await _apiClient.GetCategoriesAsync(pageNumber: ApiConstants.FirstPage, pageSize: ApiConstants.DefaultPageSize);
            return result?.GetItems() ?? new List<CategoryListDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[CATEGORY SERVICE] API error fetching categories: {StatusCode}", ex.StatusCode);
            return new List<CategoryListDto>();
        }
    }

    public Task<ICollection<CategoryListDto>> GetAllCategoriesAsync() => GetCategoriesAsync();

    public async Task<PaginatedResult<CategoryListDto>> GetCategoriesPagedAsync(int pageNumber, int pageSize)
    {
        try
        {
            var result = await _apiClient.GetCategoriesAsync(pageNumber, pageSize);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CATEGORY SERVICE] Error fetching paged categories (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<CategoryListDto>.Empty(pageNumber, pageSize);
        }
    }

    public async Task<CategoryDto?> GetCategoryByIdAsync(Guid categoryId)
    {
        try
        {
            var result = await _apiClient.GetCategoryByIdAsync(categoryId);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("[CATEGORY SERVICE] Category not found: {CategoryId}", categoryId);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[CATEGORY SERVICE] API error fetching category {CategoryId}: {StatusCode}", categoryId, ex.StatusCode);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CreateCategoryAsync(CreateCategoryDto dto)
    {
        try
        {
            return await _apiClient.CreateCategoryAsync(dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error creating category: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = $"API error: {ex.Message}", Errors = new List<string> { ex.Response ?? ex.Message } };
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateCategoryAsync(Guid id, Guid expectedConcurrencyStamp, UpdateCategoryDto dto)
    {
        try
        {
            return await _apiClient.UpdateCategoryAsync(id, dto, $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error updating category: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = $"API error: {ex.Message}", Errors = new List<string> { ex.Response ?? ex.Message } };
        }
    }

    public async Task<bool> DeleteCategoryAsync(Guid categoryId)
    {
        try
        {
            await _apiClient.DeleteCategoryAsync(categoryId);
            return true;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "API error deleting category: {StatusCode}", ex.StatusCode);
            return false;
        }
    }

    public async Task<ICollection<CategoryTypeWithCategoriesDto>> GetCategoriesGroupedByCategoryTypeAsync()
    {
        try
        {
            return await _categoryTypeClient.GetCategoryTypeOptionsWithCategoriesAsync() ?? new List<CategoryTypeWithCategoriesDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[CATEGORY SERVICE] API error fetching grouped categories: {StatusCode}", ex.StatusCode);
            return new List<CategoryTypeWithCategoriesDto>();
        }
    }

}
