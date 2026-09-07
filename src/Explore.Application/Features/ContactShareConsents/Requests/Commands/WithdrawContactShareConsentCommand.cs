using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ContactShareConsents.Requests.Commands;

public sealed record WithdrawContactShareConsentCommand(
    Guid ConsentId = default,
    Guid UserId = default,
    Guid TenantId = default
) : IRequest<BaseCommandResponse<Guid>>;
