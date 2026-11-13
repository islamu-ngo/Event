using Explore.Application.Features.Authentication.Local.Models;

namespace Explore.Application.Contracts.Identity;

public sealed record LocalVisitorEnrollmentReservation(Guid OperationId, string EnrollmentIntent, DateTime ExpiresAt)
{
    public override string ToString() => nameof(LocalVisitorEnrollmentReservation);
}

public interface ILocalVisitorEnrollmentService
{
    Task<LocalVisitorEnrollmentReservation> ReserveAsync(
        Guid operationId, string proofDigest, CancellationToken cancellationToken);
    Task<Guid> RegisterAsync(
        LocalVisitorRegistrationRequestDto request, string proofDigest, CancellationToken cancellationToken);
}
