using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.Features.ContactShareConsents.Requests.Queries;

namespace Explore.Application.Features.ContactShareConsents.Handlers.Queries;

public class GetUserContactShareConsentsQueryHandler : IQueryHandler<GetUserContactShareConsentsQuery, List<UserContactShareConsentDto>>
{
    private readonly IContactShareConsentService _consentService;

    public GetUserContactShareConsentsQueryHandler(IContactShareConsentService consentService)
    {
        _consentService = consentService;
    }

    public async Task<List<UserContactShareConsentDto>> QueryAsync(GetUserContactShareConsentsQuery request, CancellationToken cancellationToken = default)
    {
        return await _consentService.GetUserConsents(request.TenantId, request.UserId);
    }
}
