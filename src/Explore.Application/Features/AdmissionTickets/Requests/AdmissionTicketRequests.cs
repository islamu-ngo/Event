using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.AdmissionTickets;

namespace Explore.Application.Features.AdmissionTickets.Requests.Commands
{
    public sealed record RequestAdmissionTicketRecoveryCommand(string Email) :
        ICommand<AdmissionTicketRecoveryRequestResultDto>;

    public sealed record RedeemAdmissionTicketRecoveryCommand(string Capability) :
        ICommand<AdmissionTicketRecoveryConsumeResultDto>;

    public sealed record ReissueCurrentAdmissionTicketQrCommand(Guid TicketId) :
        ICommand<AdmissionTicketQrDeliveryDto>;

    public sealed record ReissueCurrentAdmissionTicketPrintCommand(Guid TicketId) :
        ICommand<AdmissionTicketPrintDeliveryDto>;
}

namespace Explore.Application.Features.AdmissionTickets.Requests.Queries
{
    public sealed record GetCurrentAdmissionTicketsQuery :
        IQuery<IReadOnlyList<AdmissionTicketDto>>;

    public sealed record GetCurrentAdmissionTicketQuery(Guid TicketId) :
        IQuery<AdmissionTicketDto>;

}
