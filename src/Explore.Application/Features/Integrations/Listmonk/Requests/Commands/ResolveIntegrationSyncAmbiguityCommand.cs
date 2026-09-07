using Explore.Application.DTOs.Integrations;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Integrations.Listmonk.Requests.Commands;

public sealed record ResolveIntegrationSyncAmbiguityCommand(
    Guid OutboxId,
    ResolveIntegrationSyncAmbiguityDto Resolution) : IRequest<BaseCommandResponse<Guid>>;
