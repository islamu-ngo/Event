using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class FooterPresentationServiceTests
{
    [Test]
    public async Task FailedTransportDoesNotBecomeCachedFooter()
    {
        using var context = new BlazorTestContext();
        var client = Substitute.For<IFooterClient>();
        var fresh = new FooterConfigDto { Settings = new FooterSettingsDto { Template = "minimal" } };
        client.GetFooterConfigAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FooterConfigDto>(new HttpRequestException()), Task.FromResult(fresh));
        using var service = Create(context, client);
        await Assert.That(await service.GetAsync(null)).IsNull();
        await Assert.That(await service.GetAsync(null)).IsSameReferenceAs(fresh);
    }

    [Test]
    public async Task LateTenantResponseCannotReplaceCurrentFooter()
    {
        using var context = new BlazorTestContext();
        var client = Substitute.For<IFooterClient>();
        var first = new TaskCompletionSource<FooterConfigDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<FooterConfigDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GetFooterConfigAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns(first.Task, second.Task);
        using var service = Create(context, client);
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        var loadingA = service.GetAsync(tenantA);
        var loadingB = service.GetAsync(tenantB);
        var current = new FooterConfigDto { Settings = new FooterSettingsDto { Template = "community" } };
        second.SetResult(current);
        await Assert.That(await loadingB).IsSameReferenceAs(current);
        first.SetResult(new FooterConfigDto { Settings = new FooterSettingsDto { Template = "minimal" } });
        await Assert.That(await loadingA).IsNull();
        await Assert.That(await service.GetAsync(tenantB)).IsSameReferenceAs(current);
    }

    [Test]
    public async Task SettingsResetRejectsPendingResponseAndAllowsFreshLoad()
    {
        using var context = new BlazorTestContext();
        var client = Substitute.For<IFooterClient>();
        var pending = new TaskCompletionSource<FooterConfigDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fresh = new FooterConfigDto { Settings = new FooterSettingsDto { Template = "community" } };
        client.GetFooterConfigAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns(pending.Task, Task.FromResult(fresh));
        using var service = Create(context, client);
        var old = service.GetAsync(null);
        context.Services.GetRequiredService<IPublicExperienceService>().SettingsChanged += Raise.Event<Action>();
        await Assert.That(await service.GetAsync(null)).IsSameReferenceAs(fresh);
        pending.SetResult(new FooterConfigDto());
        await Assert.That(await old).IsNull();
        await Assert.That(await service.GetAsync(null)).IsSameReferenceAs(fresh);
    }

    [Test]
    public async Task DisposedServiceRejectsOutstandingResponse()
    {
        using var context = new BlazorTestContext();
        var client = Substitute.For<IFooterClient>();
        var pending = new TaskCompletionSource<FooterConfigDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.GetFooterConfigAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns(pending.Task);
        using var service = Create(context, client);
        var load = service.GetAsync(null);
        service.Dispose();
        pending.SetResult(new FooterConfigDto());
        await Assert.That(await load).IsNull();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.GetAsync(null));
    }

    private static FooterPresentationService Create(BlazorTestContext context, IFooterClient client) =>
        new(client, context.Services.GetRequiredService<NavigationManager>(),
            context.Services.GetRequiredService<IPublicExperienceService>(),
            context.Services.GetRequiredService<AuthenticationStateProvider>(),
            NullLogger<FooterPresentationService>.Instance, TimeProvider.System);
}
