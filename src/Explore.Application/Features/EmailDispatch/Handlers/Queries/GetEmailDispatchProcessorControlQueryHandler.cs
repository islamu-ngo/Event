using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Queries;

public sealed class GetEmailDispatchProcessorControlQueryHandler(IEmailDispatchOutboxRepository repository)
    : IRequestHandler<GetEmailDispatchProcessorControlQuery, EmailDispatchProcessorControlDto>
{
    public async Task<EmailDispatchProcessorControlDto> Handle(
        GetEmailDispatchProcessorControlQuery request,
        CancellationToken cancellationToken)
    {
        var state = await repository.GetProcessorState(cancellationToken);
        return state is null
            ? new EmailDispatchProcessorControlDto()
            : new EmailDispatchProcessorControlDto
            {
                ProcessorCode = state.ProcessorCode,
                IsPaused = state.IsPaused,
                PauseReason = state.PauseReason,
                PausedAt = state.PausedAt,
                GlobalSmtpRateLimitPerMinuteOverride = state.GlobalSmtpRateLimitPerMinuteOverride,
                OptionalRemindersDeferred = state.OptionalRemindersDeferred,
                UpdatedAt = state.UpdatedAt
            };
    }
}
