using Explore.Application.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface IWebPushConfigurationProvider
{
    WebPushPublicConfiguration GetPublicConfiguration();
}
