using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Models.CustomProperties;

namespace Explore.Blazor.Client.Contracts.Services.CustomProperties;

public interface ICustomPropertyAdminService
{
    Task<PaginatedResult<CustomPropertyDefinitionListDto>> GetDefinitionsAsync(
        EntityTypeName entityTypeName,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<CustomPropertyDefinitionDto?> GetDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid?> UpdateDefinitionFlagsAsync(
        DefinitionFlagUpdateModel update,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateManyDefinitionFlagsAsync(
        IReadOnlyList<DefinitionFlagUpdateModel> updates,
        CancellationToken cancellationToken = default);

    Task<PaginatedResult<CustomPropertyGovernanceRowDto>> GetGovernanceReportAsync(
        Guid? tenantId = null,
        string? scope = null,
        PromotionRecommendation? recommendation = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HalResourceOfProjectionStatusDto>> GetEventProjectionStatusAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HalResourceOfProjectionStatusDto>> GetSessionProjectionStatusAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<PaginatedResult<HalResourceOfProjectionDirtyScopeDto>> GetDirtyScopesAsync(
        Guid? tenantId = null,
        string? projectionName = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfRebuildProjectionResponseDto?> RebuildEventProjectionAsync(
        Guid tenantId,
        int? batchSize = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfRebuildProjectionResponseDto?> RebuildSessionProjectionAsync(
        Guid tenantId,
        int? batchSize = null,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfDrainDirtyScopesResponseDto?> DrainDirtyScopesAsync(
        Guid tenantId,
        string? projectionName = null,
        CancellationToken cancellationToken = default);
}
