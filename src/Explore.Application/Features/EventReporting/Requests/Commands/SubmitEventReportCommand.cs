using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

public enum EventReportSubmissionChannel
{
    General = 0,
    Correction = 1,
    UnsafeExternalLink = 2,
    LegalOrCopyright = 3
}

public sealed record SubmitEventReportCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required SubmitEventReportDto Request { get; init; }
    public EventReportSubmissionChannel SubmissionChannel { get; init; }
    public string? ReporterIpHash { get; init; }
    public string? ReporterUserAgentHash { get; init; }
    public string? CorrelationId { get; init; }
}
