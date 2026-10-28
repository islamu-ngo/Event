using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Services.Moderation;

public sealed class ModerationRecognitionCorrectionReceiptAuthenticator(
    IModerationRecognitionRecoveryRepository recovery) : IModerationRecognitionCorrectionReceiptAuthenticator
{
    public async Task<Guid?> AuthenticateAsync(string receipt, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(receipt) || receipt.Length > 128) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(receipt));
        try
        {
            var remedies = await recovery.ReadRemediesAsync(hash, token);
            return remedies.Count == 0 ? null : remedies.Select(proof => proof.IntentId).Distinct().Single();
        }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }
}
