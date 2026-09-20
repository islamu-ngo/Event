namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Responses;

public sealed class ApplyConfigurationManifestCommandHandler(
    IConfigurationManifestApplier applier)
    : ICommandHandler<ApplyConfigurationManifestCommand, BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ApplyConfigurationManifestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return applier.ApplyAsync(request.Source, cancellationToken);
    }
}
