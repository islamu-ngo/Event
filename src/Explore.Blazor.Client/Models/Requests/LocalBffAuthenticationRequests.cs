namespace Explore.Blazor.Client.Models.Requests;

internal sealed record LocalBffLoginRequest(
    string Email,
    string Password,
    bool IsPersistent,
    string ReturnUrl);

internal sealed record LocalBffRegistrationRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    bool IsPersistent,
    string ReturnUrl);

internal sealed record LocalBffAuthenticationResponse(string RedirectUrl);
