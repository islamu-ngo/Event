// ABOUTME: Implements Local Identity credential verification, issuance policy, and brute-force lockout.
// ABOUTME: Uses ASP.NET Core Identity stores and exposes tokens only after secret-backed issuance succeeds.

using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
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
    private readonly LocalIdentityCredentialStateStore _credentialStates;
    private readonly LocalIdentityUser _dummyUser;
    private readonly string _dummyPasswordHash;

    public LocalIdentityAuthService(
        UserManager<LocalIdentityUser> userManager,
        ILocalJwtTokenGenerator tokenGenerator,
        ISystemSettingRepository systemSettings,
        LocalIdentityCredentialStateStore credentialStates)
    {
        _userManager = userManager;
        _tokenGenerator = tokenGenerator;
        _systemSettings = systemSettings;
        _credentialStates = credentialStates;
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
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.InvalidCredentials);
        }

        if (await _userManager.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AccountLocked);
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password).ConfigureAwait(false))
        {
            IdentityResult accessFailure = await _userManager
                .AccessFailedAsync(user)
                .ConfigureAwait(false);
            if (!accessFailure.Succeeded)
            {
                return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AuthenticationFailed);
            }

            return await _userManager.IsLockedOutAsync(user).ConfigureAwait(false)
                ? LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AccountLocked)
                : LocalAuthResponseDto.Failed(failure: LocalAuthFailure.InvalidCredentials);
        }

        string? checkedSecurityStamp = user.SecurityStamp;
        bool checkedEmailVerified = user.EmailConfirmed;
        IdentityResult reset = await _userManager
            .ResetAccessFailedCountAsync(user)
            .ConfigureAwait(false);
        if (!reset.Succeeded)
        {
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AuthenticationFailed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(checkedSecurityStamp))
        {
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.InvalidCredentials);
        }
        try
        {
            LocalCredentialStateMetadata? credentialState = await _credentialStates
                .ReadAsync(localSubjectId: user.Id, expectedSecurityStamp: checkedSecurityStamp,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            if (credentialState?.State == LocalCredentialState.ChangeRequired)
            {
                LocalCredentialReplacementSubject? subject = await _credentialStates.ReadReplacementSubjectAsync(
                    localSubjectId: user.Id,
                    expectedSecurityStamp: checkedSecurityStamp,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (subject is null)
                {
                    return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.InvalidCredentials);
                }
                LocalIssuedReplacementChallenge challenge = await _tokenGenerator
                    .GenerateReplacementChallengeAsync(subject, cancellationToken).ConfigureAwait(false);
                return LocalAuthResponseDto.ReplacementRequired(challenge: challenge);
            }
            if (credentialState?.State != LocalCredentialState.Ready)
            {
                return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.InvalidCredentials);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AuthenticationFailed);
        }

        var authority = new LocalSessionAuthority(
            localSubjectId: user.Id, securityStamp: checkedSecurityStamp, emailVerified: checkedEmailVerified);
        (LocalIdentityUser? currentUser, LocalAuthFailure? failure) = await ReadValidatedSessionAsync(
            authority: authority, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (failure is { } rejected)
        {
            return LocalAuthResponseDto.Failed(failure: rejected);
        }
        return await CreateAuthenticatedResponseAsync(
            user: currentUser!,
            authority: authority,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LocalSessionValidationOutcome> ValidateSessionAsync(
        LocalSessionAuthority authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        var (_, failure) = await ReadValidatedSessionAsync(
            authority: authority, cancellationToken: cancellationToken).ConfigureAwait(false);
        return failure switch
        {
            null => LocalSessionValidationOutcome.Valid,
            LocalAuthFailure.AuthenticationFailed => LocalSessionValidationOutcome.Unavailable,
            _ => LocalSessionValidationOutcome.Invalid
        };
    }

    private async Task<(LocalIdentityUser? User, LocalAuthFailure? Failure)> ReadValidatedSessionAsync(
        LocalSessionAuthority authority,
        CancellationToken cancellationToken)
    {
        try
        {
            LocalIdentityUser? user = await _credentialStates.ReadReadySessionAsync(authority, cancellationToken)
                .ConfigureAwait(false);
            if (user is null)
            {
                return (User: null, Failure: LocalAuthFailure.InvalidCredentials);
            }
            if (!user.EmailConfirmed)
            {
                var intent = await _systemSettings.GetByKey(
                    GovernanceSettingKeys.Email.DeliveryEnabled, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (intent is not null && JsonSerializer.Deserialize<bool>(intent.Value))
                {
                    return (User: null, Failure: LocalAuthFailure.EmailVerificationRequired);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return (User: user, Failure: null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (User: null, Failure: LocalAuthFailure.AuthenticationFailed);
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
        LocalSessionAuthority authority,
        CancellationToken cancellationToken)
    {
        string? email = user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AuthenticationFailed);
        }

        IList<string> roles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        LocalIssuedToken issued = await _tokenGenerator.GenerateAsync(
            new LocalJwtTokenSubject(
                authority: authority,
                email: email,
                firstName: user.FirstName,
                lastName: user.LastName,
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
