using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class SetEmailDispatchGlobalRateLimitOverrideCommandHandler(IEmailDispatchOutboxRepository repository)
    : IRequestHandler<SetEmailDispatchGlobalRateLimitOverrideCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        SetEmailDispatchGlobalRateLimitOverrideCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new SetEmailDispatchGlobalRateLimitOverrideCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Failure<Guid>(
                EmailDispatchFailureCodes.ValidationFailed,
                "Global SMTP rate-limit override failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var state = await repository.SetGlobalSmtpRateLimitOverride(
            request.RateLimitPerMinute,
            request.ChangedBy,
            DateTime.UtcNow,
            cancellationToken);
        return BaseCommandResponse.Success(
            state.Id,
            request.RateLimitPerMinute.HasValue
                ? "Global SMTP rate-limit override updated."
                : "Global SMTP rate-limit override cleared.");
    }
}
