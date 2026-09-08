namespace Explore.Application.Features.ConfigurationManifest.Application;

using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Responses;

public interface IConfigurationManifestApplier
{
    Task<BaseCommandResponse<Guid>> ApplyAsync(
        ConfigurationManifestReadResult source,
        CancellationToken cancellationToken);
}
