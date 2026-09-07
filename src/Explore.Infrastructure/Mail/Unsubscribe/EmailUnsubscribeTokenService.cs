using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Mail.Unsubscribe;

public sealed class EmailUnsubscribeTokenService(IDataProtectionProvider dataProtectionProvider) : IEmailUnsubscribeTokenService
{
    private const string Purpose = "ISLAMU.Email.Unsubscribe.v1";
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(180);

    private readonly ITimeLimitedDataProtector _protector = dataProtectionProvider
        .CreateProtector(Purpose)
        .ToTimeLimitedDataProtector();

    public string GenerateToken(EmailUnsubscribeTokenPayload payload, TimeSpan? lifetime = null)
    {
        var normalizedPayload = payload with
        {
            Category = NotificationPreferenceCategories.Normalize(payload.Category),
            IssuedAt = payload.IssuedAt == default ? DateTime.UtcNow : payload.IssuedAt
        };

        var json = JsonSerializer.Serialize(normalizedPayload);
        return _protector.Protect(json, lifetime ?? DefaultLifetime);
    }

    public EmailUnsubscribeTokenValidationResult ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new EmailUnsubscribeTokenValidationResult(false, null, "missing_token");
        }

        try
        {
            var json = _protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<EmailUnsubscribeTokenPayload>(json);

            if (payload is null || !NotificationPreferenceCategories.IsKnown(payload.Category))
            {
                return new EmailUnsubscribeTokenValidationResult(false, null, "invalid_payload");
            }

            var normalizedPayload = payload with
            {
                Category = NotificationPreferenceCategories.Normalize(payload.Category)
            };

            return new EmailUnsubscribeTokenValidationResult(true, normalizedPayload, null);
        }
        catch (CryptographicException)
        {
            return new EmailUnsubscribeTokenValidationResult(false, null, "invalid_or_expired_token");
        }
        catch (JsonException)
        {
            return new EmailUnsubscribeTokenValidationResult(false, null, "invalid_payload");
        }
    }
}
