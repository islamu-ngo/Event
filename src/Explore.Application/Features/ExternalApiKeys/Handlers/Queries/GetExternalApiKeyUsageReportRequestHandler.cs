using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Lookups;
using MediatR;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Queries;

public class GetExternalApiKeyUsageReportRequestHandler : IRequestHandler<GetExternalApiKeyUsageReportRequest, List<ExternalApiKeyUsageReportDto>>
{
    private readonly IExternalApiKeyQuotaRepository _quotaRepository;
    private readonly IAdminContext _adminContext;

    public GetExternalApiKeyUsageReportRequestHandler(
        IExternalApiKeyQuotaRepository quotaRepository,
        IAdminContext adminContext)
    {
        _quotaRepository = quotaRepository;
        _adminContext = adminContext;
    }

    public async Task<List<ExternalApiKeyUsageReportDto>> Handle(GetExternalApiKeyUsageReportRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantApiKeyUsageSummary> summaries;

        if (request.TenantId.HasValue)
        {
            var isTenantAdmin = await _adminContext.IsTenantAdminAsync(request.TenantId.Value, cancellationToken);
            var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(cancellationToken);

            if (!isTenantAdmin && !isInstanceAdmin)
            {
                throw new AuthorizationException("Only tenant administrators or instance administrators can read tenant API key usage.");
            }

            summaries = await _quotaRepository.GetUsageByTenant(request.TenantId.Value, request.From, request.To, cancellationToken);
        }
        else
        {
            if (!await _adminContext.IsInstanceAdminAsync(cancellationToken))
            {
                throw new AuthorizationException("Only instance administrators can read platform-wide API key usage.");
            }

            summaries = await _quotaRepository.GetUsagePlatformWide(request.From, request.To, cancellationToken);
        }

        return summaries
            .Select(s => new ExternalApiKeyUsageReportDto
            {
                ApiKeyId = s.ApiKeyId,
                ApiKeyName = s.ApiKeyName,
                TenantId = s.TenantId,
                ExternalApiKeyOwnerTypeId = s.OwnerType,
                ExternalApiKeyOwnerTypeCode = NormalizedLookupMetadata.ExternalApiKeyOwnerType(s.OwnerType).Code,
                ExternalApiKeyOwnerTypeName = NormalizedLookupMetadata.ExternalApiKeyOwnerType(s.OwnerType).Name,
                OwnerId = s.OwnerId,
                TotalRequestCount = s.TotalRequestCount,
                TotalCreditsUsed = s.TotalCreditsUsed,
                CreditLimit = s.CreditLimit
            })
            .ToList();
    }
}
