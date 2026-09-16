using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventReporting.Requests.Commands;

public sealed record UpdateMyReportCommunicationConsentCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid ReportId { get; init; }
    public required UpdateMyReportCommunicationConsentDto Request { get; init; }
}
