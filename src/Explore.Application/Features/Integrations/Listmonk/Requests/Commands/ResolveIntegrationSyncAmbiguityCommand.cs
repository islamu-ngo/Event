using Explore.Application.DTOs.Integrations;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Integrations.Listmonk.Requests.Commands;

public sealed record ResolveIntegrationSyncAmbiguityCommand(
    Guid OutboxId,
    ResolveIntegrationSyncAmbiguityDto Resolution) : ICommand<BaseCommandResponse<Guid>>;
