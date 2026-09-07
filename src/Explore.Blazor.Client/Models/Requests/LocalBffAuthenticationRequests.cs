// ABOUTME: Immutable browser-to-BFF Local Identity request and navigation response contracts.
// ABOUTME: Keeps credentials confined to typed same-origin submissions without diagnostic values.

namespace Explore.Blazor.Client.Models.Requests;

internal sealed record LocalBffLoginRequest(
    string Identifier,
    string Password,
    bool IsPersistent,
    string ReturnUrl)
{
    public override string ToString() => nameof(LocalBffLoginRequest);
}

internal sealed record LocalBffAuthenticationResponse(string? RedirectUrl, string? ErrorCode = null);

internal sealed record LocalBffCredentialReplacementRequest(string NewPassword)
{
    public override string ToString() => nameof(LocalBffCredentialReplacementRequest);
}
