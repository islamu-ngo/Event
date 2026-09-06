// ABOUTME: Browser-to-BFF request model for Local Identity login.
// ABOUTME: Redacts credential values from diagnostic text while carrying safe return navigation.

namespace Explore.Blazor.Models;

public sealed class LocalBffLoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool IsPersistent { get; init; }
    public string? ReturnUrl { get; init; }

    public override string ToString() => nameof(LocalBffLoginRequest);
}
