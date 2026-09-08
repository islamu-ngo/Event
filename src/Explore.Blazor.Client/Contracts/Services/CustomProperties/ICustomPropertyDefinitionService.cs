using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Models.CustomProperties;

namespace Explore.Blazor.Client.Contracts.Services.CustomProperties;

public interface ICustomPropertyDefinitionService
{
    Task<PaginatedResult<CustomPropertyDefinitionListDto>> GetDefinitionsAsync(
        EntityTypeName entityTypeName,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<CustomPropertyDefinitionDto?> GetDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomPropertyDefinitionDto>> GetEventDefinitionsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomPropertyDefinitionDto>> GetEventSessionDefinitionsAsync(
        Guid eventSessionId,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid?> CreateDefinitionAsync(
        CreateCustomPropertyDefinitionDto body,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid?> UpdateDefinitionAsync(
        Guid id,
        Guid expectedConcurrencyStamp,
        UpdateCustomPropertyDefinitionDto body,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
