using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.Features.ContactShareConsents.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ContactShareConsents.Handlers.Queries;

public class GetUserContactShareConsentsQueryHandler : IRequestHandler<GetUserContactShareConsentsQuery, List<UserContactShareConsentDto>>
{
    private readonly IContactShareConsentService _consentService;

    public GetUserContactShareConsentsQueryHandler(IContactShareConsentService consentService)
    {
        _consentService = consentService;
    }

    public async Task<List<UserContactShareConsentDto>> Handle(GetUserContactShareConsentsQuery request, CancellationToken cancellationToken)
    {
        return await _consentService.GetUserConsents(request.TenantId, request.UserId);
    }
}
