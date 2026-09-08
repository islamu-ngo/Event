namespace Explore.API.Services;

public interface ICoopWebhookSignatureValidator
{
    Task<CoopWebhookSignatureValidationResult> ReadAndValidateAsync(
        HttpRequest request,
        CancellationToken cancellationToken);
}
