using Explore.Application.Contracts.Identity;

namespace Explore.Application.Responses;

public sealed record LocalVisitorEnrollmentReservationCommandResponse : BaseCommandResponse<Guid>
{
    private LocalVisitorEnrollmentReservationCommandResponse(
        BaseCommandResponse<Guid> state, LocalVisitorEnrollmentReservation? reservation) : base(state, true)
    {
        Reservation = reservation;
    }

    public LocalVisitorEnrollmentReservation? Reservation { get; }

    public static LocalVisitorEnrollmentReservationCommandResponse Reserved(LocalVisitorEnrollmentReservation reservation) =>
        new(BaseCommandResponse.Success(id: reservation.OperationId), reservation);

    public static LocalVisitorEnrollmentReservationCommandResponse Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);

    public override string ToString() => nameof(LocalVisitorEnrollmentReservationCommandResponse);
}
