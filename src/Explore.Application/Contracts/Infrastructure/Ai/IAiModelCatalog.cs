namespace Explore.Application.Contracts.Infrastructure.Ai;

public interface IAiModelCatalog
{
    Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default);
}
