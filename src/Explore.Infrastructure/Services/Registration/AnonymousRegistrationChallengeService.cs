// ABOUTME: Protects anonymous registration intent using the host's existing native Data Protection authority.
// ABOUTME: Verifies bounded SHA-256 work before reconstructing stable internal order and capability authority.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AnonymousRegistrationChallengeService(
    IDataProtectionProvider protection,
    IGuestCapabilityTokenService capabilities,
    TimeProvider timeProvider) : IAnonymousRegistrationChallengeService
{
    private const string Purpose = "Explore.AnonymousRegistrationChallenge.v1";
    private const string Operation = "start-guest-registration";
    private const string ProofPrefix = "islamu-event:anonymous-registration:v1\n";
    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    public AnonymousRegistrationChallengeDto Issue(AnonymousRegistrationChallengeBinding binding, int difficulty = 18)
    {
        if (difficulty is < 16 or > 22)
            throw new ArgumentOutOfRangeException(nameof(difficulty));

        DateTimeOffset issuedAt = timeProvider.GetUtcNow();
        var envelope = new Envelope(1, binding.TenantId, binding.EventId, Operation,
            binding.CanonicalRequestDigest, HashKey(binding.IdempotencyKey), Guid.CreateVersion7(),
            issuedAt, issuedAt.AddSeconds(120), difficulty, capabilities.Issue().RawToken);
        return new AnonymousRegistrationChallengeDto(_protector.Protect(JsonSerializer.Serialize(envelope)),
            envelope.ExpiresAt, envelope.Difficulty, envelope.Version);
    }

    public AnonymousRegistrationChallengeAuthority? Validate(
        AnonymousRegistrationChallengeBinding binding, string? protectedChallenge, string? nonce)
    {
        // Bound attacker-controlled parsing and work before native unprotection or hashing.
        if (protectedChallenge is not { Length: > 0 and <= 4096 }
            || !protectedChallenge.All(IsBase64UrlCharacter)
            || nonce is not { Length: 16 }
            || nonce.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return null;

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(_protector.Unprotect(protectedChallenge));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            // An unauthenticated or malformed bearer is an expected invalid proof, never logged.
            return null;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (envelope is null || envelope.Version != 1 || envelope.Operation != Operation
            || envelope.TenantId != binding.TenantId || envelope.EventId != binding.EventId
            || envelope.CanonicalRequestDigest != binding.CanonicalRequestDigest
            || envelope.IdempotencyKeyDigest != HashKey(binding.IdempotencyKey)
            || envelope.OrderId == Guid.Empty || envelope.OrderId.Version != 7
            || envelope.Difficulty is < 16 or > 22
            || envelope.ExpiresAt - envelope.IssuedAt != TimeSpan.FromSeconds(120)
            || envelope.ExpiresAt > DateTimeOffset.MaxValue.AddHours(-24)
            || now < envelope.IssuedAt || now >= envelope.ExpiresAt.AddHours(24)
            || envelope.GuestCapabilityToken is not { Length: 43 }
            || !envelope.GuestCapabilityToken.All(IsBase64UrlCharacter)
            || !HasProof(protectedChallenge, nonce, envelope.Difficulty))
            return null;

        var hash = CapabilityTokenHash.Create(Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(envelope.GuestCapabilityToken))));
        return new ValidatedAuthority(envelope, hash);
    }

    private static bool HasProof(string protectedChallenge, string nonce, int difficulty)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(ProofPrefix + protectedChallenge + "\n" + nonce));
        int wholeBytes = difficulty / 8;
        for (int index = 0; index < wholeBytes; index++)
            if (hash[index] != 0) return false;
        int remainingBits = difficulty % 8;
        return remainingBits == 0 || (hash[wholeBytes] >> (8 - remainingBits)) == 0;
    }

    private static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static bool IsBase64UrlCharacter(char character) => character is
        >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';

    private sealed class ValidatedAuthority(Envelope envelope, CapabilityTokenHash hash)
        : AnonymousRegistrationChallengeAuthority(envelope.TenantId, envelope.EventId, envelope.OrderId,
            envelope.ExpiresAt, envelope.GuestCapabilityToken, hash);

    private sealed record Envelope(int Version, Guid TenantId, Guid EventId, string Operation,
        string CanonicalRequestDigest, string IdempotencyKeyDigest, Guid OrderId, DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt, int Difficulty, string GuestCapabilityToken)
    {
        public override string ToString() => "AnonymousRegistrationChallengeEnvelope { Redacted = true }";
    }
}
