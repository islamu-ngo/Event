using System.Security.Cryptography;

namespace Explore.Application.Features.Authentication.Local;

public static class LocalVisitorBrowserProof
{
    public const string HeaderName = "X-Local-Enrollment-Proof";

    public static string? ComputeDigest(string proof)
    {
        if (proof is not { Length: 64 }
            || proof.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return null;
        return Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(proof)));
    }
}
