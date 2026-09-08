using Explore.Application.DTOs.ContactShareConsent;
using MediatR;

namespace Explore.Application.Features.ContactShareConsents.Requests.Queries;

public sealed record GetUserContactShareConsentsQuery(
    Guid UserId = default,
    Guid TenantId = default
) : IRequest<List<UserContactShareConsentDto>>;
