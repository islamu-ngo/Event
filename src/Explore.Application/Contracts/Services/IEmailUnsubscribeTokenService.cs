namespace Explore.Application.Contracts.Services;

public interface IEmailUnsubscribeTokenService
{
    string GenerateToken(EmailUnsubscribeTokenPayload payload, TimeSpan? lifetime = null);

    EmailUnsubscribeTokenValidationResult ValidateToken(string? token);
}

public sealed record EmailUnsubscribeTokenPayload(
    Guid TenantId,
    Guid UserId,
    string Category,
    DateTime IssuedAt);

public sealed record EmailUnsubscribeTokenValidationResult(
    bool IsValid,
    EmailUnsubscribeTokenPayload? Payload,
    string? FailureReason);
