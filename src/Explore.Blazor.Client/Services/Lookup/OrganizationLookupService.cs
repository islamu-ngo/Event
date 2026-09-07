using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services.Lookup;

public class OrganizationLookupService(
    IOrganizationPositionClient organizationPositionClient,
    IActorTypeClient actorTypeClient,
    IApprovalStatusClient approvalStatusClient,
    ILogger<OrganizationLookupService> logger) : IOrganizationLookupService
{
    public async Task<ICollection<OrganizationPositionListDto>> GetOrganizationPositionsAsync()
    {
        try
        {
            return await organizationPositionClient.GetOrganizationPositionsAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[OrganizationLookupService.GetOrganizationPositionsAsync] API error fetching positions. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<OrganizationPositionListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[OrganizationLookupService.GetOrganizationPositionsAsync] Unexpected error fetching positions");
            return new List<OrganizationPositionListDto>();
        }
    }

    public async Task<ICollection<ActorTypeListDto>> GetActorTypesAsync()
    {
        try
        {
            return await actorTypeClient.GetActorTypesAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[OrganizationLookupService.GetActorTypesAsync] API error fetching actor types. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<ActorTypeListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[OrganizationLookupService.GetActorTypesAsync] Unexpected error fetching actor types");
            return new List<ActorTypeListDto>();
        }
    }

    public async Task<ICollection<StatusTypeListDto>> GetApprovalStatusesAsync()
    {
        try
        {
            return await approvalStatusClient.GetApprovalStatusOptionsAsync();
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "[OrganizationLookupService.GetApprovalStatusesAsync] API error fetching approval statuses. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<StatusTypeListDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[OrganizationLookupService.GetApprovalStatusesAsync] Unexpected error fetching approval statuses");
            return new List<StatusTypeListDto>();
        }
    }
}
