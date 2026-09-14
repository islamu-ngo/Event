using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.ContactShareConsents.Requests.Commands;

public sealed record WithdrawContactShareConsentCommand(
    Guid ConsentId = default,
    Guid UserId = default,
    Guid TenantId = default
) : ICommand<BaseCommandResponse<Guid>>;
