using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Models;
using Microsoft.AspNetCore.Identity;

namespace Explore.Persistence.Identity;

internal sealed class LocalIdentityAuthService : ILocalIdentityAuthService
{
    private readonly UserManager<LocalIdentityUser> _userManager;
    private readonly ILocalJwtTokenGenerator _tokenGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly LocalIdentityUser _dummyUser;
    private readonly string _dummyPasswordHash;

    public LocalIdentityAuthService(
        UserManager<LocalIdentityUser> userManager,
        ILocalJwtTokenGenerator tokenGenerator,
        TimeProvider timeProvider)
    {
        _userManager = userManager;
        _tokenGenerator = tokenGenerator;
        _timeProvider = timeProvider;
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

        return await CreateAuthenticatedResponseAsync(user, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalRegistrationResponseDto> RegisterAsync(
        LocalRegistrationRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string email = NormalizeEmail(request.Email);
        DateTime createdAt = _timeProvider.GetUtcNow().UtcDateTime;
        var user = new LocalIdentityUser
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            EmailConfirmed = false,
            LockoutEnabled = true,
            CreatedAt = createdAt
        };

        IdentityResult creation = await _userManager
            .CreateAsync(user, request.Password)
            .ConfigureAwait(false);
        if (!creation.Succeeded)
        {
            return LocalRegistrationResponseDto.Failed("registration_failed");
        }

        try
        {
            LocalAuthResponseDto authentication = await CreateAuthenticatedResponseAsync(
                user,
                cancellationToken).ConfigureAwait(false);
            return LocalRegistrationResponseDto.Registered(authentication);
        }
        catch
        {
            IdentityResult rollback = await _userManager.DeleteAsync(user).ConfigureAwait(false);
            if (!rollback.Succeeded)
            {
                throw new InvalidOperationException(
                    "Local Identity registration token issuance and credential rollback both failed.");
            }

            throw;
        }
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
                user.Id,
                email,
                user.FirstName,
                user.LastName,
                user.EmailConfirmed,
                roles),
            cancellationToken).ConfigureAwait(false);
        return LocalAuthResponseDto.Authenticated(
            user.Id,
            email,
            user.FirstName,
            user.LastName,
            user.EmailConfirmed,
            roles,
            issued.Token,
            issued.ExpiresAt);
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
