using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class ReserveLocalVisitorEnrollmentCommandHandler(ILocalVisitorEnrollmentService enrollment)
    : ICommandHandler<ReserveLocalVisitorEnrollmentCommand, LocalVisitorEnrollmentReservationCommandResponse>
{
    public async Task<LocalVisitorEnrollmentReservationCommandResponse> ExecuteAsync(
        ReserveLocalVisitorEnrollmentCommand request, CancellationToken cancellationToken = default)
    {
        string? digest = LocalVisitorBrowserProof.ComputeDigest(request.BrowserProof);
        if (request.OperationId.Version != 7 || request.OperationId.Variant is < 8 or > 11 || digest is null)
            return LocalVisitorEnrollmentReservationCommandResponse.Failure(
                BaseCommandResponse.Validation<Guid>(errors: ["Invalid enrollment correlation."], id: request.OperationId));
        try
        {
            var reservation = await enrollment.ReserveAsync(request.OperationId, digest, cancellationToken)
                .ConfigureAwait(false);
            return LocalVisitorEnrollmentReservationCommandResponse.Reserved(reservation);
        }
        catch (TenantEnrollmentClosedException)
        {
            return LocalVisitorEnrollmentReservationCommandResponse.Failure(
                BaseCommandResponse.Failure(FailureCodes.LocalEnrollmentClosed, id: request.OperationId));
        }
        catch (UnauthorizedAccessException)
        {
            return LocalVisitorEnrollmentReservationCommandResponse.Failure(
                BaseCommandResponse.Failure<Guid>(FailureCodes.LocalEnrollmentProofInvalid));
        }
    }
}
