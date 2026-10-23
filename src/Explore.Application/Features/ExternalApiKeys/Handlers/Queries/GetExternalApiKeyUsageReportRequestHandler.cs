using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Lookups;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Queries;

public class GetExternalApiKeyUsageReportRequestHandler : IQueryHandler<GetExternalApiKeyUsageReportRequest, List<ExternalApiKeyUsageReportDto>>
{
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;
    private readonly InstanceApiKeyOperations _instanceOperations;
    private readonly IExternalApiKeyTenantUsageReader _tenantReader;

    public GetExternalApiKeyUsageReportRequestHandler(
        IAdminContext adminContext,
        IUserContext userContext,
        InstanceApiKeyOperations instanceOperations,
        IExternalApiKeyTenantUsageReader tenantReader)
    {
        _adminContext = adminContext;
        _userContext = userContext;
        _instanceOperations = instanceOperations;
        _tenantReader = tenantReader;
    }

    public async Task<List<ExternalApiKeyUsageReportDto>> QueryAsync(GetExternalApiKeyUsageReportRequest request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            throw new AuthorizationException("The authenticated application user is required.");
        IReadOnlyList<TenantApiKeyUsageSummary> summaries;

        if (request.TenantId.HasValue)
        {
            summaries = await _tenantReader.ReadAsync(request, cancellationToken);
        }
        else
        {
            if (!await _adminContext.IsInstanceAdminAsync(cancellationToken))
            {
                throw new AuthorizationException("Only instance administrators can read platform-wide API key usage.");
            }

            summaries = await _instanceOperations.ReportAsync(userId, request.From, request.To, cancellationToken);
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
