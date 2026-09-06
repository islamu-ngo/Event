// ABOUTME: Application boundary for local Identity credential operations and platform token issuance.
// ABOUTME: Keeps ASP.NET Core Identity and signing implementation details outside the application layer.

using Explore.Application.Features.Authentication.Local.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface ILocalIdentityAuthService
{
    Task<LocalAuthResponseDto> AuthenticateAsync(
        LocalAuthRequestDto request,
        CancellationToken cancellationToken);

    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    Task ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken);

    Task ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);

    Task SetTwoFactorEnabledAsync(
        Guid userId,
        bool enabled,
        CancellationToken cancellationToken);
}
