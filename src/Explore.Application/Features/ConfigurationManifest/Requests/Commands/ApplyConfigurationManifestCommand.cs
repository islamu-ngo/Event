namespace Explore.Application.Features.ConfigurationManifest.Requests.Commands;

using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Responses;
using MediatR;

public sealed record ApplyConfigurationManifestCommand(
    ConfigurationManifestReadResult Source)
    : IRequest<BaseCommandResponse<Guid>>;
