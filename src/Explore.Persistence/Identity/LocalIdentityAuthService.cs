using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

internal sealed class LocalIdentityAuthService : ILocalIdentityAuthService
{
    private readonly UserManager<LocalIdentityUser> _userManager;
    private readonly ILocalJwtTokenGenerator _tokenGenerator;
    private readonly LocalIdentityCredentialStateStore _credentialStates;
    private readonly LocalIdentityUser _dummyUser;
    private readonly string _dummyPasswordHash;

    public LocalIdentityAuthService(
        UserManager<LocalIdentityUser> userManager,
        ILocalJwtTokenGenerator tokenGenerator,
        LocalIdentityCredentialStateStore credentialStates)
    {
        _userManager = userManager;
        _tokenGenerator = tokenGenerator;
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
        var outcome = await _credentialStates.CreateAuthenticationExecutionStrategy().ExecuteAsync(async () =>
        {
            LocalIdentityCredentialStateStore.AuthenticationAttempt? attempt = null;
            LocalAuthResponseDto? response = null;
            Exception? failure = null;
            try
            {
                attempt = await _credentialStates.BeginAuthenticationAsync(cancellationToken).ConfigureAwait(false);
                response = await AuthenticateAttemptAsync(request, attempt, cancellationToken).ConfigureAwait(false);
                _credentialStates.RequireSelectedLifecycleCurrent();
                await attempt.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            if (attempt is not null)
                try { await attempt.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup)
                {
                    failure = failure is null ? cleanup
                        : new AggregateException("Authentication and its original owner cleanup both failed.", failure, cleanup);
                }
            // Leave the strategy before rethrowing: uncertain settlement and mutated tracked state cannot replay a password attempt.
            return (Response: response, Failure: failure is null ? null : ExceptionDispatchInfo.Capture(failure));
        }).ConfigureAwait(false);
        outcome.Failure?.Throw();
        return outcome.Response ?? throw new InvalidOperationException("Authentication produced no settlement outcome.");
    }

    private async Task<LocalAuthResponseDto> AuthenticateAttemptAsync(
        LocalAuthRequestDto request, LocalIdentityCredentialStateStore.AuthenticationAttempt attempt,
        CancellationToken cancellationToken)
    {
        string identifier = request.Identifier.Trim();
        LocalIdentityUser? user = identifier.Contains('@', StringComparison.Ordinal)
            ? await _userManager.FindByEmailAsync(identifier).ConfigureAwait(false)
            : await _userManager.FindByNameAsync(identifier).ConfigureAwait(false);
        _credentialStates.RequireSelectedLifecycleCurrent();
        cancellationToken.ThrowIfCancellationRequested();
        if (user is null)
        {
            _ = _userManager.PasswordHasher.VerifyHashedPassword(
                _dummyUser,
                _dummyPasswordHash,
                request.Password);
            cancellationToken.ThrowIfCancellationRequested();
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.InvalidCredentials);
        }

        bool locked = await _userManager.IsLockedOutAsync(user).ConfigureAwait(false);
        _credentialStates.RequireSelectedLifecycleCurrent();
        cancellationToken.ThrowIfCancellationRequested();
        if (locked)
        {
            await _credentialStates.PublishAuthenticationLockoutAsync(user, cancellationToken).ConfigureAwait(false);
            _ = _userManager.PasswordHasher.VerifyHashedPassword(
                _dummyUser,
                _dummyPasswordHash,
                request.Password);
            cancellationToken.ThrowIfCancellationRequested();
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AccountLocked);
        }

        bool passwordValid = await _userManager.CheckPasswordAsync(user, request.Password).ConfigureAwait(false);
        _credentialStates.RequireSelectedLifecycleCurrent();
        cancellationToken.ThrowIfCancellationRequested();
        if (!passwordValid)
        {
            IdentityResult accessFailure = await _userManager
                .AccessFailedAsync(user)
                .ConfigureAwait(false);
            _credentialStates.RequireSelectedLifecycleCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            if (!accessFailure.Succeeded)
            {
                return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AuthenticationFailed);
            }

            await _credentialStates.PublishAuthenticationLockoutAsync(user, cancellationToken).ConfigureAwait(false);

            bool lockedAfterFailure = await _userManager.IsLockedOutAsync(user).ConfigureAwait(false);
            _credentialStates.RequireSelectedLifecycleCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            return lockedAfterFailure
                ? LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AccountLocked)
                : LocalAuthResponseDto.Failed(failure: LocalAuthFailure.InvalidCredentials);
        }

        string? checkedSecurityStamp = user.SecurityStamp;
        bool checkedEmailVerified = user.EmailConfirmed;
        IdentityResult reset = await _userManager
            .ResetAccessFailedCountAsync(user)
            .ConfigureAwait(false);
        _credentialStates.RequireSelectedLifecycleCurrent();
        if (!reset.Succeeded)
        {
            return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.AuthenticationFailed);
        }

        await _credentialStates.PublishAuthenticationLockoutAsync(user, cancellationToken).ConfigureAwait(false);

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
                LocalCredentialReplacementSubject? subject = await attempt.ReadReplacementSubjectAsync(
                    localSubjectId: user.Id,
                    expectedSecurityStamp: checkedSecurityStamp,
                    token: cancellationToken).ConfigureAwait(false);
                if (subject is null)
                {
                    return LocalAuthResponseDto.Failed(failure: LocalAuthFailure.InvalidCredentials);
                }
                LocalIssuedReplacementChallenge challenge = await _tokenGenerator
                    .GenerateReplacementChallengeAsync(subject, cancellationToken).ConfigureAwait(false);
                _credentialStates.RequireSelectedLifecycleCurrent();
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
        _credentialStates.RequireSelectedLifecycleCurrent();
        var (_, failure) = await ReadValidatedSessionAsync(
            authority: authority, cancellationToken: cancellationToken).ConfigureAwait(false);
        _credentialStates.RequireSelectedLifecycleCurrent();
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
                string? intent = await _credentialStates.ReadEmailDeliveryEnabledValueAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (intent is not null && JsonSerializer.Deserialize<bool>(intent))
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

}
