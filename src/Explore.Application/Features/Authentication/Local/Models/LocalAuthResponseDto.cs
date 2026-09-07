// ABOUTME: Represents a Local session, isolated replacement challenge, or closed authentication failure.
// ABOUTME: Keeps challenge authority separate from session claims and suppresses sensitive diagnostic formatting.

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;

namespace Explore.Application.Features.Authentication.Local.Models;

public enum LocalAuthFailure
{
    InvalidRequest = 1,
    InvalidCredentials = 2,
    AccountLocked = 3,
    ProviderInactive = 4,
    UserSynchronizationFailed = 5,
    AuthenticationFailed = 6,
    EmailVerificationRequired = 7
}

public enum LocalAuthOutcome
{
    Failed = 1,
    Authenticated = 2,
    ReplacementRequired = 3
}

public sealed record LocalAuthResponseDto
{
    private LocalAuthResponseDto(
        LocalAuthOutcome outcome,
        LocalAuthFailure? failure,
        Guid? userId,
        string? email,
        string? firstName,
        string? lastName,
        bool emailVerified,
        IReadOnlyList<string> roles,
        string? token,
        DateTimeOffset? expiresAt,
        LocalIssuedReplacementChallenge? replacementChallenge)
    {
        Outcome = outcome;
        Failure = failure;
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        EmailVerified = emailVerified;
        Roles = new ReadOnlyCollection<string>(roles.ToArray());
        Token = token;
        ExpiresAt = expiresAt;
        ReplacementChallenge = replacementChallenge;
    }

    [JsonIgnore]
    public LocalAuthOutcome Outcome { get; }
    public bool Success => Outcome == LocalAuthOutcome.Authenticated;
    [JsonIgnore]
    public LocalAuthFailure? Failure { get; }
    public string FailureCode => Failure switch
    {
        null => string.Empty,
        LocalAuthFailure.InvalidRequest => "invalid_request",
        LocalAuthFailure.InvalidCredentials => "invalid_credentials",
        LocalAuthFailure.AccountLocked => "account_locked",
        LocalAuthFailure.ProviderInactive => "provider_inactive",
        LocalAuthFailure.UserSynchronizationFailed => "user_sync_failed",
        LocalAuthFailure.AuthenticationFailed => "authentication_failed",
        LocalAuthFailure.EmailVerificationRequired => "email_verification_required",
        _ => throw new InvalidOperationException("Unknown Local authentication failure.")
    };
    public Guid? UserId { get; }
    public string? Email { get; }
    public string? FirstName { get; }
    public string? LastName { get; }
    public bool EmailVerified { get; }
    public IReadOnlyList<string> Roles { get; }
    public string? Token { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public LocalIssuedReplacementChallenge? ReplacementChallenge { get; }

    public static LocalAuthResponseDto Failed(LocalAuthFailure failure)
    {
        if (!Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "Unknown Local authentication failure.");
        }
        return new LocalAuthResponseDto(
            outcome: LocalAuthOutcome.Failed,
            failure: failure,
            userId: null,
            email: null,
            firstName: null,
            lastName: null,
            emailVerified: false,
            roles: Array.Empty<string>(),
            token: null,
            expiresAt: null,
            replacementChallenge: null);
    }

    public static LocalAuthResponseDto ReplacementRequired(LocalIssuedReplacementChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return new LocalAuthResponseDto(
            outcome: LocalAuthOutcome.ReplacementRequired,
            failure: null,
            userId: null,
            email: null,
            firstName: null,
            lastName: null,
            emailVerified: false,
            roles: Array.Empty<string>(),
            token: null,
            expiresAt: null,
            replacementChallenge: challenge);
    }

    public static LocalAuthResponseDto Authenticated(
        Guid userId,
        string email,
        string firstName,
        string lastName,
        bool emailVerified,
        IEnumerable<string> roles,
        string token,
        DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Authenticated user ID cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentNullException.ThrowIfNull(lastName);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        string[] roleSnapshot = roles.ToArray();
        if (roleSnapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Assigned roles cannot contain blank values.", nameof(roles));
        }

        return new LocalAuthResponseDto(
            outcome: LocalAuthOutcome.Authenticated,
            failure: null,
            userId: userId,
            email: email,
            firstName: firstName,
            lastName: lastName,
            emailVerified: emailVerified,
            roles: roleSnapshot,
            token: token,
            expiresAt: expiresAt,
            replacementChallenge: null);
    }

    public override string ToString() => nameof(LocalAuthResponseDto);
}
