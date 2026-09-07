using Explore.Application.Features.Authentication.Local.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface ILocalIdentityAuthService
{
    Task<LocalAuthResponseDto> AuthenticateAsync(
        LocalAuthRequestDto request,
        CancellationToken cancellationToken);

    Task<LocalRegistrationResponseDto> RegisterAsync(
        LocalRegistrationRequestDto request,
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
