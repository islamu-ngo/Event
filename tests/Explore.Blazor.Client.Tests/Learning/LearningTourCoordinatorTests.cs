using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Learning;
using Explore.Blazor.Client.Services.Learning;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Tests.Learning;

public sealed class LearningTourCoordinatorTests
{
    [Test]
    public async Task CoordinatorMutationsUseDistinctUuid7IntentIdentities()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        await fixture.Coordinator.NextAsync();
        await fixture.Coordinator.CloseAsync();

        var intents = fixture.Claims.Select(request => request.IntentId)
            .Concat(fixture.Outcomes.Select(request => request.IntentId)).ToArray();
        await Assert.That(intents.Length).IsEqualTo(3);
        await Assert.That(intents.All(id => id.Version == 7 && id.Variant is >= 8 and <= 11)).IsTrue();
        await Assert.That(intents.Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    [Arguments(LearningTourOutcome.Completed, false)]
    [Arguments(LearningTourOutcome.Completed, true)]
    [Arguments(LearningTourOutcome.Skipped, false)]
    [Arguments(LearningTourOutcome.Skipped, true)]
    public async Task ReplayDismissalAcceptsTheClaimedHistoricalOutcome(
        LearningTourOutcome retained, bool interrupt)
    {
        using var fixture = new LearningTourFixture();
        fixture.ClaimReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        fixture.ClaimReply.SetResult(fixture.Receipt(1, outcome: retained));
        await fixture.StartAsync();
        fixture.OutcomeReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        fixture.OutcomeReply.SetResult(fixture.Receipt(2, outcome: retained));
        if (interrupt)
            await fixture.Coordinator.CloseAsync();
        else
            await fixture.Coordinator.SkipAsync();
        await Assert.That(fixture.Coordinator.Failure).IsNull();
        await Assert.That(fixture.Coordinator.IsActive).IsFalse();
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(LearningTourOutcome.InProgress)]
    [Arguments(LearningTourOutcome.Completed)]
    [Arguments(LearningTourOutcome.Skipped)]
    public async Task ReplayCanAdvancePastAlreadyRecordedCoverageWithoutRenewingTheClaim(
        LearningTourOutcome retained)
    {
        using var fixture = new LearningTourFixture();
        fixture.ClaimReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        fixture.ClaimReply.SetResult(fixture.Receipt(1, "shell.navigation", retained));
        fixture.Api.RecordLearningTourOutcomeAsync(Arg.Any<LearningRecordTourOutcomeRequest>(),
            cancellationToken: Arg.Any<CancellationToken>()).Returns(
                Task.FromException<HalResourceOfLearningMutationReceiptDto>(
                    new InvalidOperationException("Only new explicit semantic progress renews a claim.")));
        await fixture.StartAsync();

        await fixture.Coordinator.NextAsync();

        await Assert.That(fixture.Renderer.Shown.Last()).IsEqualTo("shell.help");
        await Assert.That(fixture.Renderer.IsVisible).IsTrue();
        await Assert.That(fixture.Coordinator.IsActive).IsTrue();
        await Assert.That(fixture.Coordinator.Failure).IsNull();
    }

    [Test]
    public async Task InvitationSkipDoesNotAcquireAClaimOrOpenAnOverlay()
    {
        using var fixture = new LearningTourFixture();
        await fixture.Coordinator.OfferAsync();
        await fixture.Coordinator.SkipAsync();
        await Assert.That(fixture.Claims.Count).IsEqualTo(0);
        await Assert.That(fixture.Outcomes.Single().Action).IsEqualTo(LearningTourAction.Skip);
        await Assert.That(fixture.Outcomes.Single().ClaimToken).IsNull();
        await Assert.That(fixture.Coordinator.Plan).IsNull();
        await Assert.That(fixture.Renderer.Shown.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(LearningSnoozeDuration.OneHour)]
    [Arguments(LearningSnoozeDuration.OneDay)]
    [Arguments(LearningSnoozeDuration.OneWeek)]
    public async Task InvitationSnoozeUsesTheClosedDurationWithoutClaiming(LearningSnoozeDuration duration)
    {
        using var fixture = new LearningTourFixture();
        await fixture.Coordinator.OfferAsync();
        await fixture.Coordinator.SnoozeAsync(duration);
        var request = fixture.Outcomes.Single();
        await Assert.That(request.Action).IsEqualTo(LearningTourAction.Snooze);
        await Assert.That(request.SnoozeDuration).IsEqualTo(duration);
        await Assert.That(request.ClaimToken).IsNull();
        await Assert.That(fixture.Claims.Count).IsEqualTo(0);
        await Assert.That(fixture.Coordinator.Plan).IsNull();
        await Assert.That(fixture.Renderer.Shown.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExplicitSkipRecordsOnlyTheCurrentClaimAndClosesGuidance()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        await fixture.Coordinator.SkipAsync();
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(1);
        await Assert.That(fixture.Outcomes[0].Action).IsEqualTo(LearningTourAction.Skip);
        await Assert.That(fixture.Coordinator.IsActive).IsFalse();
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
    }

    [Test]
    public async Task MissingPlanAffordanceCannotReadOrOfferGuidance()
    {
        using var fixture = new LearningTourFixture();
        fixture.Source._links!.Remove("tour-plan");
        await fixture.Coordinator.OfferAsync();
        await Assert.That(fixture.PlanRead.Task.IsCompleted).IsFalse();
        await Assert.That(fixture.Coordinator.CanStart).IsFalse();
        await Assert.That(fixture.Claims.Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnexpectedTransitionFailureIsObservedByTheCaller()
    {
        using var fixture = new LearningTourFixture();
        fixture.PlanReply = LearningTourFixture.Signal<HalResourceOfLearningTourPlanDto>();
        fixture.PlanReply.SetException(new ArgumentException("Invalid transition input."));
        var observed = false;
        try
        {
            await fixture.Coordinator.OfferAsync();
        }
        catch (ArgumentException)
        {
            observed = true;
        }
        await Assert.That(observed).IsTrue();
        await Assert.That(fixture.Coordinator.Plan).IsNull();
        await Assert.That(fixture.Claims.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FirstOfferFailureBelongsToTheObservedScopeAndRemainsVisible()
    {
        using var fixture = new LearningTourFixture();
        fixture.PlanReply = LearningTourFixture.Signal<HalResourceOfLearningTourPlanDto>();
        fixture.PlanReply.SetException(new HttpRequestException("Request failed."));
        await fixture.Coordinator.OfferAsync();
        await Assert.That(fixture.Coordinator.Failure).IsNotNull();
        await Assert.That(fixture.Coordinator.Plan).IsNull();
        await Assert.That(fixture.Claims.Count).IsEqualTo(0);
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
    }

    [Test]
    public async Task MissingClaimAffordanceCannotBeReplacedByLocalAvailability()
    {
        using var fixture = new LearningTourFixture();
        var plan = fixture.Plan();
        plan._links = new Dictionary<string, HalLink>();
        fixture.PlanReply = LearningTourFixture.Signal<HalResourceOfLearningTourPlanDto>();
        fixture.PlanReply.SetResult(plan);
        await fixture.Coordinator.OfferAsync();
        await fixture.Coordinator.StartAsync();
        await Assert.That(fixture.Coordinator.CanStart).IsFalse();
        await Assert.That(fixture.Claims.Count).IsEqualTo(0);
        await Assert.That(fixture.Renderer.Shown.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FailedSaveClosesWithoutAdvancingOrReplayingTheMutation()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        fixture.OutcomeReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var next = fixture.Coordinator.NextAsync();
        fixture.OutcomeReply.SetException(new HttpRequestException("Request failed."));
        await next;
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(1);
        await Assert.That(string.Join(",", fixture.Renderer.Shown)).IsEqualTo("shell.navigation");
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
        await Assert.That(fixture.Coordinator.Failure).IsNotNull();
    }

    [Test]
    public async Task OfferDoesNotClaimAndExplicitStartIsSingleFlight()
    {
        using var fixture = new LearningTourFixture();
        await fixture.Coordinator.OfferAsync();
        await Assert.That(fixture.Claims.Count).IsEqualTo(0);
        await Assert.That(fixture.Renderer.Shown.Count).IsEqualTo(0);
        await Assert.That(fixture.Coordinator.CanStart).IsTrue();

        fixture.ClaimReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var start = fixture.Coordinator.StartAsync();
        var duplicate = fixture.Coordinator.StartAsync();
        await Assert.That(ReferenceEquals(start, duplicate)).IsTrue();
        await Assert.That(fixture.Claims.Count).IsEqualTo(1);
        await Assert.That(fixture.Renderer.Shown.Count).IsEqualTo(0);
        fixture.ClaimReply.SetResult(fixture.Receipt(1));
        await start;
        await Assert.That(string.Join(",", fixture.Renderer.Shown)).IsEqualTo("shell.navigation");
    }

    [Test]
    public async Task NextWaitsForAcceptedSemanticProgressBeforeShowingAnotherTarget()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        fixture.OutcomeReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var next = fixture.Coordinator.NextAsync();
        var duplicate = fixture.Coordinator.NextAsync();
        await Assert.That(ReferenceEquals(next, duplicate)).IsTrue();
        await Assert.That(string.Join(",", fixture.Renderer.Shown)).IsEqualTo("shell.navigation");
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(1);
        await Assert.That(fixture.Outcomes[0].StepKey).IsEqualTo("shell.navigation");
        await Assert.That(fixture.Outcomes[0].Action).IsEqualTo(LearningTourAction.Progress);

        fixture.OutcomeReply.SetResult(fixture.Receipt(2, "shell.navigation"));
        await next;
        await Assert.That(string.Join(",", fixture.Renderer.Shown)).IsEqualTo("shell.navigation,shell.help");
    }

    [Test]
    public async Task RejectedProgressDoesNotAdvanceOrReportCompletion()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        // A successful HTTP response without the semantic step witness is not progress.
        fixture.OutcomeReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var next = fixture.Coordinator.NextAsync();
        fixture.OutcomeReply.SetResult(fixture.Receipt(2));
        await next;
        await Assert.That(string.Join(",", fixture.Renderer.Shown)).IsEqualTo("shell.navigation");
        await Assert.That(fixture.Coordinator.IsActive).IsFalse();
        await Assert.That(fixture.Coordinator.Failure).IsNotNull();
        await Assert.That(fixture.Outcomes.Any(item => item.Action == LearningTourAction.Complete)).IsFalse();
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
    }

    [Test]
    public async Task CompletionRequiresBothLastStepAndAcceptedCompletionReceipt()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        await fixture.Coordinator.NextAsync();
        await fixture.Coordinator.NextAsync();
        await Assert.That(string.Join(",", fixture.Outcomes.Select(item => item.Action)))
            .IsEqualTo("Progress,Progress,Complete");
        await Assert.That(fixture.Coordinator.IsActive).IsFalse();
        await Assert.That(fixture.Coordinator.Failure).IsNull();
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LateClaimCannotOpenAfterAccountOrPolicySnapshotChanges(bool replaceOwner)
    {
        using var fixture = new LearningTourFixture();
        await fixture.Coordinator.OfferAsync();
        fixture.ClaimReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var start = fixture.Coordinator.StartAsync();
        fixture.ReplaceSnapshot(replaceOwner);
        fixture.ClaimReply.SetResult(fixture.Receipt(1));
        await start;
        await Assert.That(fixture.Renderer.Shown.Count).IsEqualTo(0);
        await Assert.That(fixture.Coordinator.IsActive).IsFalse();
        await Assert.That(fixture.Coordinator.Plan).IsNull();
    }

    [Test]
    public async Task LateProgressCannotOpenInAReplacementPresentationGeneration()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        fixture.OutcomeReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var next = fixture.Coordinator.NextAsync();
        fixture.Registry.Publish(fixture.Owner,
            new(LearningTourFamily.Shell, "replacement"), false, fixture.NavigationTarget);
        fixture.OutcomeReply.SetResult(fixture.Receipt(2, "shell.navigation"));
        await next;
        await Assert.That(string.Join(",", fixture.Renderer.Shown)).IsEqualTo("shell.navigation");
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RemovingActiveTargetClosesWithoutWritingIntoAnotherScope()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        fixture.Registry.Remove(fixture.Owner);
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
        await Assert.That(fixture.Coordinator.IsActive).IsFalse();
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task BlockingAnUrgentFlowClosesOptionalGuidance()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        fixture.Registry.Publish(fixture.Owner, fixture.Presentation, true, fixture.NavigationTarget);
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
        await Assert.That(fixture.Coordinator.CanStart).IsFalse();
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task EscapeDuringUncertainSaveClosesLocallyAndIgnoresLateSuccess()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        fixture.OutcomeReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var next = fixture.Coordinator.NextAsync();
        await fixture.Coordinator.CloseAsync();
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
        fixture.OutcomeReply.SetResult(fixture.Receipt(2, "shell.navigation"));
        await next;
        await Assert.That(string.Join(",", fixture.Renderer.Shown)).IsEqualTo("shell.navigation");
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DisposalCancelsLateClaimWithoutReopeningOrWritingAnInterruption()
    {
        using var fixture = new LearningTourFixture();
        await fixture.Coordinator.OfferAsync();
        fixture.ClaimReply = LearningTourFixture.Signal<HalResourceOfLearningMutationReceiptDto>();
        var start = fixture.Coordinator.StartAsync();
        fixture.Coordinator.Dispose();
        fixture.ClaimReply.SetResult(fixture.Receipt(1));
        await start;
        await Assert.That(fixture.Renderer.Shown.Count).IsEqualTo(0);
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExpiredClaimCannotAdmitAnotherProgressWrite()
    {
        using var fixture = new LearningTourFixture();
        await fixture.StartAsync();
        fixture.Clock.Now = fixture.Clock.Now.AddHours(1);
        await fixture.Coordinator.NextAsync();
        await Assert.That(fixture.Outcomes.Count).IsEqualTo(0);
        await Assert.That(fixture.Renderer.IsVisible).IsFalse();
    }

    [Test]
    public async Task TargetReadinessUsesRegistrationSignalAndCancellation()
    {
        var registry = new LearningTargetRegistry();
        var owner = new object();
        var context = new LearningPresentationContext(LearningTourFamily.Studio, "loaded");
        registry.Publish(owner, context, false);
        using var lifetime = new CancellationTokenSource();
        var readiness = registry.WaitAsync(context, "studio.event-list", lifetime.Token);
        await Assert.That(readiness.IsCompleted).IsFalse();
        var target = new LearningTarget("studio.event-list", "studio-events", new ElementReference("rendered"));
        registry.Publish(owner, context, false, target);
        await Assert.That(await readiness).IsEqualTo(target);

        var missing = registry.WaitAsync(context, "studio.event-management", lifetime.Token);
        lifetime.Cancel();
        var canceled = false;
        try { await missing; }
        catch (OperationCanceledException) { canceled = true; }
        await Assert.That(canceled).IsTrue();
    }

    [Test]
    public async Task AmbiguousTargetsNeverResolveAsARealTarget()
    {
        using var fixture = new LearningTourFixture();
        fixture.Registry.Publish(new object(), fixture.Presentation, false, fixture.NavigationTarget);
        await Assert.That(fixture.Registry.Find(fixture.Presentation, "shell.navigation")).IsNull();
    }
}

internal sealed class LearningTourFixture : IDisposable
{
    internal static TaskCompletionSource<T> Signal<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal readonly ILearningClient Api = Substitute.For<ILearningClient>();
    internal readonly ILearningClientService Learning = Substitute.For<ILearningClientService>();
    internal readonly LearningTargetRegistry Registry = new();
    internal readonly RecordingLearningRenderer Renderer = new();
    internal readonly LearningTestClock Clock = new();
    internal readonly object Owner = new();
    internal readonly LearningPresentationContext Presentation = new(LearningTourFamily.Shell, "shell-1");
    internal readonly LearningTarget NavigationTarget = new(
        "shell.navigation", "learning-shell-navigation", new ElementReference("navigation"));
    internal readonly List<LearningClaimTourRequest> Claims = [];
    internal readonly List<LearningRecordTourOutcomeRequest> Outcomes = [];
    internal TaskCompletionSource<HalResourceOfLearningMutationReceiptDto>? ClaimReply;
    internal TaskCompletionSource<HalResourceOfLearningMutationReceiptDto>? OutcomeReply;
    internal TaskCompletionSource<HalResourceOfLearningTourPlanDto>? PlanReply;
    internal readonly TaskCompletionSource PlanRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource ClaimObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal LearningClientScope Scope = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "self");
    internal HalResourceOfLearningContextDto Source;
    internal readonly LearningOnboardingCoordinator Coordinator;

    internal LearningTourFixture()
    {
        Source = SourceContext();
        Learning.Scope.Returns(_ => Scope);
        Learning.Context.Returns(_ => Source);
        Registry.Publish(Owner, Presentation, false, NavigationTarget,
            new("shell.help", "learning-shell-help", new ElementReference("help")));
        Api.GetLearningTourPlanAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(),
            cancellationToken: Arg.Any<CancellationToken>()).Returns(_ =>
            {
                PlanRead.TrySetResult();
                return PlanReply?.Task ?? Task.FromResult(Plan());
            });
        Api.ClaimLearningTourAsync(Arg.Any<LearningClaimTourRequest>(),
            cancellationToken: Arg.Any<CancellationToken>()).Returns(call =>
            {
                var input = call.Arg<LearningClaimTourRequest>()
                    ?? throw new InvalidOperationException("A claim request is required.");
                Claims.Add(input);
                ClaimObserved.TrySetResult();
                return ClaimReply?.Task ?? Task.FromResult(Receipt(1));
            });
        Api.RecordLearningTourOutcomeAsync(Arg.Any<LearningRecordTourOutcomeRequest>(),
            cancellationToken: Arg.Any<CancellationToken>()).Returns(call =>
            {
                var input = call.Arg<LearningRecordTourOutcomeRequest>()
                    ?? throw new InvalidOperationException("An outcome request is required.");
                Outcomes.Add(input);
                return OutcomeReply?.Task ?? Task.FromResult(Receipt(input.ExpectedRevision + 1,
                    input.StepKey, input.Action == LearningTourAction.Complete
                        ? LearningTourOutcome.Completed : input.Action == LearningTourAction.Skip
                            ? LearningTourOutcome.Skipped : input.Action == LearningTourAction.Interrupt
                            ? LearningTourOutcome.Interrupted : LearningTourOutcome.InProgress,
                    input.Action == LearningTourAction.Snooze ? Clock.Now.AddHours(1) : null));
            });
        Coordinator = new(Api, Learning, Registry, Renderer, Clock);
    }

    internal async Task StartAsync()
    {
        await Coordinator.OfferAsync();
        await Coordinator.StartAsync();
    }

    internal void ReplaceSnapshot(bool replaceOwner)
    {
        if (replaceOwner)
            Scope = new(Scope.TenantId, Guid.CreateVersion7(), "self");
        Source = SourceContext();
        Learning.Changed += Raise.Event<Action>();
    }

    internal HalResourceOfLearningContextDto SourceContext() => new()
    {
        TenantId = Scope.TenantId,
        UserId = Scope.UserId,
        ContextKey = "self",
        ScopeStamp = "scope",
        LearnerRevision = 0,
        _links = new Dictionary<string, HalLink>
        {
            ["tour-plan"] = new() { Href = "/api/learning/tours/plan" },
            ["record-tour-outcome"] = new() { Href = "/api/learning/tours/outcome" }
        }
    };

    internal HalResourceOfLearningTourPlanDto Plan() => new()
    {
        Family = LearningTourFamily.Shell,
        MaterialVersion = 2,
        ContextKey = "self",
        ScopeStamp = "scope",
        PolicySourceStamp = "policy",
        PlanFingerprint = "plan",
        LearnerRevision = 0,
        IsAvailable = true,
        Steps =
        [
            new() { StepKey = "shell.navigation", StepVersion = 1, TargetKey = "shell.navigation",
                Instruction = new() { Key = "navigation", Fallback = "Find available areas." } },
            new() { StepKey = "shell.help", StepVersion = 1, TargetKey = "shell.help",
                Instruction = new() { Key = "help", Fallback = "Reopen Help." } }
        ],
        _links = new Dictionary<string, HalLink>
        {
            ["claim-tour"] = new() { Href = "/api/learning/tours/claim" }
        }
    };

    internal HalResourceOfLearningMutationReceiptDto Receipt(long revision, string? step = null,
        LearningTourOutcome outcome = LearningTourOutcome.InProgress,
        DateTimeOffset? snoozedUntil = null) => new()
        {
            ScopeStamp = "scope",
            LearnerRevision = revision,
            State = new()
            {
                TenantId = Scope.TenantId,
                UserId = Scope.UserId,
                ContextKey = "self",
                ScopeStamp = "scope",
                LearnerRevision = revision,
                Tours =
            [
                new() { Family = LearningTourFamily.Shell, MaterialVersion = 2, Outcome = outcome,
                    SnoozedUntilUtc = snoozedUntil,
                    CompletedSteps = step is null ? [] :
                        [new() { StepKey = step, StepVersion = 1, CompletedAtUtc = Clock.Now }] }
            ]
            },
            Claim = new()
            {
                Token = Guid.CreateVersion7(),
                Family = LearningTourFamily.Shell,
                MaterialVersion = 2,
                PlanFingerprint = "plan",
                PolicySourceStamp = "policy",
                ExpiresAtUtc = Clock.Now.AddMinutes(5),
                Revision = revision
            }
        };

    public void Dispose() => Coordinator.Dispose();
}

internal sealed class RecordingLearningRenderer : ILearningTourRenderer
{
    internal List<string> Shown { get; } = [];
    internal bool IsVisible { get; private set; }
    public Task ShowAsync(LearningTourStepDto step, LearningTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Shown.Add(step.StepKey);
        IsVisible = true;
        return Task.CompletedTask;
    }
    public Task CloseAsync()
    {
        IsVisible = false;
        return Task.CompletedTask;
    }
}

internal sealed class LearningTestClock : TimeProvider
{
    internal DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
