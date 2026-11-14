using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services;

/// <summary>Protects local enrollment correlation data; receipt and browser proof checks remain mandatory.</summary>
public sealed class LocalVisitorEnrollmentIntentProtector(IDataProtectionProvider provider, TimeProvider clock)
    : ILocalVisitorEnrollmentIntentProtector
{
    private const int MaximumProtectedLength = 2048;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly IDataProtector _protector = provider.CreateProtector("ISLAMU.LocalVisitorEnrollment.Intent.v1");

    public string Protect(LocalVisitorEnrollmentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!IsValid(intent, clock.GetUtcNow().UtcDateTime))
            throw new ArgumentException("Invalid local visitor enrollment intent binding.", nameof(intent));

        return _protector.Protect(JsonSerializer.Serialize(intent));
    }

    public LocalVisitorEnrollmentIntent? Unprotect(string protectedIntent)
    {
        if (string.IsNullOrWhiteSpace(protectedIntent) || protectedIntent.Length > MaximumProtectedLength)
            return null;

        string plaintext;
        try
        {
            plaintext = _protector.Unprotect(protectedIntent);
        }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }

        LocalVisitorEnrollmentIntent? intent;
        try
        {
            intent = JsonSerializer.Deserialize<LocalVisitorEnrollmentIntent>(plaintext);
        }
        catch (JsonException) { return null; }

        return intent is not null && IsValid(intent, clock.GetUtcNow().UtcDateTime) ? intent : null;
    }

    private static bool IsValid(LocalVisitorEnrollmentIntent intent, DateTime now)
    {
        if (intent.OperationId == Guid.Empty || intent.TenantId == Guid.Empty
            || intent.ProofDigest is not { Length: 64 }
            || intent.ExpiresAt.Kind != DateTimeKind.Utc
            || intent.ExpiresAt <= now || intent.ExpiresAt - now > MaximumLifetime)
            return false;

        foreach (var character in intent.ProofDigest)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;

        return true;
    }
}
