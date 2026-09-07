namespace Explore.Blazor.Models;

public sealed class LocalBffLoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool IsPersistent { get; init; }
    public string? ReturnUrl { get; init; }

    public override string ToString() => nameof(LocalBffLoginRequest);
}

public sealed class LocalBffRegistrationRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool IsPersistent { get; init; }
    public string? ReturnUrl { get; init; }

    public override string ToString() => nameof(LocalBffRegistrationRequest);
}
