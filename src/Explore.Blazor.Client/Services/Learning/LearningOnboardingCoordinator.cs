using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Learning;

namespace Explore.Blazor.Client.Services.Learning;

/// <summary>
/// Owns explicit starts and single-flight semantic progress. Renderers never
/// decide whether a tenant/user/plan transition has been durably accepted.
/// </summary>
public sealed class LearningOnboardingCoordinator : ILearningOnboardingCoordinator, IDisposable
{
    private readonly ILearningClient _client;
    private readonly ILearningClientService _learning;
    private readonly ILearningTargetRegistry _targets;
    private readonly ILearningTourRenderer _renderer;
    private readonly TimeProvider _clock;
    private CancellationTokenSource _lifetime = new();
    private LearningClientScope? _scope;
    private HalResourceOfLearningContextDto? _source;
    private LearningPresentationContext? _presentation;
    private LearningTourClaimDto? _claim;
    private HalResourceOfLearningMutationReceiptDto? _acceptedReceipt;
    private Task? _transition;
    private long _generation;
    private bool _disposed;
    private int _index;
    private long _revision;
    private string? _policyStamp;
    private LearningTourOutcome? _retainedOutcome;

    public LearningOnboardingCoordinator(ILearningClient client, ILearningClientService learning,
        ILearningTargetRegistry targets, ILearningTourRenderer renderer, TimeProvider? clock = null)
    {
        _client = client;
        _learning = learning;
        _targets = targets;
        _renderer = renderer;
        _clock = clock ?? TimeProvider.System;
        _learning.Changed += Observe;
        _targets.Changed += Observe;
    }

    public event Action? Changed;
    public HalResourceOfLearningTourPlanDto? Plan { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsBusy => _transition is { IsCompleted: false };
    public string? Failure { get; private set; }
    public bool CanStart => !_disposed && !IsBusy && !IsActive && !_targets.IsBlocked
        && Plan?.IsAvailable == true && Plan._links?.ContainsKey("claim-tour") == true
        && _source?._links?.ContainsKey("tour-plan") == true
        && Plan.Steps?.Count > 0 && _scope == _learning.Scope;

    public Task OfferAsync(bool automatic = false)
    {
        if (_disposed)
            return Task.CompletedTask;
        Observe();
        return RunAsync((generation, token) => OfferCoreAsync(generation, token, automatic));
    }
    public Task StartAsync() => RunAsync(StartCoreAsync);
    public Task NextAsync() => RunAsync(NextCoreAsync);
    public Task SkipAsync() => RunAsync((generation, token) => IsActive
        ? EndCoreAsync(LearningTourAction.Skip, generation, token)
        : DismissInvitationAsync(LearningTourAction.Skip, null, generation, token));
    public Task SnoozeAsync(LearningSnoozeDuration duration) => RunAsync((generation, token) =>
        DismissInvitationAsync(LearningTourAction.Snooze, duration, generation, token));
    public Task CancelAsync()
    {
        if (_disposed)
            return Task.CompletedTask;
        Invalidate();
        return _renderer.CloseAsync();
    }
    public Task CloseAsync()
    {
        if (IsBusy)
        {
            // Escape must hide immediately, including while a save is uncertain.
            // Its old claim expires rather than guessing the committed revision.
            Invalidate();
            return _renderer.CloseAsync();
        }
        return RunAsync((generation, token) =>
            EndCoreAsync(LearningTourAction.Interrupt, generation, token));
    }

    private Task RunAsync(Func<long, CancellationToken, Task> action)
    {
        if (_disposed)
            return Task.CompletedTask;
        if (_transition is { IsCompleted: false })
            return _transition;
        // Publish ownership before calling async code, including synchronous callbacks.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transition = completion.Task;
        _ = ExecuteAsync(action, completion, _generation, _lifetime.Token);
        return completion.Task;
    }

    private async Task ExecuteAsync(Func<long, CancellationToken, Task> action,
        TaskCompletionSource completion, long generation, CancellationToken token)
    {
        try
        {
            try
            {
                await action(generation, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception) when (exception is ApiException or HttpRequestException
                or TimeoutException or InvalidOperationException or Microsoft.JSInterop.JSException)
            {
                if (Current(generation, token))
                {
                    Failure = "Guidance could not be saved or displayed. Reopen guidance to refresh; no step was assumed complete.";
                    Plan = null;
                    _claim = null;
                    IsActive = false;
                    await _renderer.CloseAsync();
                }
            }
        }
        catch (Exception exception)
        {
            if (Current(generation, token))
                Invalidate();
            completion.TrySetException(exception);
        }
        finally
        {
            completion.TrySetResult();
            if (!_disposed)
                Changed?.Invoke();
        }
    }

    private async Task OfferCoreAsync(long generation, CancellationToken token, bool automatic)
    {
        if (IsActive)
            return;
        if (_learning.Scope is not { } scope || _learning.Context is not { } source
            || source._links?.ContainsKey("tour-plan") != true
            || _learning.IsBusy || _learning.IsSaving || _targets.IsBlocked
            || _targets.Current is not { } presentation)
            return;
        _scope = scope;
        _source = source;
        _presentation = presentation;
        var plan = await _client.GetLearningTourPlanAsync(presentation.Family.ToString(),
            scope.ContextKey, presentation.EventId, cancellationToken: token);
        if (!Current(generation, token))
            return;
        if (plan is null || automatic && plan.CanInvite != true
            || plan.ContextKey != scope.ContextKey || plan.EventContextId != presentation.EventId
            || plan.Family != presentation.Family || plan.IsAvailable != true
            || string.IsNullOrWhiteSpace(plan.ScopeStamp)
            || string.IsNullOrWhiteSpace(plan.PolicySourceStamp)
            || string.IsNullOrWhiteSpace(plan.PlanFingerprint))
            return;
        Plan = plan;
        _policyStamp = plan.PolicySourceStamp;
        _revision = plan.LearnerRevision;
        Failure = null;
    }

    private async Task StartCoreAsync(long generation, CancellationToken token)
    {
        if (Plan is not { } plan || plan.IsAvailable != true || IsActive || _targets.IsBlocked
            || plan._links?.ContainsKey("claim-tour") != true
            || _source?._links?.ContainsKey("tour-plan") != true
            || plan.Steps is not { Count: > 0 } || !Current(generation, token))
            return;
        var receipt = await _client.ClaimLearningTourAsync(new LearningClaimTourRequest
        {
            ExpectedScopeStamp = plan.ScopeStamp,
            ExpectedRevision = _revision,
            IntentId = Guid.CreateVersion7(),
            Family = plan.Family,
            MaterialVersion = plan.MaterialVersion,
            ContextKey = plan.ContextKey,
            EventContextId = plan.EventContextId,
            ExpectedPlanFingerprint = plan.PlanFingerprint,
            ExpectedPolicySourceStamp = plan.PolicySourceStamp,
            Replay = true
        }, cancellationToken: token);
        if (!Current(generation, token))
            return;
        if (!Accept(receipt) || receipt.Claim is not { } claim || claim.Token == Guid.Empty
            || claim.Family != plan.Family || claim.MaterialVersion != plan.MaterialVersion
            || claim.PlanFingerprint != plan.PlanFingerprint || claim.PolicySourceStamp != plan.PolicySourceStamp
            || claim.ExpiresAtUtc <= _clock.GetUtcNow())
            throw new InvalidOperationException("The tour claim was not accepted.");
        _claim = claim;
        var priorOutcome = receipt.State?.Tours?.SingleOrDefault(tour =>
            tour.Family == plan.Family && tour.MaterialVersion == plan.MaterialVersion)?.Outcome;
        _retainedOutcome = priorOutcome is LearningTourOutcome.Completed or LearningTourOutcome.Skipped
            ? priorOutcome : null;
        _index = 0;
        IsActive = true;
        await ShowCurrentAsync(generation, token);
    }

    private async Task NextCoreAsync(long generation, CancellationToken token)
    {
        if (!IsActive || Plan?.Steps is not { } steps || _claim is null
            || !Current(generation, token) || _targets.IsBlocked)
            return;
        if (_claim.ExpiresAtUtc <= _clock.GetUtcNow())
            throw new InvalidOperationException("The original tour scope is no longer writable.");
        var step = steps.ElementAt(_index);
        if (_presentation is null || _targets.Find(_presentation, step.TargetKey) is null)
            throw new InvalidOperationException("The rendered target is no longer available.");
        if (_acceptedReceipt?.State?.Tours?.Any(tour =>
                tour.Family == Plan.Family && tour.MaterialVersion == Plan.MaterialVersion
                && tour.CompletedSteps?.Any(completed => completed.StepKey == step.StepKey
                    && completed.StepVersion == step.StepVersion) == true) != true)
            await RecordAsync(LearningTourAction.Progress, step, generation, token);
        if (!Current(generation, token))
            return;
        if (_index + 1 == steps.Count)
        {
            await RecordAsync(LearningTourAction.Complete, null, generation, token);
            if (!Current(generation, token))
                return;
            IsActive = false;
            _claim = null;
            Plan = null;
            await _renderer.CloseAsync();
            return;
        }
        ++_index;
        await ShowCurrentAsync(generation, token);
    }

    private async Task ShowCurrentAsync(long generation, CancellationToken token)
    {
        var steps = Plan?.Steps
            ?? throw new InvalidOperationException("The tour plan is no longer available.");
        var presentation = _presentation
            ?? throw new InvalidOperationException("The tour context is no longer available.");
        var step = steps.ElementAt(_index);
        var target = await _targets.WaitAsync(presentation, step.TargetKey, token);
        if (Current(generation, token))
            await _renderer.ShowAsync(step, target, token);
    }

    private async Task EndCoreAsync(LearningTourAction action, long generation, CancellationToken token)
    {
        var active = IsActive;
        IsActive = false;
        await _renderer.CloseAsync();
        if (active && Current(generation, token) && _claim is not null)
            await RecordAsync(action, null, generation, token);
        if (Current(generation, token))
        {
            _claim = null;
            Plan = null;
        }
    }

    private async Task DismissInvitationAsync(LearningTourAction action,
        LearningSnoozeDuration? duration, long generation, CancellationToken token)
    {
        if (IsActive || Plan is null || !Current(generation, token))
            return;
        await RecordAsync(action, null, generation, token, duration);
        if (Current(generation, token))
            Plan = null;
    }

    private async Task RecordAsync(LearningTourAction action, LearningTourStepDto? step,
        long generation, CancellationToken token, LearningSnoozeDuration? duration = null)
    {
        var plan = Plan!;
        if (!Current(generation, token)
            || _claim is null && action is not (LearningTourAction.Skip or LearningTourAction.Snooze)
            || _claim is not null && _claim.ExpiresAtUtc <= _clock.GetUtcNow()
            || _source?._links?.ContainsKey("record-tour-outcome") != true)
            throw new InvalidOperationException("The original tour scope is no longer writable.");
        var receipt = await _client.RecordLearningTourOutcomeAsync(new LearningRecordTourOutcomeRequest
        {
            ExpectedScopeStamp = plan.ScopeStamp,
            ExpectedRevision = _revision,
            IntentId = Guid.CreateVersion7(),
            ClaimToken = _claim?.Token,
            ContextKey = plan.ContextKey,
            EventContextId = plan.EventContextId,
            Family = plan.Family,
            MaterialVersion = plan.MaterialVersion,
            Action = action,
            StepKey = step?.StepKey,
            StepVersion = step?.StepVersion,
            SnoozeDuration = duration,
            ExpectedPlanFingerprint = plan.PlanFingerprint,
            ExpectedPolicySourceStamp = plan.PolicySourceStamp
        }, cancellationToken: token);
        if (!Current(generation, token))
            return;
        if (!Accept(receipt))
            throw new InvalidOperationException("The tour outcome was not accepted.");
        var progress = receipt.State?.Tours?.SingleOrDefault(item =>
            item.Family == plan.Family && item.MaterialVersion == plan.MaterialVersion);
        if (action == LearningTourAction.Progress
            && progress?.CompletedSteps?.Any(item => item.StepKey == step!.StepKey
                && item.StepVersion == step.StepVersion) != true
            || _retainedOutcome is not null && progress?.Outcome != _retainedOutcome
            || action == LearningTourAction.Complete
                && progress?.Outcome != (_retainedOutcome ?? LearningTourOutcome.Completed)
            || action == LearningTourAction.Skip
                && progress?.Outcome is not (LearningTourOutcome.Skipped or LearningTourOutcome.Completed)
            || action == LearningTourAction.Snooze
                && (progress?.SnoozedUntilUtc is not { } until || until <= _clock.GetUtcNow())
            || action == LearningTourAction.Interrupt
                && progress?.Outcome != (_retainedOutcome ?? LearningTourOutcome.Interrupted))
            throw new InvalidOperationException("The semantic tour transition was not accepted.");
    }

    private bool Accept(HalResourceOfLearningMutationReceiptDto? receipt)
    {
        if (receipt?.State is not { } state || Plan is not { } plan || _scope is null
            || receipt.ScopeStamp != plan.ScopeStamp || receipt.LearnerRevision <= _revision
            || state.TenantId != _scope.TenantId || state.UserId != _scope.UserId
            || state.ContextKey != _scope.ContextKey || state.EventContextId != plan.EventContextId
            || state.ScopeStamp != plan.ScopeStamp || state.LearnerRevision != receipt.LearnerRevision)
            return false;
        _revision = receipt.LearnerRevision;
        _acceptedReceipt = receipt;
        return true;
    }

    private bool Current(long generation, CancellationToken token) =>
        !_disposed && !token.IsCancellationRequested && generation == _generation
        && _scope == _learning.Scope && ReferenceEquals(_source, _learning.Context)
        && !_learning.IsBusy && !_learning.IsSaving && _presentation == _targets.Current
        && !_targets.IsBlocked && (Plan is null || Plan.PolicySourceStamp == _policyStamp);

    private void Observe()
    {
        if (_disposed)
            return;
        var targetLost = IsActive && Plan?.Steps is { } steps && _presentation is not null
            && _targets.Find(_presentation, steps.ElementAt(_index).TargetKey) is null;
        if (_scope != _learning.Scope || !ReferenceEquals(_source, _learning.Context)
            || _presentation != _targets.Current || _targets.IsBlocked
            || _learning.IsBusy || _learning.IsSaving || targetLost)
        {
            Invalidate();
            _scope = _learning.Scope;
            _source = _learning.Context;
            _presentation = _targets.Current;
            // No stale-scope interruption write; the captured server lease expires.
            _ = _renderer.CloseAsync();
            Changed?.Invoke();
        }
    }

    private void Invalidate()
    {
        ++_generation;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lifetime = new();
        Plan = null;
        IsActive = false;
        _claim = null;
        _acceptedReceipt = null;
        _retainedOutcome = null;
        _retainedOutcome = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _learning.Changed -= Observe;
        _targets.Changed -= Observe;
        _lifetime.Cancel();
        _lifetime.Dispose();
        Plan = null;
        IsActive = false;
        _claim = null;
        Changed = null;
        _ = _renderer.CloseAsync();
    }
}
