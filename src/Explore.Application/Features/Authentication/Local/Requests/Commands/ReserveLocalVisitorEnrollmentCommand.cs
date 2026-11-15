using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record ReserveLocalVisitorEnrollmentCommand(Guid OperationId, string BrowserProof)
    : ICommand<LocalVisitorEnrollmentReservationCommandResponse>
{
    public override string ToString() => nameof(ReserveLocalVisitorEnrollmentCommand);
}
