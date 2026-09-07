// ABOUTME: Immutable credential request for a local Identity sign-in.
// ABOUTME: Carries boundary input only and never persists or logs the submitted password.

namespace Explore.Application.Features.Authentication.Local.Models;

public sealed record LocalAuthRequestDto(string Email, string Password)
{
    public override string ToString() => nameof(LocalAuthRequestDto);
}
