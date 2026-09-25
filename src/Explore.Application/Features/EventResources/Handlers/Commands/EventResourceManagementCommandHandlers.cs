using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventResources.Requests.Commands;
using Explore.Application.Features.EventResources.Validators;
using Explore.Application.Responses;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Commands;

public sealed class CreateEventResourceCommandHandler(EventResourceManagementWorkflow workflow)
    : ICommandHandler<CreateEventResourceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateEventResourceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new CreateEventResourceValidator().ValidateAsync(command, cancellationToken);
        return !validation.IsValid ? BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage))
            : await workflow.CreateAsync(command.EventId, command.ResourceId, command.Draft, cancellationToken);
    }
}

public sealed class UpdateEventResourceCommandHandler(EventResourceManagementWorkflow workflow)
    : ICommandHandler<UpdateEventResourceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateEventResourceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new UpdateEventResourceValidator().ValidateAsync(command, cancellationToken);
        return !validation.IsValid ? BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage))
            : await workflow.UpdateAsync(command.ResourceId, command.ExpectedVersion, command.Draft, cancellationToken);
    }
}

public sealed class PublishEventResourceCommandHandler(
    EventResourceManagementWorkflow workflow, IEventResourceDestinationProtector destinationProtector)
    : ICommandHandler<PublishEventResourceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(PublishEventResourceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new EventResourceVersionValidator().ValidateAsync((command.ResourceId, command.ExpectedVersion), cancellationToken);
        return !validation.IsValid ? BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage))
            : await workflow.ChangeStateAsync(command.ResourceId, command.ExpectedVersion,
                EventResourceManagementAction.Publish, cancellationToken, destinationProtector);
    }
}

public sealed class UnpublishEventResourceCommandHandler(EventResourceManagementWorkflow workflow)
    : ICommandHandler<UnpublishEventResourceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UnpublishEventResourceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new EventResourceVersionValidator().ValidateAsync((command.ResourceId, command.ExpectedVersion), cancellationToken);
        return !validation.IsValid ? BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage))
            : await workflow.ChangeStateAsync(command.ResourceId, command.ExpectedVersion, EventResourceManagementAction.Unpublish, cancellationToken);
    }
}

public sealed class ArchiveEventResourceCommandHandler(EventResourceManagementWorkflow workflow)
    : ICommandHandler<ArchiveEventResourceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(ArchiveEventResourceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new EventResourceVersionValidator().ValidateAsync((command.ResourceId, command.ExpectedVersion), cancellationToken);
        return !validation.IsValid ? BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage))
            : await workflow.ChangeStateAsync(command.ResourceId, command.ExpectedVersion, EventResourceManagementAction.Archive, cancellationToken);
    }
}

public sealed class DeleteEventResourceCommandHandler(EventResourceManagementWorkflow workflow)
    : ICommandHandler<DeleteEventResourceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteEventResourceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new EventResourceVersionValidator().ValidateAsync((command.ResourceId, command.ExpectedVersion), cancellationToken);
        return !validation.IsValid ? BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage))
            : await workflow.ChangeStateAsync(command.ResourceId, command.ExpectedVersion, EventResourceManagementAction.Delete, cancellationToken);
    }
}

public sealed class ModerateEventResourceCommandHandler(EventResourceManagementWorkflow workflow)
    : ICommandHandler<ModerateEventResourceCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(ModerateEventResourceCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new EventResourceVersionValidator().ValidateAsync((command.ResourceId, command.ExpectedVersion), cancellationToken);
        return !validation.IsValid ? BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage))
            : await workflow.ChangeStateAsync(command.ResourceId, command.ExpectedVersion, EventResourceManagementAction.Moderate, cancellationToken);
    }
}
