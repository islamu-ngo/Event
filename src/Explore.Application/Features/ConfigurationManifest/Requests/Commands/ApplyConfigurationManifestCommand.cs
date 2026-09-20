namespace Explore.Application.Features.ConfigurationManifest.Requests.Commands;

using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

public sealed record ApplyConfigurationManifestCommand(
    ConfigurationManifestReadResult Source)
    : ICommand<BaseCommandResponse<Guid>>;
