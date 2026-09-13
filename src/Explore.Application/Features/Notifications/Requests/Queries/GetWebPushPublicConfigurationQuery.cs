using Explore.Application.Models;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Notifications.Requests.Queries;

public sealed record GetWebPushPublicConfigurationQuery : IQuery<WebPushPublicConfiguration>;
