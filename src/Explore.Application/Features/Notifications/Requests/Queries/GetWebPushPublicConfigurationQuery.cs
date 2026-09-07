using Explore.Application.Models;
using MediatR;

namespace Explore.Application.Features.Notifications.Requests.Queries;

public sealed record GetWebPushPublicConfigurationQuery : IRequest<WebPushPublicConfiguration>;
