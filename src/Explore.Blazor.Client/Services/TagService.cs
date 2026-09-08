using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public interface ITagService
{
    Task<ICollection<TagListDto>> GetTagsAsync();
    Task<ICollection<TagListDto>> GetAllTagsAsync(); // Alias for GetTagsAsync
    Task<PaginatedResult<TagListDto>> GetTagsPagedAsync(int pageNumber, int pageSize);
    Task<TagDto?> GetTagByIdAsync(Guid tagId);
    Task<BaseCommandResponseOfGuid?> CreateTagAsync(CreateTagDto dto);
    Task<BaseCommandResponseOfGuid?> UpdateTagAsync(Guid id, UpdateTagDto dto);
    Task<bool> DeleteTagAsync(Guid tagId);
    Task<ICollection<TagTypeWithTagsDto>> GetTagsGroupedByTagTypeAsync();
}

public class TagService : ITagService
{
    private readonly ITagClient _apiClient;
    private readonly ITagTypeClient _tagTypeClient;
    private readonly ILogger<TagService> _logger;

    public TagService(ITagClient apiClient, ITagTypeClient tagTypeClient, ILogger<TagService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _tagTypeClient = tagTypeClient ?? throw new ArgumentNullException(nameof(tagTypeClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ICollection<TagListDto>> GetTagsAsync()
    {
        try
        {
            var result = await _apiClient.GetTagsAsync(pageNumber: ApiConstants.FirstPage, pageSize: ApiConstants.DefaultPageSize);
            return result?.GetItems() ?? new List<TagListDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[TAG SERVICE] API error fetching tags: {StatusCode}", ex.StatusCode);
            return new List<TagListDto>();
        }
    }

    public Task<ICollection<TagListDto>> GetAllTagsAsync() => GetTagsAsync();

    public async Task<PaginatedResult<TagListDto>> GetTagsPagedAsync(int pageNumber, int pageSize)
    {
        try
        {
            var result = await _apiClient.GetTagsAsync(pageNumber, pageSize);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TAG SERVICE] Error fetching paged tags (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<TagListDto>.Empty(pageNumber, pageSize);
        }
    }

    public async Task<TagDto?> GetTagByIdAsync(Guid tagId)
    {
        try
        {
            var result = await _apiClient.GetTagByIdAsync(tagId);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("[TAG SERVICE] Tag not found: {TagId}", tagId);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[TAG SERVICE] API error fetching tag {TagId}: {StatusCode}", tagId, ex.StatusCode);
            return null;
        }
    }

    public async Task<BaseCommandResponseOfGuid?> CreateTagAsync(CreateTagDto dto)
    {
        try
        {
            return await _apiClient.CreateTagAsync(dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[TAG SERVICE] API error creating tag: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = $"API error: {ex.Message}", Errors = new List<string> { ex.Response ?? ex.Message } };
        }
    }

    public async Task<BaseCommandResponseOfGuid?> UpdateTagAsync(Guid id, UpdateTagDto dto)
    {
        try
        {
            return await _apiClient.UpdateTagAsync(id, dto);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[TAG SERVICE] API error updating tag: {StatusCode}", ex.StatusCode);
            return new BaseCommandResponseOfGuid { Success = false, Message = $"API error: {ex.Message}", Errors = new List<string> { ex.Response ?? ex.Message } };
        }
    }

    public async Task<bool> DeleteTagAsync(Guid tagId)
    {
        try
        {
            await _apiClient.DeleteTagAsync(tagId);
            return true;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[TAG SERVICE] API error deleting tag: {StatusCode}", ex.StatusCode);
            return false;
        }
    }

    public async Task<ICollection<TagTypeWithTagsDto>> GetTagsGroupedByTagTypeAsync()
    {
        try
        {
            return await _tagTypeClient.GetTagTypesWithTagsAsync() ?? new List<TagTypeWithTagsDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[TAG SERVICE] API error fetching grouped tags: {StatusCode}", ex.StatusCode);
            return new List<TagTypeWithTagsDto>();
        }
    }

}
