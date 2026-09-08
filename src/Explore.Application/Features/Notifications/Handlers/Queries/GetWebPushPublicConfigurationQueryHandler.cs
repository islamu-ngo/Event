using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Models;
using MediatR;

namespace Explore.Application.Features.Notifications.Handlers.Queries;

public sealed class GetWebPushPublicConfigurationQueryHandler(IWebPushConfigurationProvider provider)
    : IRequestHandler<GetWebPushPublicConfigurationQuery, WebPushPublicConfiguration>
{
    public Task<WebPushPublicConfiguration> Handle(
        GetWebPushPublicConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(provider.GetPublicConfiguration());
    }
}
