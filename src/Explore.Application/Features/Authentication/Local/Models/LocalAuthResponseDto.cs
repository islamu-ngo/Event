using System.Collections.ObjectModel;

namespace Explore.Application.Features.Authentication.Local.Models;

public sealed record LocalAuthResponseDto
{
    private LocalAuthResponseDto(
        bool success,
        string failureCode,
        Guid? userId,
        string? email,
        string? firstName,
        string? lastName,
        bool emailVerified,
        IReadOnlyList<string> roles,
        string? token,
        DateTimeOffset? expiresAt)
    {
        Success = success;
        FailureCode = failureCode;
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        EmailVerified = emailVerified;
        Roles = new ReadOnlyCollection<string>(roles.ToArray());
        Token = token;
        ExpiresAt = expiresAt;
    }

    public bool Success { get; }
    public string FailureCode { get; }
    public Guid? UserId { get; }
    public string? Email { get; }
    public string? FirstName { get; }
    public string? LastName { get; }
    public bool EmailVerified { get; }
    public IReadOnlyList<string> Roles { get; }
    public string? Token { get; }
    public DateTimeOffset? ExpiresAt { get; }

    public static LocalAuthResponseDto Failed(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return new LocalAuthResponseDto(
            false,
            failureCode,
            null,
            null,
            null,
            null,
            false,
            Array.Empty<string>(),
            null,
            null);
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
            true,
            string.Empty,
            userId,
            email,
            firstName,
            lastName,
            emailVerified,
            roleSnapshot,
            token,
            expiresAt);
    }
}
