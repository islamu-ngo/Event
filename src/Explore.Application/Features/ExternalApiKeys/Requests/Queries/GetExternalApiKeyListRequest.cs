using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.ExternalApiKey;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Queries;

public sealed record GetExternalApiKeyListRequest : IQuery<List<ExternalApiKeyListDto>>
{
}
