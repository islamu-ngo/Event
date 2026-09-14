using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.ExternalApiKey;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Queries;

public sealed record GetExternalApiKeyUsageReportRequest : IQuery<List<ExternalApiKeyUsageReportDto>>
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public Guid? TenantId { get; init; }
}
