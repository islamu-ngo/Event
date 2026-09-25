using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventResources.Requests.Commands;
using Explore.Application.Features.EventResources.Validators;
using Explore.Application.Responses;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Commands;

public sealed class SetEventResourceDestinationCommandHandler(
    EventResourceManagementWorkflow workflow, IEventResourceDestinationProtector protector)
    : ICommandHandler<SetEventResourceDestinationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(SetEventResourceDestinationCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await new EventResourceVersionValidator().ValidateAsync(
            (command.ResourceId, command.ExpectedVersion), cancellationToken);
        return !validation.IsValid
            ? BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage))
            : await workflow.ConfigureExternalDestinationAsync(command.ResourceId, command.ExpectedVersion,
                command.Destination, protector, cancellationToken);
    }
}
