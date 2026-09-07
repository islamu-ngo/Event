using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Services;
using Explore.Domain.ValueObjects;

namespace Explore.Infrastructure.Services;

public sealed class GuestCapabilityTokenService : IGuestCapabilityTokenService
{
    public GuestCapabilityTokenIssue Issue()
    {
        Span<byte> tokenBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(tokenBytes);

        string rawToken = Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

        return new GuestCapabilityTokenIssue(rawToken, CapabilityTokenHash.Create(Convert.ToBase64String(hashBytes)));
    }

    public bool Matches(string? rawToken, CapabilityTokenHash expectedHash)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || expectedHash is null)
        {
            return false;
        }

        byte[] candidateHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        byte[] expectedHashBytes = Convert.FromBase64String(expectedHash.Value);

        return candidateHashBytes.Length == expectedHashBytes.Length
            && CryptographicOperations.FixedTimeEquals(candidateHashBytes, expectedHashBytes);
    }
}
