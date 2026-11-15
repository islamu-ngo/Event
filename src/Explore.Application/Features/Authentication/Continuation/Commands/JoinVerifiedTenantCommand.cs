using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Continuation.Commands;

public sealed record JoinVerifiedTenantCommand(ProviderAccountKey AccountKey, Guid TenantId)
    : ICommand<BaseCommandResponse<Guid>>;
