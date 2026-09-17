using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PrivacyErasure;

namespace Explore.Application.Features.PrivacyErasure.Requests.Queries;

public sealed record GetPrivacyErasureStatusQuery(Guid IntentId)
    : IQuery<PrivacyErasureStatusDto?>;
