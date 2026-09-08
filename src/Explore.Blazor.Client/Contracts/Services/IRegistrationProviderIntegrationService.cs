using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IRegistrationProviderIntegrationService
{
    Task<HalCollectionResourceOfRegistrationProviderConnectionDto> GetConnectionsAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateConnectionAsync(Guid tenantId, Guid eventId, HalLink link, RegistrationProviderConnectionRequestDto request, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationProviderConnectionDto> GetConnectionAsync(Guid tenantId, Guid eventId, Guid connectionId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateConnectionAsync(Guid tenantId, Guid eventId, Guid connectionId, HalLink link, RegistrationProviderConnectionRequestDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> DeleteConnectionAsync(Guid tenantId, Guid eventId, Guid connectionId, HalLink link, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> ReplaceApprovedOriginsAsync(Guid tenantId, Guid eventId, Guid connectionId, HalLink link, ReplaceRegistrationProviderApprovedOriginsRequestDto request, CancellationToken cancellationToken = default);
    Task<HalCollectionResourceOfRegistrationProviderBindingDto> GetBindingsAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateBindingAsync(Guid tenantId, Guid eventId, HalLink link, RegistrationProviderBindingRequestDto request, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationProviderBindingDto> GetBindingAsync(Guid tenantId, Guid eventId, Guid bindingId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateBindingAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, RegistrationProviderBindingRequestDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> ReplaceMappingsAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, ReplaceRegistrationProviderMappingsRequestDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> DeleteBindingAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> PublishBindingAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, CancellationToken cancellationToken = default);
    Task<HalCollectionResourceOfRegistrationChannelDto> GetChannelsAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateChannelAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, HalLink link, RegistrationChannelRequestDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateChannelAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, HalLink link, RegistrationChannelRequestDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> DeleteChannelAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, HalLink link, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationProviderLaunchDescriptorDto> GetLaunchDescriptorAsync(RegistrationProviderLaunchLineage lineage, CancellationToken cancellationToken = default);
    Task<HalCollectionResourceOfRegistrationProviderBindingHealthDto> GetHealthAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default);
    Task<HalCollectionResourceOfRegistrationProviderParkedQueueItemDto> GetQueueAsync(Guid tenantId, Guid eventId, int? limit = null, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> PollReconciliationAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> QueueManualImportAsync(Guid tenantId, Guid eventId, HalLink link, ManualRegistrationProviderImportRequestDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> RetryQueueItemAsync(Guid tenantId, Guid eventId, HalLink link, RetryRegistrationProviderParkedItemRequestDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> ResolveQueueItemAsync(Guid tenantId, Guid eventId, HalLink link, ResolveRegistrationProviderQueueItemRequestDto request, CancellationToken cancellationToken = default);
}

public readonly record struct RegistrationProviderLaunchLineage(
    Guid TenantId,
    Guid EventId,
    Guid WorkflowId,
    Guid RequirementId,
    Guid ChannelId,
    Guid BindingId,
    Guid FormId,
    Guid FormVersionId);
