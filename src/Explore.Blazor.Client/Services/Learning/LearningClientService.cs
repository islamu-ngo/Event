using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Learning;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Services.Learning;

public sealed record LearningClientScope(Guid TenantId, Guid UserId, string ContextKey);

public enum LearningClientFailure
{
    None,
    SignInRequired,
    Unavailable,
    Conflict,
    SaveNotApplied,
    RequestFailed
}

/// <summary>
/// Owns interactive private learning reads and single-attempt, server-advertised
/// simulation writes. Scope changes invalidate both transport and retained state.
/// </summary>
public sealed class LearningClientService : ILearningClientService
{
    private readonly ILearningClient _client;
    private readonly AuthenticationStateProvider _authentication;
    private readonly CurrentUserState _currentUser;
    private readonly UiShellState _shell;
    private readonly ILogger<LearningClientService> _logger;
    private readonly object _stateGate = new();
    private CancellationTokenSource? _pending;
    private Guid? _tenantId;
    private Guid? _observedUserId;
    private string _contextKey;
    private long _generation;
    private bool _disposed;
    private bool _saveRequiresReload;

    public LearningClientService(
        ILearningClient client,
        AuthenticationStateProvider authentication,
        CurrentUserState currentUser,
        UiShellState shell,
        ILogger<LearningClientService> logger)
    {
        _client = client;
        _authentication = authentication;
        _currentUser = currentUser;
        _shell = shell;
        _logger = logger;
        _observedUserId = CurrentUserId();
        _contextKey = SelectedContextKey();
        _authentication.AuthenticationStateChanged += OnAuthenticationChanged;
        _currentUser.OnChanged += OnCurrentUserChanged;
        _shell.Changed += OnShellChanged;
    }

    public event Action? Changed;
    public LearningClientScope? Scope { get; private set; }
    public HalResourceOfLearningContextDto? Context { get; private set; }
    public HalResourceOfLearningPracticeDto? Practice { get; private set; }
    public LearningClientFailure Failure { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsSaving { get; private set; }
    public bool RequiresContextRefresh { get; private set; } = true;
    public bool? LastMutationApplied { get; private set; }
    public bool CanSubmit => !IsBusy && !_saveRequiresReload;

    public IEnumerable<LearningLessonDto> Lessons =>
        Context?.Lessons?.Where(lesson => IsSupportedRecipe(lesson.RecipeKey)) ?? [];

    public IEnumerable<LearningPracticeSummaryDto> OwnedPractices =>
        Context?.OwnedPractices ?? [];

    public static bool IsSupportedRecipe(string recipeKey) =>
        recipeKey is "event-details" or "sessions-program" or "ticket-capacity"
            or "attendees-team" or "publication-lifecycle" or "ai-example-review";

    public bool HasContextAction(string relation) =>
        Context?._links?.ContainsKey(relation) == true;

    public bool HasPracticeAction(string relation) =>
        Practice?._links?.ContainsKey(relation) == true;

    public bool CanPreview(LearningPracticeSummaryDto practice) =>
        Context?.SpaceStatus != LearningSpaceStatus.Removed
        && practice.PreviewAvailable == true && practice.ContextKey == Scope?.ContextKey
        && IsSupportedRecipe(practice.RecipeKey);

    public void SetTenantContext(Guid? tenantId)
    {
        if (_disposed || _tenantId == tenantId)
            return;

        _tenantId = tenantId;
        InvalidateScope();
    }

    /// <summary>Called by the owning page only after its interactive render.</summary>
    public Task LoadContextAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(null, cancellationToken);

    /// <summary>A read never starts an exercise or recreates a removed source.</summary>
    public Task LoadPracticeAsync(Guid practiceId, CancellationToken cancellationToken = default) =>
        LoadAsync(practiceId, cancellationToken);

    public async Task<Guid?> StartPracticeAsync(
        LearningLessonDto lesson, CancellationToken cancellationToken = default)
    {
        var source = Context;
        var current = Lessons.FirstOrDefault(candidate =>
            candidate.RecipeKey == lesson.RecipeKey && candidate.MaterialVersion == lesson.MaterialVersion);
        if (source is null || current is null || !CanSubmit || !HasContextAction("start-simulation")
            || current.Transitions?.Any(transition => transition.FromStepKey == "absent"
                && transition.Action == LearningPracticeAction.Start) != true)
            return null;

        var request = Request(source, current.RecipeKey, current.MaterialVersion,
            LearningPracticeAction.Start, source.ContextKey ?? "self", null, 0, null);
        var receipt = await MutateAsync(source,
            token => _client.ApplyLearningPracticeActionAsync(request, cancellationToken: token),
            result => result.Practice is { } practice
                && practice.Id == result.Id && practice.RecipeKey == request.RecipeKey
                && practice.MaterialVersion == request.MaterialVersion
                && practice.ContextKey == request.ContextKey && practice.Revision > 0
                && result.LearnerRevision > source.LearnerRevision,
            refreshPractice: true, cancellationToken);
        return receipt?.Id;
    }

    public async Task<bool> ApplyPracticeActionAsync(
        LearningPracticeAction action, string? choiceKey = null,
        CancellationToken cancellationToken = default)
    {
        var source = Context;
        var practice = Practice;
        if (source is null || practice is null || !CanSubmit
            || practice.ContextKey != Scope?.ContextKey)
            return false;

        string? relation = action switch
        {
            LearningPracticeAction.Choose => "simulate-choice",
            LearningPracticeAction.Confirm => "simulate-confirm",
            LearningPracticeAction.Reset => "reset-simulation",
            LearningPracticeAction.Clear => "clear-practice",
            _ => null
        };
        var lesson = Lessons.FirstOrDefault(item => item.RecipeKey == practice.RecipeKey
            && item.MaterialVersion == practice.MaterialVersion);
        var transition = lesson?.Transitions?.FirstOrDefault(item =>
            item.FromStepKey == practice.Simulation.StepKey
            && item.Action == action && item.ChoiceKey == choiceKey);
        if (relation is null || !HasPracticeAction(relation)
            || action != LearningPracticeAction.Clear && transition is null)
            return false;

        var request = Request(source, practice.RecipeKey, practice.MaterialVersion,
            action, practice.ContextKey, practice.Id, practice.Revision, choiceKey);
        var receipt = await MutateAsync(source,
            token => _client.ApplyLearningPracticeActionAsync(request, cancellationToken: token),
            result => result.Id == practice.Id && result.LearnerRevision > source.LearnerRevision
                && (action == LearningPracticeAction.Clear
                    ? result.Practice is null && result.SimulationStatus == LearningSimulationStatus.Cleared
                    : result.Practice is { } updated && updated.Id == practice.Id
                        && updated.ContextKey == practice.ContextKey
                        && updated.RecipeKey == practice.RecipeKey
                        && updated.MaterialVersion == practice.MaterialVersion
                        && updated.Revision > practice.Revision
                        && updated.Simulation.StepKey == transition?.ToStepKey),
            refreshPractice: action != LearningPracticeAction.Clear, cancellationToken);
        return receipt is not null;
    }

    public async Task<bool> ClearPracticeAsync(
        LearningPracticeSummaryDto practice, CancellationToken cancellationToken = default)
    {
        var source = Context;
        var owned = OwnedPractices.FirstOrDefault(item => item.Id == practice.Id
            && item.Revision == practice.Revision && item.ContextKey == practice.ContextKey);
        if (source is null || owned is null || !CanSubmit || !HasContextAction("clear-practice"))
            return false;

        var request = Request(source, owned.RecipeKey, owned.MaterialVersion,
            LearningPracticeAction.Clear, owned.ContextKey, owned.Id, owned.Revision, null);
        var receipt = await MutateAsync(source,
            token => _client.ApplyLearningPracticeActionAsync(request, cancellationToken: token),
            result => result.Id == owned.Id && result.Practice is null
                && result.SimulationStatus == LearningSimulationStatus.Cleared
                && result.LearnerRevision > source.LearnerRevision,
            refreshPractice: false, cancellationToken);
        return receipt is not null;
    }

    public async Task<bool> ManageSpaceAsync(
        LearningSpaceAction action, CancellationToken cancellationToken = default)
    {
        var source = Context;
        string relation = action == LearningSpaceAction.Remove
            ? "remove-learning-space" : "recreate-learning-space";
        if (source is null || !CanSubmit || !HasContextAction(relation))
            return false;

        var request = new LearningManageSpaceRequest
        {
            ExpectedScopeStamp = source.ScopeStamp,
            ExpectedRevision = source.ResourceRevision,
            Action = action
        };
        var receipt = await MutateAsync(source,
            token => _client.ManageTenantLearningSpaceAsync(request, cancellationToken: token),
            result => result.Space is { } space && result.Id == space.Id
                && space.Revision > source.ResourceRevision
                && space.Status == (action == LearningSpaceAction.Remove
                    ? LearningSpaceStatus.Removed : LearningSpaceStatus.Active),
            refreshPractice: false, cancellationToken);
        return receipt is not null;
    }

    public void Clear()
    {
        if (!_disposed)
            InvalidateScope();
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
            _currentUser.OnChanged -= OnCurrentUserChanged;
            _shell.Changed -= OnShellChanged;
            CancelPending();
            Context = null;
            Practice = null;
            Scope = null;
            Changed = null;
        }
        GC.SuppressFinalize(this);
    }

    private async Task LoadAsync(Guid? practiceId, CancellationToken cancellationToken)
    {
        if (_disposed)
            return;

        var operation = BeginOperation(saving: false, cancellationToken);
        var token = operation.Token;
        var generation = _generation;
        string contextKey = _contextKey;
        Context = null;
        Practice = null;
        Changed?.Invoke();
        try
        {
            if (!await AuthenticateAsync(generation, token))
                return;

            var context = await _client.GetLearningContextAsync(contextKey, cancellationToken: token);
            lock (_stateGate)
            {
                if (!IsCurrent(generation, token))
                    return;
                if (!MatchesContext(context, contextKey))
                {
                    Failure = LearningClientFailure.Unavailable;
                    Scope = null;
                    return;
                }
                Scope = new(context.TenantId, context.UserId, contextKey);
                Context = context;
            }
            if (practiceId is { } id)
            {
                var owned = context.OwnedPractices?.FirstOrDefault(item => item.Id == id);
                if (owned is null || !CanPreview(owned))
                {
                    lock (_stateGate)
                    {
                        if (IsCurrent(generation, token))
                            Failure = LearningClientFailure.Unavailable;
                    }
                    return;
                }
                var practice = await _client.GetLearningPracticeAsync(id, cancellationToken: token);
                lock (_stateGate)
                {
                    if (!IsCurrent(generation, token))
                        return;
                    if (!MatchesPractice(practice, context, id))
                    {
                        Failure = LearningClientFailure.Unavailable;
                        return;
                    }
                    Practice = practice;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            lock (_stateGate)
            {
                if (IsCurrent(generation, token))
                    ReportFailure(exception);
            }
        }
        finally
        {
            CompleteOperation(operation, generation);
        }
    }

    private async Task<HalResourceOfLearningMutationReceiptDto?> MutateAsync(
        HalResourceOfLearningContextDto source,
        Func<CancellationToken, Task<HalResourceOfLearningMutationReceiptDto>> write,
        Func<HalResourceOfLearningMutationReceiptDto, bool> applied,
        bool refreshPractice, CancellationToken cancellationToken)
    {
        var scope = Scope;
        if (_disposed || scope is null || !ReferenceEquals(source, Context))
            return null;

        var operation = BeginOperation(saving: true, cancellationToken);
        var token = operation.Token;
        var generation = _generation;
        bool writeAttempted = false;
        HalResourceOfLearningMutationReceiptDto? acceptedReceipt = null;
        Changed?.Invoke();
        try
        {
            if (!await AuthenticateAsync(generation, token))
                return null;

            Task<HalResourceOfLearningMutationReceiptDto> response;
            lock (_stateGate)
            {
                if (!IsCurrent(generation, token) || Scope != scope || !ReferenceEquals(source, Context))
                    return null;
                writeAttempted = true;
                response = write(token);
            }
            var receipt = await response;
            if (!IsCurrent(generation, token))
                return null;
            if (receipt is null || receipt.ScopeStamp != source.ScopeStamp
                || receipt.State is not { } state || state.TenantId != scope.TenantId
                || state.UserId != scope.UserId || !applied(receipt))
            {
                lock (_stateGate)
                {
                    if (!IsCurrent(generation, token))
                        return null;
                    Failure = LearningClientFailure.SaveNotApplied;
                    LastMutationApplied = false;
                    _saveRequiresReload = true;
                }
                return null;
            }

            // Nested receipt records are not fresh HAL grants. Re-read the actual
            // resources without replaying the mutation or resurrecting cleared data.
            var context = await _client.GetLearningContextAsync(scope.ContextKey, cancellationToken: token);
            if (!IsCurrent(generation, token))
                return null;
            if (!MatchesContext(context, scope.ContextKey)
                || context.ScopeStamp != source.ScopeStamp
                || context.LearnerRevision < receipt.LearnerRevision)
            {
                lock (_stateGate)
                {
                    if (!IsCurrent(generation, token))
                        return null;
                    Failure = LearningClientFailure.Conflict;
                    _saveRequiresReload = true;
                }
                return null;
            }

            HalResourceOfLearningPracticeDto? practice = null;
            if (refreshPractice)
            {
                practice = await _client.GetLearningPracticeAsync(receipt.Id, cancellationToken: token);
                if (!IsCurrent(generation, token))
                    return null;
                if (practice is null || !MatchesPractice(practice, context, receipt.Id)
                    || receipt.Practice is not { } settledPractice
                    || practice.Revision < settledPractice.Revision)
                {
                    lock (_stateGate)
                    {
                        if (!IsCurrent(generation, token))
                            return null;
                        Failure = LearningClientFailure.Conflict;
                        _saveRequiresReload = true;
                    }
                    return null;
                }
            }
            lock (_stateGate)
            {
                if (!IsCurrent(generation, token))
                    return null;
                Context = context;
                Practice = practice;
                LastMutationApplied = true;
            }
            acceptedReceipt = receipt;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            lock (_stateGate)
            {
                if (writeAttempted && !_disposed && generation == _generation && Scope == scope)
                {
                    Failure = LearningClientFailure.RequestFailed;
                    LastMutationApplied = null;
                    _saveRequiresReload = true;
                }
            }
        }
        catch (Exception exception)
        {
            lock (_stateGate)
            {
                if (IsCurrent(generation, token))
                {
                    ReportFailure(exception);
                    _saveRequiresReload = true;
                }
            }
        }
        finally
        {
            lock (_stateGate)
            {
                if (writeAttempted && !_disposed && generation == _generation && Scope == scope
                    && token.IsCancellationRequested)
                {
                    Failure = LearningClientFailure.RequestFailed;
                    LastMutationApplied = null;
                    _saveRequiresReload = true;
                }
            }
            CompleteOperation(operation, generation);
        }
        lock (_stateGate)
            return acceptedReceipt is not null && !_disposed && generation == _generation
                && Scope == scope && _contextKey == SelectedContextKey()
                ? acceptedReceipt
                : null;
    }

    private static LearningApplyPracticeActionRequest Request(
        HalResourceOfLearningContextDto source, string recipeKey, int materialVersion,
        LearningPracticeAction action, string contextKey, Guid? practiceId,
        long practiceRevision, string? choiceKey) => new()
    {
        ExpectedScopeStamp = source.ScopeStamp,
        ExpectedRevision = source.LearnerRevision,
        IntentId = Guid.CreateVersion7(),
        ExpectedPracticeRevision = practiceRevision,
        PracticeId = practiceId,
        ContextKey = contextKey,
        RecipeKey = recipeKey,
        MaterialVersion = materialVersion,
        Action = action,
        ChoiceKey = choiceKey
    };

    private bool MatchesContext(HalResourceOfLearningContextDto? context, string contextKey) =>
        context is not null && context.TenantId != Guid.Empty && context.UserId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.ScopeStamp)
        && context.ContextKey == contextKey
        && (_tenantId is null || context.TenantId == _tenantId)
        && (CurrentUserId() is not { } userId || context.UserId == userId)
        && (Scope is null || context.TenantId == Scope.TenantId && context.UserId == Scope.UserId);

    private static bool MatchesPractice(
        HalResourceOfLearningPracticeDto? practice, HalResourceOfLearningContextDto context, Guid id) =>
        practice is not null && practice.Id == id && practice.ScopeStamp == context.ScopeStamp
        && practice.ContextKey == context.ContextKey && IsSupportedRecipe(practice.RecipeKey);

    private async Task<bool> AuthenticateAsync(long generation, CancellationToken cancellationToken)
    {
        var authentication = await _authentication.GetAuthenticationStateAsync().WaitAsync(cancellationToken);
        lock (_stateGate)
        {
            if (!IsCurrent(generation, cancellationToken))
                return false;
            if (authentication.User.Identity?.IsAuthenticated == true)
                return true;

            Context = null;
            Practice = null;
            Scope = null;
            Failure = LearningClientFailure.SignInRequired;
            return false;
        }
    }

    private CancellationTokenSource BeginOperation(bool saving, CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            CancelPending();
            _pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsBusy = true;
            IsSaving = saving;
            RequiresContextRefresh = false;
            Failure = LearningClientFailure.None;
            LastMutationApplied = null;
            _saveRequiresReload = false;
            return _pending;
        }
    }

    private bool IsCurrent(long generation, CancellationToken token) =>
        !_disposed && !token.IsCancellationRequested && generation == Volatile.Read(ref _generation)
        && _contextKey == SelectedContextKey();

    private void CompleteOperation(CancellationTokenSource operation, long generation)
    {
        bool notify = false;
        lock (_stateGate)
        {
            if (!_disposed && generation == _generation)
            {
                _pending = null;
                IsBusy = false;
                IsSaving = false;
                notify = true;
            }
        }
        operation.Dispose();
        if (notify)
            Changed?.Invoke();
    }

    private void ReportFailure(Exception exception)
    {
        Failure = exception is ApiException api ? api.StatusCode switch
        {
            401 => LearningClientFailure.SignInRequired,
            403 or 404 => LearningClientFailure.Unavailable,
            409 => LearningClientFailure.Conflict,
            _ => LearningClientFailure.RequestFailed
        } : LearningClientFailure.RequestFailed;
        _logger.LogWarning("Private learning request failed with category {Category}.", Failure);
        if (Failure is LearningClientFailure.SignInRequired or LearningClientFailure.Unavailable)
        {
            Context = null;
            Practice = null;
            Scope = null;
        }
    }

    private Guid? CurrentUserId() =>
        _currentUser.Current?.Id is { } userId && userId != Guid.Empty ? userId : null;

    private string SelectedContextKey() =>
        _shell.ActiveActorId is { } actorId ? $"actor:{actorId:D}" : "self";

    private void OnAuthenticationChanged(Task<AuthenticationState> authentication) => InvalidateScope();

    private void OnCurrentUserChanged()
    {
        Guid? userId = CurrentUserId();
        Guid? previousUserId = _observedUserId ?? Scope?.UserId;
        _observedUserId = userId;
        if (userId != previousUserId)
            InvalidateScope();
    }

    private void OnShellChanged()
    {
        string contextKey = SelectedContextKey();
        if (contextKey == _contextKey)
            return;
        _contextKey = contextKey;
        InvalidateScope();
    }

    private void InvalidateScope()
    {
        lock (_stateGate)
        {
            if (_disposed)
                return;
            CancelPending();
            Context = null;
            Practice = null;
            Scope = null;
            Failure = LearningClientFailure.None;
            LastMutationApplied = null;
            _saveRequiresReload = false;
            RequiresContextRefresh = true;
            IsBusy = false;
            IsSaving = false;
        }
        Changed?.Invoke();
    }

    private void CancelPending()
    {
        Interlocked.Increment(ref _generation);
        var pending = Interlocked.Exchange(ref _pending, null);
        pending?.Cancel();
        pending?.Dispose();
    }
}
