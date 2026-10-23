namespace Explore.Application.Contracts.Moderation;

/// <summary>Authenticates only protected recognition correction, never account or ordinary erasure status access.</summary>
public interface IModerationRecognitionCorrectionReceiptAuthenticator
{
    Task<Guid?> AuthenticateAsync(string receipt, CancellationToken token);
}
