using Explore.Application.DTOs.PrivacyErasure;
using MediatR;

namespace Explore.Application.Features.PrivacyErasure.Requests.Queries;

public sealed record GetPrivacyErasureStatusQuery(Guid IntentId)
    : IRequest<PrivacyErasureStatusDto?>;
