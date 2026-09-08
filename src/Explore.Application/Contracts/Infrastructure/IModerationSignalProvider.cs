using Explore.Application.Features.EventReporting.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface IModerationSignalProvider
{
    Task<EventSafetySignalProviderResult> EvaluateAsync(
        EventReportProviderEnvelope envelope,
        CancellationToken cancellationToken = default);
}
