using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models.CustomProperties;

namespace Explore.Blazor.Client.Contracts.Services.CustomProperties;

public interface ICustomPropertyValueService
{
    Task<IReadOnlyList<CustomPropertyValueModel>> GetEventValuesAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> SetEventValueAsync(Guid definitionId, Guid eventId, CustomPropertyValueModel model, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> SetEventMultiValuesAsync(Guid definitionId, Guid eventId, IEnumerable<CustomPropertyValueModel> models, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomPropertyValueModel>> GetEventSessionValuesAsync(Guid eventSessionId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> SetEventSessionValueAsync(Guid definitionId, Guid eventSessionId, CustomPropertyValueModel model, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid?> SetEventSessionMultiValuesAsync(Guid definitionId, Guid eventSessionId, IEnumerable<CustomPropertyValueModel> models, CancellationToken cancellationToken = default);
}
