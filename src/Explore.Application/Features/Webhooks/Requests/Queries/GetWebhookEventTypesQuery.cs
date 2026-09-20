using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Webhooks;

namespace Explore.Application.Features.Webhooks.Requests.Queries;

public sealed record GetWebhookEventTypesQuery : IQuery<IReadOnlyList<WebhookEventTypeDto>>;
