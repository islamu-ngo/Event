using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;

namespace Explore.Application.Features.AiAssistant.Handlers.Queries;

public sealed class DiscoverAiAssistantModelsQueryHandler(
    IQueryHandler<GetTenantOnboardingStatusQuery, TenantOnboardingStatusDto> onboardingStatus,
    IAiModelDiscoveryGateway gateway)
    : IQueryHandler<DiscoverAiAssistantModelsQuery, AiModelDiscoveryResult>
{
    public async Task<AiModelDiscoveryResult> QueryAsync(
        DiscoverAiAssistantModelsQuery query, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(query.Request.Scope))
            return new([], "invalid_scope");

        var status = await onboardingStatus.QueryAsync(new GetTenantOnboardingStatusQuery(), cancellationToken);
        bool authorized = status.IsAuthenticated && (query.Request.Scope == AiModelDiscoveryScope.Instance
            ? status.IsCurrentUserPlatformAdministrator
            : status.IsCurrentUserTenantAdministrator || status.IsCurrentUserPlatformAdministrator);
        if (!authorized)
            return new([], "forbidden");

        if (!Enum.IsDefined(query.Request.CredentialScope))
            return new([], "invalid_settings");

        return await gateway.DiscoverAsync(query.Request, cancellationToken);
    }
}
