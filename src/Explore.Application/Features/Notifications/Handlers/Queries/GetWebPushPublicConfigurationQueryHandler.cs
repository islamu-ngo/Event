using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Models;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public sealed class GetWebPushPublicConfigurationQueryHandler(IWebPushConfigurationProvider provider)
    : IQueryHandler<GetWebPushPublicConfigurationQuery, WebPushPublicConfiguration>
{
    public Task<WebPushPublicConfiguration> QueryAsync(
        GetWebPushPublicConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(provider.GetPublicConfiguration());
    }
}
