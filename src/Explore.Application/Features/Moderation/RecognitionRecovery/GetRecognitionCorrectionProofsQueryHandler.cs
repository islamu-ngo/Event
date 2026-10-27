using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

public sealed class GetRecognitionCorrectionProofsQueryHandler(
    IModerationRecognitionCorrectionReceiptAuthenticator authenticator, ModerationRecognitionRecovery recovery)
    : IQueryHandler<GetRecognitionCorrectionProofsQuery, RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionCorrectionProofDto>>>
{
    public async Task<RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionCorrectionProofDto>>> QueryAsync(
        GetRecognitionCorrectionProofsQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await new GetRecognitionCorrectionProofsQueryValidator().ValidateAsync(query, cancellationToken);
        if (!validation.IsValid || await authenticator.AuthenticateAsync(query.Receipt, cancellationToken) != query.AuthenticatedIntentId)
            return RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionCorrectionProofDto>>.Failed(
                BaseCommandResponse.Authentication<Guid>());
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(query.Receipt));
        try
        {
            var rows = await recovery.ReadRemediesAsync(hash, cancellationToken);
            // Authentication is rechecked at the read boundary; expired or mixed-intent proof never becomes status authority.
            if (rows.Count == 0 || rows.Any(row => row.IntentId != query.AuthenticatedIntentId))
                return RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionCorrectionProofDto>>.Failed(
                    BaseCommandResponse.Authentication<Guid>());
            return RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionCorrectionProofDto>>.Found(query.AuthenticatedIntentId,
                Array.AsReadOnly(rows.Select(row => new RecognitionCorrectionProofDto(row.Id, row.IntentId, row.SourceId,
                    row.PendingRevision, row.Grounds, row.ProtectedAt, row.RemedyUntil, row.RetainUntil)).ToArray()));
        }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }
}
