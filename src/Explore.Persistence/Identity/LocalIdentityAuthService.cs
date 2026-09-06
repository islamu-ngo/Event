// ABOUTME: Implements Local Identity credential verification, issuance policy, and brute-force lockout.
// ABOUTME: Uses ASP.NET Core Identity stores and exposes tokens only after secret-backed issuance succeeds.

using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Identity;

namespace Explore.Persistence.Identity;

internal sealed class LocalIdentityAuthService : ILocalIdentityAuthService
{
    private readonly UserManager<LocalIdentityUser> _userManager;
    private readonly ILocalJwtTokenGenerator _tokenGenerator;
    private readonly ISystemSettingRepository _systemSettings;
    private readonly LocalIdentityUser _dummyUser;
    private readonly string _dummyPasswordHash;

    public LocalIdentityAuthService(
        UserManager<LocalIdentityUser> userManager,
        ILocalJwtTokenGenerator tokenGenerator,
        ISystemSettingRepository systemSettings)
    {
        _userManager = userManager;
        _tokenGenerator = tokenGenerator;
        _systemSettings = systemSettings;
        _dummyUser = new LocalIdentityUser();
        _dummyPasswordHash = userManager.PasswordHasher.HashPassword(
            _dummyUser,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    public async Task<LocalAuthResponseDto> AuthenticateAsync(
        LocalAuthRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string email = NormalizeEmail(request.Email);
        LocalIdentityUser? user = await _userManager
            .FindByEmailAsync(email)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null)
        {
            _ = _userManager.PasswordHasher.VerifyHashedPassword(
                _dummyUser,
                _dummyPasswordHash,
                request.Password);
            return LocalAuthResponseDto.Failed("invalid_credentials");
        }

        if (await _userManager.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            return LocalAuthResponseDto.Failed("account_locked");
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password).ConfigureAwait(false))
        {
            IdentityResult accessFailure = await _userManager
                .AccessFailedAsync(user)
                .ConfigureAwait(false);
            if (!accessFailure.Succeeded)
            {
                return LocalAuthResponseDto.Failed("authentication_failed");
            }

            return await _userManager.IsLockedOutAsync(user).ConfigureAwait(false)
                ? LocalAuthResponseDto.Failed("account_locked")
                : LocalAuthResponseDto.Failed("invalid_credentials");
        }

        IdentityResult reset = await _userManager
            .ResetAccessFailedCountAsync(user)
            .ConfigureAwait(false);
        if (!reset.Succeeded)
        {
            return LocalAuthResponseDto.Failed("authentication_failed");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!user.EmailConfirmed)
        {
            try
            {
                var intent = await _systemSettings.GetByKey(
                    GovernanceSettingKeys.Email.DeliveryEnabled,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (intent is not null && JsonSerializer.Deserialize<bool>(intent.Value))
                {
                    return LocalAuthResponseDto.Failed("email_verification_required");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return LocalAuthResponseDto.Failed("authentication_failed");
            }
        }

        return await CreateAuthenticatedResponseAsync(user, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Local Identity password reset is not available.");

    public Task ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Local Identity password reset is not available.");

    public Task ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Local Identity password changes are not available.");

    public Task SetTwoFactorEnabledAsync(
        Guid userId,
        bool enabled,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Local Identity two-factor authentication is not available.");

    private async Task<LocalAuthResponseDto> CreateAuthenticatedResponseAsync(
        LocalIdentityUser user,
        CancellationToken cancellationToken)
    {
        string? email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return LocalAuthResponseDto.Failed("authentication_failed");
        }

        IList<string> roles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        LocalIssuedToken issued = await _tokenGenerator.GenerateAsync(
            new LocalJwtTokenSubject(
                userId: user.Id,
                email: email,
                firstName: user.FirstName,
                lastName: user.LastName,
                emailVerified: user.EmailConfirmed,
                roles: roles),
            cancellationToken).ConfigureAwait(false);
        return LocalAuthResponseDto.Authenticated(
            userId: user.Id,
            email: email,
            firstName: user.FirstName,
            lastName: user.LastName,
            emailVerified: user.EmailConfirmed,
            roles: roles,
            token: issued.Token,
            expiresAt: issued.ExpiresAt);
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
