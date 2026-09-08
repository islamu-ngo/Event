using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class RegistrationProviderIntegrationServiceTests
{
    private readonly IRegistrationProviderManagementClient _apiClient = Substitute.For<IRegistrationProviderManagementClient>();
    private readonly RegistrationProviderIntegrationService _service;

    public RegistrationProviderIntegrationServiceTests()
    {
        _service = new RegistrationProviderIntegrationService(_apiClient);
    }

    [Test]
    public async Task GetConnectionsAsync_ReturnsGeneratedHalResourceAndPassesCancellationToken()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        using var cts = new CancellationTokenSource();
        var expected = new HalCollectionResourceOfRegistrationProviderConnectionDto();
        _apiClient.GetRegistrationProviderConnectionsAsync(tenantId, eventId, null, null, cts.Token)
            .Returns(Task.FromResult(expected));

        var actual = await _service.GetConnectionsAsync(tenantId, eventId, cts.Token);

        await Assert.That(actual).IsSameReferenceAs(expected);
        await _apiClient.Received(1).GetRegistrationProviderConnectionsAsync(tenantId, eventId, null, null, cts.Token);
    }

    [Test]
    public async Task GetLaunchDescriptorAsync_UsesSixIdLineageWithoutQueryState()
    {
        var lineage = new RegistrationProviderLaunchLineage(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7());
        var expected = new HalResourceOfRegistrationProviderLaunchDescriptorDto();
        _apiClient.GetRegistrationProviderLaunchDescriptorAsync(
                lineage.TenantId, lineage.EventId, lineage.WorkflowId, lineage.RequirementId,
                lineage.ChannelId, lineage.BindingId, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var actual = await _service.GetLaunchDescriptorAsync(lineage);

        await Assert.That(actual).IsSameReferenceAs(expected);
        await _apiClient.Received(1).GetRegistrationProviderLaunchDescriptorAsync(
            lineage.TenantId, lineage.EventId, lineage.WorkflowId, lineage.RequirementId,
            lineage.ChannelId, lineage.BindingId, null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateConnectionAsync_RequiresPostLinkForSameTenantEventRoute()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var request = new RegistrationProviderConnectionRequestDto { Name = "Forms", ProviderKindId = 1, DeploymentKindId = 2 };
        var link = Link($"/api/tenants/{tenantId:D}/events/{eventId:D}/registration-providers/connections", "POST");
        var expected = new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true };
        _apiClient.CreateRegistrationProviderConnectionAsync(tenantId, eventId, request, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var actual = await _service.CreateConnectionAsync(tenantId, eventId, link, request);

        await Assert.That(actual).IsSameReferenceAs(expected);
        await _apiClient.Received(1).CreateRegistrationProviderConnectionAsync(tenantId, eventId, request, null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateConnectionAsync_RejectsWrongMethodOrHrefBeforeGeneratedClientCall()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var request = new RegistrationProviderConnectionRequestDto();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateConnectionAsync(
            tenantId,
            eventId,
            Link($"/api/tenants/{tenantId:D}/events/{eventId:D}/registration-providers/connections", "GET"),
            request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateConnectionAsync(
            tenantId,
            eventId,
            Link($"/api/tenants/{Guid.CreateVersion7():D}/events/{eventId:D}/registration-providers/connections", "POST"),
            request));
        await _apiClient.DidNotReceiveWithAnyArgs().CreateRegistrationProviderConnectionAsync(default, default, default!);
    }

    [Test]
    public async Task ChannelMutationAsync_RequiresWorkflowRequirementChannelLineage()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var workflowId = Guid.CreateVersion7();
        var requirementId = Guid.CreateVersion7();
        var channelId = Guid.CreateVersion7();
        var request = new RegistrationChannelRequestDto { Ordinal = 1 };
        var link = Link($"/api/tenants/{tenantId:D}/events/{eventId:D}/registration-providers/workflows/{workflowId:D}/requirements/{requirementId:D}/channels/{channelId:D}", "PUT");
        _apiClient.UpdateRegistrationChannelAsync(tenantId, eventId, workflowId, requirementId, channelId, request, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = channelId, Success = true }));

        await _service.UpdateChannelAsync(tenantId, eventId, workflowId, requirementId, channelId, link, request);

        await _apiClient.Received(1).UpdateRegistrationChannelAsync(tenantId, eventId, workflowId, requirementId, channelId, request, null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task QueueMutations_RequireItemActionLinks()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        _apiClient.RetryRegistrationProviderParkedItemAsync(tenantId, eventId, Arg.Any<RetryRegistrationProviderParkedItemRequestDto>(), null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));
        _apiClient.ResolveRegistrationProviderQueueItemAsync(tenantId, eventId, Arg.Any<ResolveRegistrationProviderQueueItemRequestDto>(), null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true }));

        await _service.RetryQueueItemAsync(tenantId, eventId, Link($"/api/tenants/{tenantId:D}/events/{eventId:D}/registration-providers/queue/retry", "POST"), new RetryRegistrationProviderParkedItemRequestDto());
        await _service.ResolveQueueItemAsync(tenantId, eventId, Link($"/api/tenants/{tenantId:D}/events/{eventId:D}/registration-providers/queue/resolve", "POST"), new ResolveRegistrationProviderQueueItemRequestDto());

        await _apiClient.Received(1).RetryRegistrationProviderParkedItemAsync(tenantId, eventId, Arg.Any<RetryRegistrationProviderParkedItemRequestDto>(), null, null, Arg.Any<CancellationToken>());
        await _apiClient.Received(1).ResolveRegistrationProviderQueueItemAsync(tenantId, eventId, Arg.Any<ResolveRegistrationProviderQueueItemRequestDto>(), null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReplaceMappingsAsync_RequiresExactMappingsHalLink()
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var bindingId = Guid.CreateVersion7();
        var request = new ReplaceRegistrationProviderMappingsRequestDto
        {
            FieldMappings = [new RegistrationProviderFieldMappingDto { PlatformFieldKey = "email", ProviderFieldKey = "Email", IsRequired = true }],
            OptionMappings = [new RegistrationProviderOptionMappingDto { PlatformFieldKey = "ticket", PlatformOptionKey = "vip", ProviderOptionKey = "VIP" }]
        };
        var link = Link($"/api/tenants/{tenantId:D}/events/{eventId:D}/registration-providers/bindings/{bindingId:D}/mappings", "PUT");
        _apiClient.ReplaceRegistrationProviderMappingsAsync(tenantId, eventId, bindingId, request, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BaseCommandResponseOfGuid { Id = bindingId, Success = true }));

        await _service.ReplaceMappingsAsync(tenantId, eventId, bindingId, link, request);

        await _apiClient.Received(1).ReplaceRegistrationProviderMappingsAsync(tenantId, eventId, bindingId, request, null, null, Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ReplaceMappingsAsync(tenantId, eventId, bindingId, Link($"/api/tenants/{tenantId:D}/events/{eventId:D}/registration-providers/bindings/{bindingId:D}", "PUT"), request));
    }

    private static HalLink Link(string href, string method) => new() { Href = href, Method = method };
}
