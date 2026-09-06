// ABOUTME: Immutable browser-to-BFF Local Identity request and navigation response contracts.
// ABOUTME: Keeps credentials confined to typed same-origin submissions without diagnostic values.

namespace Explore.Blazor.Client.Models.Requests;

internal sealed record LocalBffLoginRequest(
    string Email,
    string Password,
    bool IsPersistent,
    string ReturnUrl);

internal sealed record LocalBffAuthenticationResponse(string? RedirectUrl, string? ErrorCode = null);
