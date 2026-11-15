namespace Explore.Application.Features.Authentication.Local.Models;

public sealed record LocalVisitorRegistrationRequestDto(
    Guid OperationId, string Username, string Password, string? Email, string EnrollmentIntent)
{
    public override string ToString() => nameof(LocalVisitorRegistrationRequestDto);
}
