using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EmailDispatch.Handlers.Queries;

public sealed class GetEmailDispatchProcessorControlQueryHandler(IEmailDispatchOutboxRepository repository)
    : IQueryHandler<GetEmailDispatchProcessorControlQuery, EmailDispatchProcessorControlDto>
{
    public async Task<EmailDispatchProcessorControlDto> QueryAsync(
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
