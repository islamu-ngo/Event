using Explore.Blazor.Client.Components.Registration.FormRenderer;
using Explore.Blazor.Client.Components.Registration.ProviderLaunch;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Tests.Components.Registration;

public sealed class RegistrationProviderLaunchModelTests
{
    private readonly IBffClient _bffClient = Substitute.For<IBffClient>();
    private readonly INativeRegistrationFormService _nativeRegistrationFormService = Substitute.For<INativeRegistrationFormService>();
    private readonly IRegistrationOrderService _registrationOrderService = Substitute.For<IRegistrationOrderService>();
    private readonly IBrowserActionInterop _browserActions = Substitute.For<IBrowserActionInterop>();
    private readonly RegistrationProviderLaunchState _model;

    public RegistrationProviderLaunchModelTests()
    {
        _model = new RegistrationProviderLaunchState(_bffClient, _nativeRegistrationFormService, _registrationOrderService, _browserActions);
    }

    [Test]
    public async Task InitializeAsync_UsesOpaqueSameOriginEmbedUrlReturnedByBff()
    {
        var context = CreateContext();
        GivenBffLaunch("/bff/registration-provider-embed/launches/opaque-launch-id");
        _nativeRegistrationFormService.GetRequirementsAsync(context.EventId, context.OrderId, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NativeRegistrationRequirementCollectionView?>(IncompleteRequirements(context.Lineage.RequirementId)));
        _registrationOrderService.GetCurrentAsync(context.EventId, context.OrderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(new HalResourceOfRegistrationOrderDto()));

        await _model.InitializeAsync(context);

        await Assert.That(_model.EmbedUrl).IsEqualTo("/bff/registration-provider-embed/launches/opaque-launch-id");
        await Assert.That(_model.EmbedUrl).DoesNotContain("?");
        await Assert.That(_model.IsComplete).IsFalse();
    }

    [Test]
    public async Task IframeCallbacks_DoNotMarkRequirementComplete()
    {
        var context = CreateContext();
        GivenBffLaunch("/bff/registration-provider-embed/launches/opaque-launch-id");
        _nativeRegistrationFormService.GetRequirementsAsync(context.EventId, context.OrderId, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NativeRegistrationRequirementCollectionView?>(IncompleteRequirements(context.Lineage.RequirementId)));
        _registrationOrderService.GetCurrentAsync(context.EventId, context.OrderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(new HalResourceOfRegistrationOrderDto()));

        await _model.InitializeAsync(context);
        await _model.OnIframeLoadedAsync();
        await _model.OnIframeNavigatedAsync();

        await Assert.That(_model.IframeLoaded).IsTrue();
        await Assert.That(_model.IsComplete).IsFalse();
    }

    [Test]
    public async Task PollAsync_MarksCompleteOnlyFromAuthoritativeRequirementStatus()
    {
        var context = CreateContext();
        GivenBffLaunch("/bff/registration-provider-embed/launches/opaque-launch-id");
        _nativeRegistrationFormService.GetRequirementsAsync(context.EventId, context.OrderId, null, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<NativeRegistrationRequirementCollectionView?>(IncompleteRequirements(context.Lineage.RequirementId)),
                Task.FromResult<NativeRegistrationRequirementCollectionView?>(CompleteRequirements(context.Lineage.RequirementId)));
        _registrationOrderService.GetCurrentAsync(context.EventId, context.OrderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(new HalResourceOfRegistrationOrderDto()));

        await _model.InitializeAsync(context);
        await _model.PollAsync();

        await Assert.That(_model.IsComplete).IsTrue();
        await _nativeRegistrationFormService.Received(2).GetRequirementsAsync(context.EventId, context.OrderId, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OpenAuthorizedNewTabAsync_UsesSameOriginBffUrl()
    {
        var context = CreateContext();
        GivenBffLaunch("/bff/registration-provider-embed/launches/opaque-launch-id");
        _nativeRegistrationFormService.GetRequirementsAsync(context.EventId, context.OrderId, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NativeRegistrationRequirementCollectionView?>(IncompleteRequirements(context.Lineage.RequirementId)));
        _registrationOrderService.GetCurrentAsync(context.EventId, context.OrderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderDto?>(new HalResourceOfRegistrationOrderDto()));
        _browserActions.OpenSameOriginNewTabAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        await _model.InitializeAsync(context);
        var opened = await _model.OpenAuthorizedNewTabAsync();

        await Assert.That(opened).IsTrue();
        await _browserActions.Received(1).OpenSameOriginNewTabAsync(_model.EmbedUrl!, Arg.Any<CancellationToken>());
    }

    private static RegistrationProviderLaunchContext CreateContext() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        new RegistrationProviderLaunchLineage(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7()));

    private void GivenBffLaunch(string embedUrl) =>
        _bffClient.SendAsync<RegistrationProviderBffLaunch, RegistrationProviderBffTicket>(
                HttpMethod.Post, "/bff/registration-provider-embed/launches",
                Arg.Any<RegistrationProviderBffLaunch>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RegistrationProviderBffTicket?>(new(embedUrl)));

    private static NativeRegistrationRequirementCollectionView IncompleteRequirements(Guid requirementId) => Requirements(requirementId, false);

    private static NativeRegistrationRequirementCollectionView CompleteRequirements(Guid requirementId) => Requirements(requirementId, true);

    private static NativeRegistrationRequirementCollectionView Requirements(Guid requirementId, bool complete) => new(
        [new NativeRegistrationLaunchDescriptorView(
            requirementId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            false,
            [],
            new RegistrationRequirementProgressView(1, complete ? 1 : 0, 0, complete ? 0 : 1, complete))],
        new Dictionary<string, HalLink>());
}
