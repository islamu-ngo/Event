using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class SetEmailDispatchProcessorPauseStateCommandHandler(IEmailDispatchOutboxRepository repository)
    : IRequestHandler<SetEmailDispatchProcessorPauseStateCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        SetEmailDispatchProcessorPauseStateCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new SetEmailDispatchProcessorPauseStateCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Failure<Guid>(
                EmailDispatchFailureCodes.ValidationFailed,
                "Email dispatch processor pause update failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var state = await repository.SetProcessorPauseState(
            request.IsPaused,
            request.IsPaused ? request.PauseReason?.Trim() : null,
            request.ChangedBy,
            DateTime.UtcNow,
            cancellationToken);
        return BaseCommandResponse.Success(
            state.Id,
            request.IsPaused
                ? "Email dispatch processor paused."
                : "Email dispatch processor resumed.");
    }
}
