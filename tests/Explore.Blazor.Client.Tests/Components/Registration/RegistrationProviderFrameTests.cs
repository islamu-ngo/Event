using Explore.Blazor.Client.Components.Registration.FormRenderer;
using Explore.Blazor.Client.Components.Registration.ProviderLaunch;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Tests.Components.Registration;

public sealed class RegistrationProviderFrameTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IBffClient _bffClient = Substitute.For<IBffClient>();
    private readonly INativeRegistrationFormService _nativeRegistrationFormService = Substitute.For<INativeRegistrationFormService>();
    private readonly IRegistrationOrderService _registrationOrderService = Substitute.For<IRegistrationOrderService>();
    private readonly IBrowserActionInterop _browserActions = Substitute.For<IBrowserActionInterop>();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Frame_RendersOnlySameOriginBffUrlAndPollsOnLoad()
    {
        var state = CreateInitializedState(false);

        var cut = _ctx.RenderMudComponent<RegistrationProviderFrame>(parameters => parameters.Add(component => component.State, state));
        cut.Find("iframe").TriggerEvent("onload", EventArgs.Empty);

        await Assert.That(cut.Find("iframe").GetAttribute("src")).StartsWith("/bff/registration-provider-embed/");
        await Assert.That(cut.Find("iframe").GetAttribute("title")).IsEqualTo("External registration provider form");
        await _nativeRegistrationFormService.Received(2).GetRequirementsAsync(state.Context!.EventId, state.Context.OrderId, null, Arg.Any<CancellationToken>());
        await Assert.That(state.IsComplete).IsFalse();
    }

    [Test]
    public async Task Frame_InvalidOrMissingUrl_RendersFallbackOnly()
    {
        var cut = _ctx.RenderMudComponent<RegistrationProviderFrame>();

        await Assert.That(cut.FindAll("iframe")).IsEmpty();
        await Assert.That(cut.Find("[data-testid='registration-provider-frame-unavailable']").TextContent).Contains("not available");
    }

    private RegistrationProviderLaunchState CreateInitializedState(bool complete)
    {
        var context = new RegistrationProviderLaunchContext(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new RegistrationProviderLaunchLineage(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()));
        _bffClient.SendAsync<RegistrationProviderBffLaunch, RegistrationProviderBffTicket>(
                HttpMethod.Post, "/bff/registration-provider-embed/launches",
                Arg.Any<RegistrationProviderBffLaunch>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RegistrationProviderBffTicket?>(
                new("/bff/registration-provider-embed/launches/opaque-launch-id")));
        _nativeRegistrationFormService.GetRequirementsAsync(context.EventId, context.OrderId, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NativeRegistrationRequirementCollectionView?>(Requirements(context.Lineage.RequirementId, complete)));
        _registrationOrderService.GetCurrentAsync(context.EventId, context.OrderId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(new HalResourceOfRegistrationOrderDto()));
        var state = new RegistrationProviderLaunchState(_bffClient, _nativeRegistrationFormService, _registrationOrderService, _browserActions);
        state.InitializeAsync(context).GetAwaiter().GetResult();
        return state;
    }

    private static NativeRegistrationRequirementCollectionView Requirements(Guid requirementId, bool complete) => new(
        [new NativeRegistrationLaunchDescriptorView(requirementId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), false, [], new RegistrationRequirementProgressView(1, complete ? 1 : 0, 0, complete ? 0 : 1, complete))],
        new Dictionary<string, HalLink>());
}
