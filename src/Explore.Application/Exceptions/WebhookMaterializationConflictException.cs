namespace Explore.Application.Exceptions;

public sealed class WebhookMaterializationConflictException : InvalidOperationException
{
    public WebhookMaterializationConflictException(string message)
        : base(message)
    {
    }
}
