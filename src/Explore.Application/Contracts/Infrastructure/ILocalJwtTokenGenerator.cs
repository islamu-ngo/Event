// ABOUTME: Application boundary and immutable subject contract for Local Identity JWT issuance.
// ABOUTME: Keeps signing keys and cryptographic implementation details in Infrastructure.

using System.Collections.ObjectModel;

namespace Explore.Application.Contracts.Infrastructure;

public interface ILocalJwtTokenGenerator
{
    Task<LocalIssuedToken> GenerateAsync(
        LocalJwtTokenSubject subject,
        CancellationToken cancellationToken);

    Task<LocalIssuedReplacementChallenge> GenerateReplacementChallengeAsync(
        LocalCredentialReplacementSubject subject,
        CancellationToken cancellationToken);
}

public static class LocalCredentialChallengeToken
{
    public const string Audience = "islamu-event-local-credential-replacement";
    public const string Purpose = "local-credential-replacement";
    public const string TokenType = "local-credential-replacement+jwt";
    public const string PurposeClaim = "purpose";
    public const string OperationIdClaim = "local_credential_operation";
    public const string SecurityStampClaim = "local_credential_stamp";
    public const string RequiredAlgorithm = "HS256";
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(5);
}

public static class LocalSessionToken
{
    public const string SecurityStampClaim = "local_session_stamp";
}

public sealed record LocalCredentialReplacementSubject
{
    public LocalCredentialReplacementSubject(Guid localSubjectId, Guid operationId, string securityStamp)
    {
        if (localSubjectId == Guid.Empty || operationId == Guid.Empty)
        {
            throw new ArgumentException("Replacement subject and operation identifiers are required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        if (securityStamp.Length > 256)
        {
            throw new ArgumentException("The credential stamp exceeds its limit.", nameof(securityStamp));
        }

        LocalSubjectId = localSubjectId;
        OperationId = operationId;
        SecurityStamp = securityStamp;
    }

    public Guid LocalSubjectId { get; }
    public Guid OperationId { get; }
    public string SecurityStamp { get; }

    public override string ToString() => nameof(LocalCredentialReplacementSubject);
}

public sealed record LocalIssuedReplacementChallenge
{
    public LocalIssuedReplacementChallenge(string token, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (expiresAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A UTC challenge expiry is required.", nameof(expiresAt));
        }
        Token = token;
        ExpiresAt = expiresAt;
    }

    public string Token { get; }
    public DateTimeOffset ExpiresAt { get; }

    public override string ToString() => nameof(LocalIssuedReplacementChallenge);
}

public sealed record LocalJwtTokenSubject
{
    public LocalJwtTokenSubject(
        LocalSessionAuthority authority,
        string email,
        string firstName,
        string lastName,
        IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(authority);

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

        Authority = authority;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        Roles = new ReadOnlyCollection<string>(roleSnapshot);
    }

    public LocalSessionAuthority Authority { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
    public IReadOnlyList<string> Roles { get; }

    public override string ToString() => nameof(LocalJwtTokenSubject);
}

public sealed record LocalIssuedToken(string Token, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(LocalIssuedToken);
}
