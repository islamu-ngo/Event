using Explore.Application.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface IWebPushNotificationSender
{
    Task<WebPushSendResult> SendAsync(WebPushSendEnvelope envelope, CancellationToken cancellationToken = default);
}
