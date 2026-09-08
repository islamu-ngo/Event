using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

public sealed record UpdateMyReportCommunicationConsentCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid ReportId { get; init; }
    public required UpdateMyReportCommunicationConsentDto Request { get; init; }
}
