namespace Explore.Application.Features.Authentication.Local.Models;

public sealed record LocalRegistrationRequestDto(
    string Email,
    string Password,
    string FirstName,
    string LastName);
