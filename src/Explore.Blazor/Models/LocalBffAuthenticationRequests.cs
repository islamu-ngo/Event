// ABOUTME: Browser-to-BFF request model for Local Identity login.
// ABOUTME: Redacts credential values from diagnostic text while carrying safe return navigation.

using System.Text.Json.Serialization;

namespace Explore.Blazor.Models;

public sealed class LocalBffLoginRequest
{
    public string Identifier { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool IsPersistent { get; init; }
    public string? ReturnUrl { get; init; }

    public override string ToString() => nameof(LocalBffLoginRequest);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LocalBffCredentialReplacementRequest
{
    public string NewPassword { get; init; } = string.Empty;

    public override string ToString() => nameof(LocalBffCredentialReplacementRequest);
}
