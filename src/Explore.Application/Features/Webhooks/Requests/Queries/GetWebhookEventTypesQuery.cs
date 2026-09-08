using Explore.Application.DTOs.Webhooks;
using MediatR;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

public sealed record GetWebhookEventTypesQuery : IRequest<IReadOnlyList<WebhookEventTypeDto>>;
