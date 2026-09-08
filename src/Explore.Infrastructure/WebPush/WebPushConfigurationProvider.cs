using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.WebPush;

public sealed class WebPushConfigurationProvider(IOptions<WebPushSettings> options) : IWebPushConfigurationProvider
{
    public WebPushPublicConfiguration GetPublicConfiguration()
    {
        var settings = options.Value;

        // Configuration binding can leave the public key absent on an instance that never enabled
        // Web Push, so the browser-safe read reports an explicitly disabled capability instead of
        // failing. The capability is advertised as enabled only when usable public material exists,
        // which keeps the flag and the key consistent for clients that branch on it.
        var publicKey = settings.VapidPublicKey?.Trim() ?? string.Empty;

        return publicKey.Length == 0
            ? new WebPushPublicConfiguration(false, string.Empty)
            : new WebPushPublicConfiguration(settings.Enabled, publicKey);
    }
}
