namespace Explore.Application.Exceptions;

public sealed class IncomingWebhookEffectReceiptConflictException(Exception innerException)
    : ApplicationException(
        "A concurrent processor committed the incoming webhook effect receipt.",
        innerException);
