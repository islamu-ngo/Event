using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.ContactShareConsent;

namespace Explore.Application.Features.ContactShareConsents.Requests.Queries;

public sealed record GetUserContactShareConsentsQuery(
    Guid UserId = default,
    Guid TenantId = default
) : IQuery<List<UserContactShareConsentDto>>;
