using Explore.Application.DTOs.ExternalApiKey;
using MediatR;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Queries;

public sealed record GetExternalApiKeyDetailsRequest(Guid Id = default) : IRequest<ExternalApiKeyListDto?>;
