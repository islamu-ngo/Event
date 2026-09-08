using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class SetEmailDispatchTenantPauseStateCommandHandler : IRequestHandler<SetEmailDispatchTenantPauseStateCommand, BaseCommandResponse<Guid>>
{
    private readonly IEmailDispatchOutboxRepository _repository;

    public SetEmailDispatchTenantPauseStateCommandHandler(IEmailDispatchOutboxRepository repository)
    {
        _repository = repository;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        SetEmailDispatchTenantPauseStateCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new SetEmailDispatchTenantPauseStateCommandValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Failure<Guid>(
                EmailDispatchFailureCodes.ValidationFailed,
                "Email dispatch tenant control update failed validation.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var changedAt = DateTime.UtcNow;
        var control = await _repository.SetTenantPauseState(
            request.TenantId,
            request.IsPaused,
            request.PauseReason,
            request.ChangedBy,
            changedAt,
            cancellationToken);

        return BaseCommandResponse.Success(
            control.Id,
            request.IsPaused
                ? "Email dispatch paused for tenant."
                : "Email dispatch resumed for tenant.");
    }
}
