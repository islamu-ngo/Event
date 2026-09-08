namespace Explore.API.Services;

public interface IIncomingWebhookIntakeService
{
    Task<IncomingWebhookReadResult> ReadAndVerifyAsync(
        HttpRequest request,
        string provider,
        long maxBodyBytes,
        CancellationToken cancellationToken);

    Task<IncomingWebhookCaptureResult> CaptureAsync(
        IncomingWebhookReadResult readResult,
        CancellationToken cancellationToken);
}
