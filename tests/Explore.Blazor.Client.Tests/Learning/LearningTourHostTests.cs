using Bunit;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Learning;
using Explore.Blazor.Client.Contracts.Services.Learning;
using Explore.Blazor.Client.Services.Learning;
using GuideFlow.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.Client.Tests.Learning;

public sealed class LearningTourHostTests : IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private readonly BlazorTestContext _context = new();
    private readonly LearningTourFixture _fixture = new();
    private readonly GuideFlowLearningTourRenderer _renderer = new();
    private readonly LearningOnboardingCoordinator _coordinator;

    public LearningTourHostTests()
    {
        _fixture.Coordinator.Dispose();
        _coordinator = new(_fixture.Api, _fixture.Learning, _fixture.Registry, _renderer, _fixture.Clock);
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddGuideFlow();
        _context.Services.AddSingleton(_fixture.Learning);
        _context.Services.AddSingleton(_fixture.Registry);
        _context.Services.AddSingleton<ILearningTargetRegistry>(_fixture.Registry);
        _context.Services.AddSingleton(_coordinator);
        _context.Services.AddSingleton<ILearningOnboardingCoordinator>(_coordinator);
        _context.Services.AddSingleton(_renderer);
        _context.Services.AddSingleton<ILearningTourHost>(_renderer);
        _context.Services.AddSingleton<ILearningTourRenderer>(_renderer);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DueContextRechecksThePlanAndNeverClaimsAutomatically(bool stillDue)
    {
        _fixture.Source.Tours =
        [
            new() { Family = LearningTourFamily.Shell, MaterialVersion = 2, CanInvite = true }
        ];
        var plan = _fixture.Plan();
        plan.CanInvite = stillDue;
        _fixture.PlanReply = LearningTourFixture.Signal<HalResourceOfLearningTourPlanDto>();
        _fixture.PlanReply.SetResult(plan);
        var host = _context.Render<LearningTourHost>();
        await _fixture.PlanRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(_fixture.Claims.Count).IsEqualTo(0);
        await Assert.That(_coordinator.CanStart).IsEqualTo(stillDue);
        await Assert.That(host.FindAll("section.learning-tour > button").Count)
            .IsEqualTo(stillDue ? 6 : 1);
    }

    [Test]
    [Arguments("Find available areas.")]
    [Arguments("ابحث عن المساحات المتاحة.")]
    public async Task InstructionDeterminesItsOwnTextDirection(string instruction)
    {
        var host = _context.Render<LearningTourHost>();
        var step = new LearningTourStepDto
        {
            StepKey = "shell.navigation", StepVersion = 1, TargetKey = "shell.navigation",
            Instruction = new() { Key = "navigation", Fallback = instruction }
        };
        await host.InvokeAsync(() => _coordinator.OfferAsync());

        await host.InvokeAsync(() => _renderer.ShowAsync(step,
            _fixture.NavigationTarget, CancellationToken.None)).WaitAsync(Deadline);

        var copy = host.Find("p[tabindex='-1']");
        await Assert.That(copy.TextContent).IsEqualTo(instruction);
        await Assert.That(copy.GetAttribute("dir")).IsEqualTo("auto");
        await Assert.That(host.Find("p.learning-tour__progress").GetAttribute("dir")).IsEqualTo("auto");
    }

    [Test]
    public async Task MissingAffordanceDoesNotMountOrImportTheRenderer()
    {
        _fixture.Source._links!.Remove("tour-plan");
        _context.JSInterop.Mode = JSRuntimeMode.Strict;
        var host = _context.Render<LearningTourHost>();
        await Assert.That(host.FindAll("section.learning-tour > button").Count).IsEqualTo(0);
        await Assert.That(_context.JSInterop.Invocations.Count).IsEqualTo(0);
    }

    [Test]
    public async Task MountingDoesNotReadAPlanOrAcquireAClaim()
    {
        var host = _context.Render<LearningTourHost>();
        await Assert.That(host.FindAll("section.learning-tour > button").Count).IsEqualTo(1);
        await Assert.That(_fixture.PlanRead.Task.IsCompleted).IsFalse();
        await Assert.That(_fixture.Claims.Count).IsEqualTo(0);
        await Assert.That(_coordinator.IsActive).IsFalse();
    }

    [Test]
    public async Task ExplicitOfferShowsStartWithoutClaimingOrAutomaticallyDriving()
    {
        var host = _context.Render<LearningTourHost>();
        await host.Find("section.learning-tour > button").ClickAsync(new());
        await Assert.That(_fixture.PlanRead.Task.IsCompleted).IsTrue();
        await Assert.That(_fixture.Claims.Count).IsEqualTo(0);
        await Assert.That(_coordinator.CanStart).IsTrue();
        await Assert.That(_coordinator.IsActive).IsFalse();
        await Assert.That(host.FindAll("section.learning-tour > button").Count).IsEqualTo(6);
    }

    [Test]
    public async Task ExplicitStartWaitsForClaimAndRejectedClaimDoesNotPresentATour()
    {
        var host = _context.Render<LearningTourHost>();
        await host.Find("section.learning-tour > button").ClickAsync(new());
        _fixture.ClaimReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var start = host.FindAll("section.learning-tour > button")[1].ClickAsync(new());
        await _fixture.ClaimObserved.Task.WaitAsync(Deadline);
        await Assert.That(_fixture.Claims.Count).IsEqualTo(1);
        await Assert.That(_coordinator.IsActive).IsFalse();
        // HTTP success with an unchanged learner revision is not an accepted claim.
        _fixture.ClaimReply.SetResult(_fixture.Receipt(0));
        await start.WaitAsync(Deadline);
        await Assert.That(_coordinator.IsActive).IsFalse();
        await Assert.That(_coordinator.Failure).IsNotNull();
        await Assert.That(host.FindAll("section.learning-tour [role='status']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task DisposedHostDetachesRendererAndInvalidatesOfferedPlan()
    {
        var host = _context.Render<LearningTourHost>();
        await host.Find("section.learning-tour > button").ClickAsync(new());
        await host.InvokeAsync(() => host.Instance.DisposeAsync().AsTask());
        await Assert.That(_coordinator.Plan).IsNull();
        var rejected = false;
        try
        {
            var steps = _fixture.Plan().Steps
                ?? throw new InvalidOperationException("The fixture must supply tour steps.");
            await _renderer.ShowAsync(steps.First(),
                _fixture.NavigationTarget, CancellationToken.None);
        }
        catch (InvalidOperationException) { rejected = true; }
        await Assert.That(rejected).IsTrue();
    }

    public void Dispose()
    {
        _context.Dispose();
        _coordinator.Dispose();
        _fixture.Dispose();
    }
}
