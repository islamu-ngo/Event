using Explore.Application.DTOs.ExternalApiKey;
using MediatR;

namespace Explore.Application.Features.ExternalApiKeys.Requests.Queries;

public sealed record GetExternalApiKeyUsageReportRequest : IRequest<List<ExternalApiKeyUsageReportDto>>
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public Guid? TenantId { get; init; }
}
