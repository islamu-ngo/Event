using Explore.Application.Contracts.Services;
using Explore.Application.Features.ContactShareConsents.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ContactShareConsents.Handlers.Commands;

public class WithdrawContactShareConsentCommandHandler : IRequestHandler<WithdrawContactShareConsentCommand, BaseCommandResponse<Guid>>
{
    private readonly IContactShareConsentService _consentService;
    private readonly ILogger<WithdrawContactShareConsentCommandHandler> _logger;

    public WithdrawContactShareConsentCommandHandler(
        IContactShareConsentService consentService,
        ILogger<WithdrawContactShareConsentCommandHandler> logger)
    {
        _consentService = consentService;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(WithdrawContactShareConsentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await _consentService.WithdrawConsent(request.TenantId, request.UserId, request.ConsentId);

            return BaseCommandResponse.Success(
                request.ConsentId,
                "Contact sharing consent withdrawn successfully.");
        }
        catch (KeyNotFoundException ex)
        {
            return BaseCommandResponse.Validation<Guid>([ex.Message], ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BaseCommandResponse.Validation<Guid>([ex.Message], ex.Message);
        }
    }
}
