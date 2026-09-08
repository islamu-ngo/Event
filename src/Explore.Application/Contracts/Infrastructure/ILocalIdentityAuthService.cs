using Explore.Application.Features.Authentication.Local.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface ILocalIdentityAuthService
{
    Task<LocalSessionValidationOutcome> ValidateSessionAsync(
        LocalSessionAuthority authority,
        CancellationToken cancellationToken);

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

public enum LocalSessionValidationOutcome
{
    Valid = 1,
    Invalid = 2,
    Unavailable = 3
}

public sealed record LocalSessionAuthority
{
    public LocalSessionAuthority(Guid localSubjectId, string securityStamp, bool emailVerified)
    {
        if (localSubjectId == Guid.Empty)
        {
            throw new ArgumentException("A Local session subject is required.", nameof(localSubjectId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        if (securityStamp.Length > 256)
        {
            throw new ArgumentException("The credential stamp exceeds its limit.", nameof(securityStamp));
        }
        LocalSubjectId = localSubjectId;
        SecurityStamp = securityStamp;
        EmailVerified = emailVerified;
    }

    public Guid LocalSubjectId { get; }
    public string SecurityStamp { get; }
    public bool EmailVerified { get; }

    public override string ToString() => nameof(LocalSessionAuthority);
}
