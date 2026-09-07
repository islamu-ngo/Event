using System.Collections.ObjectModel;

namespace Explore.Application.Contracts.Infrastructure;

public interface ILocalJwtTokenGenerator
{
    Task<LocalIssuedToken> GenerateAsync(
        LocalJwtTokenSubject subject,
        CancellationToken cancellationToken);
}

public sealed record LocalJwtTokenSubject
{
    public LocalJwtTokenSubject(
        Guid userId,
        string email,
        string firstName,
        string lastName,
        bool emailVerified,
        IEnumerable<string> roles)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Local Identity user ID cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentNullException.ThrowIfNull(lastName);
        ArgumentNullException.ThrowIfNull(roles);
        string[] roleSnapshot = roles
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roleSnapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Local Identity roles cannot contain blank values.", nameof(roles));
        }

        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        EmailVerified = emailVerified;
        Roles = new ReadOnlyCollection<string>(roleSnapshot);
    }

    public Guid UserId { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public bool EmailVerified { get; }
    public IReadOnlyList<string> Roles { get; }
}

public sealed record LocalIssuedToken(string Token, DateTimeOffset ExpiresAt);
