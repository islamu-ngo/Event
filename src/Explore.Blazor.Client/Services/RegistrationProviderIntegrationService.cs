using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed class RegistrationProviderIntegrationService(IRegistrationProviderManagementClient apiClient) : IRegistrationProviderIntegrationService
{
    private const string ProviderBase = "/api/tenants/{0}/events/{1}/registration-providers";

    public Task<HalCollectionResourceOfRegistrationProviderConnectionDto> GetConnectionsAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationProviderConnectionsAsync(tenantId, eventId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateConnectionAsync(Guid tenantId, Guid eventId, HalLink link, RegistrationProviderConnectionRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Post, Path(tenantId, eventId, "connections"));
        return apiClient.CreateRegistrationProviderConnectionAsync(tenantId, eventId, request, cancellationToken: cancellationToken);
    }

    public Task<HalResourceOfRegistrationProviderConnectionDto> GetConnectionAsync(Guid tenantId, Guid eventId, Guid connectionId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationProviderConnectionAsync(tenantId, eventId, connectionId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateConnectionAsync(Guid tenantId, Guid eventId, Guid connectionId, HalLink link, RegistrationProviderConnectionRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Put, Path(tenantId, eventId, $"connections/{connectionId:D}"));
        return apiClient.UpdateRegistrationProviderConnectionAsync(tenantId, eventId, connectionId, request, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> DeleteConnectionAsync(Guid tenantId, Guid eventId, Guid connectionId, HalLink link, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Delete, Path(tenantId, eventId, $"connections/{connectionId:D}"));
        return apiClient.DeleteRegistrationProviderConnectionAsync(tenantId, eventId, connectionId, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> ReplaceApprovedOriginsAsync(Guid tenantId, Guid eventId, Guid connectionId, HalLink link, ReplaceRegistrationProviderApprovedOriginsRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Put, Path(tenantId, eventId, $"connections/{connectionId:D}/approved-origins"));
        return apiClient.ReplaceRegistrationProviderApprovedOriginsAsync(tenantId, eventId, connectionId, request, cancellationToken: cancellationToken);
    }

    public Task<HalCollectionResourceOfRegistrationProviderBindingDto> GetBindingsAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationProviderBindingsAsync(tenantId, eventId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateBindingAsync(Guid tenantId, Guid eventId, HalLink link, RegistrationProviderBindingRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Post, Path(tenantId, eventId, "bindings"));
        return apiClient.CreateRegistrationProviderBindingAsync(tenantId, eventId, request, cancellationToken: cancellationToken);
    }

    public Task<HalResourceOfRegistrationProviderBindingDto> GetBindingAsync(Guid tenantId, Guid eventId, Guid bindingId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationProviderBindingAsync(tenantId, eventId, bindingId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateBindingAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, RegistrationProviderBindingRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Put, Path(tenantId, eventId, $"bindings/{bindingId:D}"));
        return apiClient.UpdateRegistrationProviderBindingAsync(tenantId, eventId, bindingId, request, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> ReplaceMappingsAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, ReplaceRegistrationProviderMappingsRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Put, Path(tenantId, eventId, $"bindings/{bindingId:D}/mappings"));
        return apiClient.ReplaceRegistrationProviderMappingsAsync(tenantId, eventId, bindingId, request, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> DeleteBindingAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Delete, Path(tenantId, eventId, $"bindings/{bindingId:D}"));
        return apiClient.DeleteRegistrationProviderBindingAsync(tenantId, eventId, bindingId, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> PublishBindingAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Post, Path(tenantId, eventId, $"bindings/{bindingId:D}/publish"));
        return apiClient.PublishRegistrationProviderBindingAsync(tenantId, eventId, bindingId, cancellationToken: cancellationToken);
    }

    public Task<HalCollectionResourceOfRegistrationChannelDto> GetChannelsAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationChannelsAsync(tenantId, eventId, workflowId, requirementId, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateChannelAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, HalLink link, RegistrationChannelRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Post, ChannelPath(tenantId, eventId, workflowId, requirementId, "channels"));
        return apiClient.CreateRegistrationChannelAsync(tenantId, eventId, workflowId, requirementId, request, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> UpdateChannelAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, HalLink link, RegistrationChannelRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Put, ChannelPath(tenantId, eventId, workflowId, requirementId, $"channels/{channelId:D}"));
        return apiClient.UpdateRegistrationChannelAsync(tenantId, eventId, workflowId, requirementId, channelId, request, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> DeleteChannelAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, HalLink link, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Delete, ChannelPath(tenantId, eventId, workflowId, requirementId, $"channels/{channelId:D}"));
        return apiClient.DeleteRegistrationChannelAsync(tenantId, eventId, workflowId, requirementId, channelId, cancellationToken: cancellationToken);
    }

    public Task<HalResourceOfRegistrationProviderLaunchDescriptorDto> GetLaunchDescriptorAsync(RegistrationProviderLaunchLineage lineage, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationProviderLaunchDescriptorAsync(lineage.TenantId, lineage.EventId, lineage.WorkflowId, lineage.RequirementId, lineage.ChannelId, lineage.BindingId, cancellationToken: cancellationToken);

    public Task<HalCollectionResourceOfRegistrationProviderBindingHealthDto> GetHealthAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationProviderHealthAsync(tenantId, eventId, cancellationToken: cancellationToken);

    public Task<HalCollectionResourceOfRegistrationProviderParkedQueueItemDto> GetQueueAsync(Guid tenantId, Guid eventId, int? limit = null, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationProviderQueueAsync(tenantId, eventId, limit, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> PollReconciliationAsync(Guid tenantId, Guid eventId, Guid bindingId, HalLink link, DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Post, Path(tenantId, eventId, $"{bindingId:D}/reconcile"));
        return apiClient.PollRegistrationProviderReconciliationAsync(tenantId, eventId, bindingId, sinceUtc, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> QueueManualImportAsync(Guid tenantId, Guid eventId, HalLink link, ManualRegistrationProviderImportRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Post, Path(tenantId, eventId, "manual-imports"));
        return apiClient.QueueManualRegistrationProviderImportAsync(tenantId, eventId, request, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> RetryQueueItemAsync(Guid tenantId, Guid eventId, HalLink link, RetryRegistrationProviderParkedItemRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Post, Path(tenantId, eventId, "queue/retry"));
        return apiClient.RetryRegistrationProviderParkedItemAsync(tenantId, eventId, request, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> ResolveQueueItemAsync(Guid tenantId, Guid eventId, HalLink link, ResolveRegistrationProviderQueueItemRequestDto request, CancellationToken cancellationToken = default)
    {
        RequireLink(link, HttpMethod.Post, Path(tenantId, eventId, "queue/resolve"));
        return apiClient.ResolveRegistrationProviderQueueItemAsync(tenantId, eventId, request, cancellationToken: cancellationToken);
    }

    private static string Path(Guid tenantId, Guid eventId, string suffix) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, ProviderBase, tenantId.ToString("D"), eventId.ToString("D")) + "/" + suffix;

    private static string ChannelPath(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, string suffix) =>
        Path(tenantId, eventId, $"workflows/{workflowId:D}/requirements/{requirementId:D}/{suffix}");

    private static void RequireLink(HalLink link, HttpMethod method, string expectedPath)
    {
        if (!string.Equals(link.Method, method.Method, StringComparison.OrdinalIgnoreCase) || !string.Equals(LinkPath(link.Href), expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("HAL link does not match the requested registration-provider mutation.");
        }
    }

    private static string LinkPath(string href) =>
        Uri.TryCreate(href, UriKind.Absolute, out var absolute)
            ? absolute.AbsolutePath
            : new Uri(new Uri("https://event.local"), href).AbsolutePath;
}
