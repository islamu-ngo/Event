using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning;
using Explore.Application.Services;
using Explore.Domain.Learning;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Wire = ISLAMU.Wire.Contracts.Learning;

namespace Explore.Persistence.Security;

/// <summary>
/// Settles one prepared learning intent under the original human, retained identity gate
/// and selected Serializable owner. No result is published before receipt consumption.
/// </summary>
internal sealed class HumanLearningMutationStore(
    ExploreDbContext context,
    ExploreDbContextExecutionLease lease,
    IHumanLearningMutationPreparation preparation,
    IServiceProvider services,
    TimeProvider clock) : IHumanLearningMutationStore, IDisposable
{
    private int _used;
    private IHumanLearningMutation? _completed;
    private string? _expectedImage;
    private Dictionary<object, string>? _expectedTracked;
    private DateTime? _claimDeadline;
    private RelationalConnectionIdentity? _connection;
    private IDbContextTransaction? _transaction;
    private DbContextId _contextId;
    private int? _backend;
    private bool _interrupted;

    public async Task<LearningOperationState> ExecuteAsync(
        IHumanLearningMutation mutation, CancellationToken cancellationToken)
    {
        RequireOwner(mutation, transactional: false);
        if (Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("A learning writer permits one original physical attempt.");
        await mutation.RequireOriginalAsync(cancellationToken);
        var identity = services.GetRequiredService<PrivacyIdentityFenceOperation>();
        var authority = services.GetRequiredService<IPrivacyIdentityFenceAuthority>()
            as IOriginalPrivacyIdentityFenceAuthority
            ?? throw new InvalidOperationException("Learning requires original retained identity ownership.");
        bool colocated = authority is EmbeddedPrivacyErasureAuthorityRepository embedded
            && embedded.UsesOriginalApplicationContext(context);
        var owner = colocated
            ? (IOriginalOwnerSettlementBoundary)authority
            : services.GetRequiredService<IOriginalOwnerSettlementBoundary>();
        var invocation = new object();
        bool settled = false;
        try
        {
            return await identity.ExecuteOriginalOwnerIdentityMutationAsync(async (guard, token) =>
            {
                async Task<OriginalOwnerDecision<LearningOperationState>> Settle(CancellationToken gateToken)
                {
                    var receipt = await owner.ExecuteAsync(invocation, async ownerToken =>
                    {
                        BindPhysicalOwner(mutation);
                        services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                        {
                            await RequireCompleteAsync(mutation, finalToken);
                            await mutation.RequireCurrentAsync(finalToken);
                            await identity.EnsureSubjectMayEnrollAsync(mutation.SubjectId, finalToken);
                            await guard.ValidateAsync(finalToken);
                            RequirePhysicalOwner(mutation);
                        }, now =>
                        {
                            mutation.RequireCurrentAt(now);
                            guard.RequireCurrent(now);
                            if (_claimDeadline is { } deadline && now.UtcDateTime >= deadline)
                                throw Conflict("The original learning claim expired before settlement.");
                            RequirePhysicalOwner(mutation);
                        });
                        await mutation.RequireCurrentAsync(ownerToken);
                        RequirePhysicalOwner(mutation);
                        await identity.EnsureSubjectMayEnrollAsync(mutation.SubjectId, ownerToken);
                        var result = await ApplyAsync(mutation, ownerToken);
                        _expectedImage = Image(result.Space, result.State);
                        RequirePermittedChanges(mutation, result);
                        _expectedTracked = context.ChangeTracker.Entries()
                            .Where(entry => entry.State != EntityState.Deleted)
                            .ToDictionary(entry => entry.Entity, entry => ScalarImage(entry.Entity),
                                ReferenceEqualityComparer.Instance);
                        await services.GetRequiredService<ILearningRepository>().SaveChangesAsync(ownerToken);
                        _completed = mutation;
                        await RequireCompleteAsync(mutation, ownerToken);
                        return OriginalOwnerDecision<LearningOperationState>.Commit(result);
                    }, gateToken);
                    var value = owner.Consume(invocation, receipt, OriginalOwnerDisposition.Committed);
                    settled = true;
                    await mutation.RequireOriginalAsync(gateToken);
                    return OriginalOwnerDecision<LearningOperationState>.Commit(value);
                }

                return colocated
                    ? await Settle(token)
                    : await authority.ExecuteOriginalEnrollmentAsync(Settle,
                        finalToken => mutation.RequireOriginalAsync(finalToken),
                        mutation.RequireCurrentAt, token);
            }, mutation.RequireOriginalAsync, cancellationToken);
        }
        catch (Exception exception) when (settled && exception is not HumanMutationOutcomeUnknownException)
        {
            throw new HumanMutationOutcomeUnknownException(exception);
        }
    }

    private async Task<LearningOperationState> ApplyAsync(
        IHumanLearningMutation mutation, CancellationToken token)
    {
        var repository = services.GetRequiredService<ILearningRepository>();
        var space = await repository.GetSpaceAsync(mutation.TenantId, tracked: true, token);
        var state = await repository.GetStateAsync(mutation.TenantId, mutation.SubjectId, tracked: true, token);
        LearningPracticeState? practice = null;
        if (mutation.Intent is LearningSpaceIntent spaceIntent)
        {
            if (!mutation.CanManageSpace)
                throw new AuthorizationException("Current tenant administration is required.");
            if (space is null)
            {
                if (spaceIntent.Action != LearningSpaceMutationAction.Remove || spaceIntent.ExpectedRevision != 0)
                    throw Conflict("There is no removed learning source to recreate.");
                space = TenantLearningSpace.Create(mutation.CandidateSpaceId, mutation.TenantId, mutation.UpdatedAtUtc);
                await repository.AddSpaceAsync(space, token);
            }
            switch (spaceIntent.Action)
            {
                case LearningSpaceMutationAction.Remove:
                    space.Remove(spaceIntent.ExpectedRevision, mutation.UpdatedAtUtc);
                    break;
                case LearningSpaceMutationAction.Recreate:
                    space.Recreate(spaceIntent.ExpectedRevision, mutation.UpdatedAtUtc);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        }
        else
        {
            bool ownerCleanup = mutation.Intent is LearningPreferencesIntent
                or LearningPracticeIntent { Action: LearningPracticeAction.Clear }
                or LearningTourOutcomeIntent { Outcome: not LearningTourOutcome.InProgress };
            if (space?.IsRemoved == true && !ownerCleanup)
                throw Conflict("The shared learning source has been removed.");
            if (space is null && !ownerCleanup)
            {
                space = TenantLearningSpace.Create(mutation.CandidateSpaceId, mutation.TenantId, mutation.UpdatedAtUtc);
                await repository.AddSpaceAsync(space, token);
            }
            if (state is null)
            {
                if (mutation.Intent.ExpectedRevision != 0)
                    throw Conflict("The learner revision is stale.");
                state = UserLearningState.Create(mutation.CandidateStateId, mutation.TenantId,
                    mutation.SubjectId, mutation.UpdatedAtUtc);
                await repository.AddStateAsync(state, token);
            }
            ApplyState(mutation, state);
            if (mutation.Intent is LearningPracticeIntent selected)
                practice = state.Practices.SingleOrDefault(value =>
                    value.ContextKey == selected.ContextKey && value.RecipeKey == selected.RecipeKey);
        }
        string responseContext = mutation.Intent is LearningPracticeIntent { Action: LearningPracticeAction.Clear }
            ? "self" : mutation.ContextKey;
        return new(mutation.TenantId, mutation.SubjectId, responseContext, mutation.EventContextId,
            space, state, practice, mutation.AvailableActors, mutation.CanManageSpace, mutation.CanManageEvent)
        {
            ObservedAtUtc = clock.GetUtcNow().UtcDateTime
        };
    }

    private void ApplyState(IHumanLearningMutation mutation, UserLearningState state)
    {
        try
        {
            switch (mutation.Intent)
            {
                case LearningPreferencesIntent preference:
                    state.UpdatePreferences(preference.SuppressAutomaticTours, preference.HideLearning,
                        preference.PinLearning, preference.ExpectedRevision, mutation.UpdatedAtUtc);
                    break;
                case LearningTourClaimIntent claim:
                {
                    RequireMaterial(claim.MaterialVersion);
                    var progress = state.Tours.SingleOrDefault(value =>
                        value.Family == claim.Family && value.MaterialVersion == claim.MaterialVersion);
                    Guid claimId = (state.LastIntentId == claim.IntentId || progress?.LastIntentId == claim.IntentId)
                        && state.ActiveTourId == progress?.Id && state.ActiveClaimId.HasValue
                        ? state.ActiveClaimId.Value : mutation.CandidateClaimId;
                    state.ClaimTour(progress?.Id ?? mutation.CandidateChildId, claim.Family,
                        claim.MaterialVersion, claimId, claim.IntentId, claim.ExpectedRevision,
                        mutation.UpdatedAtUtc, claim.ContextKey, claim.EventContextId,
                        replay: progress?.Outcome is LearningTourOutcome.Completed or LearningTourOutcome.Skipped);
                    _claimDeadline = state.ClaimExpiresAtUtc;
                    break;
                }
                case LearningTourOutcomeIntent outcome:
                {
                    RequireMaterial(outcome.MaterialVersion);
                    var progress = state.Tours.SingleOrDefault(value =>
                        value.Family == outcome.Family && value.MaterialVersion == outcome.MaterialVersion);
                    if (outcome.ClaimToken is { } claimToken)
                    {
                        bool duplicate = state.LastIntentId == outcome.IntentId
                            || progress?.LastIntentId == outcome.IntentId;
                        if (duplicate && progress?.LastIntentId != outcome.IntentId)
                            throw Conflict("The retained outcome belongs to another tour family or material version.");
                        if (!duplicate && (progress is null || state.ActiveTourId != progress.Id))
                            throw Conflict("The requested tour does not own the active claim.");
                        _claimDeadline = duplicate ? null : state.ClaimExpiresAtUtc;
                        state.RecordTourOutcome(claimToken, outcome.IntentId, outcome.Outcome,
                            outcome.StepIndex, outcome.ExpectedRevision, mutation.UpdatedAtUtc);
                    }
                    else if (outcome.Outcome == LearningTourOutcome.Skipped && outcome.StepIndex == 0)
                        state.SkipInvitation(progress?.Id ?? mutation.CandidateChildId, outcome.Family,
                            outcome.MaterialVersion, outcome.IntentId, outcome.ExpectedRevision, mutation.UpdatedAtUtc);
                    else
                        throw new ArgumentException("Only invitation skip may omit a claim token.");
                    break;
                }
                case LearningPracticeIntent input:
                {
                    RequireMaterial(input.MaterialVersion);
                    var current = state.Practices.SingleOrDefault(value =>
                        value.ContextKey == input.ContextKey && value.RecipeKey == input.RecipeKey);
                    if (input.PracticeId.HasValue && current?.Id != input.PracticeId
                        && !(input.Action == LearningPracticeAction.Clear && state.LastIntentId == input.IntentId))
                        throw new NotFoundException("Learning practice", input.PracticeId);
                    bool duplicate = state.LastIntentId == input.IntentId || current?.LastIntentId == input.IntentId;
                    Wire.LearningSimulationStateDto? restored = null;
                    if (!duplicate && current is not null && !LearningLessonCatalog.TryRestore(current.RecipeKey,
                            current.RecipeVersion, (Wire.LearningPracticeChoiceSlot)current.Choice,
                            current.Step, current.Value, current.IsConfirmed, out restored))
                        throw Conflict("The stored practice is not an authored state.");
                    if (!LearningLessonCatalog.TryGetLesson(input.RecipeKey, input.MaterialVersion, out var lesson))
                        throw new ArgumentException("The recipe is not part of the shipped pack.");
                    var choice = input.ChoiceKey is null ? null
                        : lesson.Choices.SingleOrDefault(value => value.Key == input.ChoiceKey);
                    if (input.Action == LearningPracticeAction.Choose && choice is null)
                        throw new ArgumentException("Choose a shipped recipe option.");
                    if (input.Action != LearningPracticeAction.Choose && input.ChoiceKey is not null)
                        throw new ArgumentException("Only Choose accepts an authored option.");
                    if (!duplicate && !LearningLessonCatalog.TryApply(input.RecipeKey, input.MaterialVersion,
                            (Wire.LearningPracticeAction)input.Action, input.ChoiceKey, restored, out _))
                        throw Conflict("The practice action is not valid in the current state.");
                    state.ApplyPractice(input.PracticeId ?? current?.Id ?? mutation.CandidateChildId,
                        input.ContextKey, input.RecipeKey, input.MaterialVersion, input.Action,
                        input.Action == LearningPracticeAction.Choose
                            ? (LearningPracticeChoice)choice!.Slot : LearningPracticeChoice.None,
                        input.Action == LearningPracticeAction.Choose ? 1 : 0,
                        input.Action == LearningPracticeAction.Choose ? choice!.Value : 0,
                        input.IntentId, input.ExpectedRevision, input.ExpectedPracticeRevision, mutation.UpdatedAtUtc);
                    break;
                }
                default:
                    throw new InvalidOperationException("The learning intent is not registered.");
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new ConcurrencyConflictException("learning_conflict",
                "Learning changed or the requested transition is stale. Refresh the private context.",
                innerException: exception);
        }
    }

    public async Task RequireCompleteAsync(IHumanLearningMutation mutation, CancellationToken token)
    {
        RequirePhysicalOwner(mutation);
        if (!ReferenceEquals(_completed, mutation) || _expectedImage is null || context.ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("The original learning image is incomplete.");
        var tracked = context.ChangeTracker.Entries().ToArray();
        if (_expectedTracked is null || tracked.Length != _expectedTracked.Count
            || tracked.Any(entry => !_expectedTracked.TryGetValue(entry.Entity, out var expected)
                || expected != ScalarImage(entry.Entity)))
            throw new UnauthorizedAccessException("The original learning write inventory changed during saving.");
        var repository = services.GetRequiredService<ILearningRepository>();
        var space = await repository.GetSpaceAsync(mutation.TenantId, tracked: false, token);
        var state = await repository.GetStateAsync(mutation.TenantId, mutation.SubjectId, tracked: false, token);
        if (_expectedImage != Image(space, state))
            throw new UnauthorizedAccessException("The persisted learning completion image changed.");
        RequirePhysicalOwner(mutation);
    }

    private void RequirePermittedChanges(IHumanLearningMutation mutation, LearningOperationState result)
    {
        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(value => value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            bool permitted = entry.Entity switch
            {
                TenantLearningSpace value => ReferenceEquals(value, result.Space) && value.TenantId == mutation.TenantId,
                UserLearningState value => ReferenceEquals(value, result.State)
                    && value.TenantId == mutation.TenantId && value.UserId == mutation.SubjectId,
                LearningTourProgress value => value.TenantId == mutation.TenantId
                    && value.UserLearningStateId == result.State?.Id,
                LearningPracticeState value => value.TenantId == mutation.TenantId
                    && value.UserLearningStateId == result.State?.Id,
                _ => false
            };
            if (!permitted)
                throw new UnauthorizedAccessException("Learning cannot commit a nonlearning effect.");
        }
    }

    private string Image(TenantLearningSpace? space, UserLearningState? state)
    {
        return JsonSerializer.Serialize(new
        {
            Space = space is null ? null : ScalarImage(space),
            State = state is null ? null : ScalarImage(state),
            Tours = state?.Tours.OrderBy(value => value.Id).Select(value => ScalarImage(value)).ToArray(),
            Practices = state?.Practices.OrderBy(value => value.Id).Select(value => ScalarImage(value)).ToArray()
        });
    }

    private string ScalarImage(object entity) => JsonSerializer.Serialize(context.Entry(entity).Properties
        .OrderBy(value => value.Metadata.Name, StringComparer.Ordinal)
        .ToDictionary(value => value.Metadata.Name, value => value.CurrentValue is DateTime timestamp
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc) : value.CurrentValue, StringComparer.Ordinal));

    private void RequireOwner(IHumanLearningMutation mutation, bool transactional)
    {
        context.RequireBoundTenantLease();
        lease.RequireOriginalOwner(context);
        if (!ReferenceEquals(preparation.CurrentMutation, mutation)
            || lease.HumanScope?.Subject.PrincipalId != mutation.SubjectId
            || lease.HumanScope.Purpose != HumanAdministratorCommitPurpose.RuntimeTenant
            || lease.SelectedTenantId != mutation.TenantId || mutation.TenantId == Guid.Empty
            || lease.PolicyPurpose != PolicyMutationPurpose.Standard
            || transactional && context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("Learning requires its original prepared selected-tenant human.");
    }

    private void BindPhysicalOwner(IHumanLearningMutation mutation)
    {
        RequireOwner(mutation, transactional: true);
        if (context.ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("Learning requires a clean original owner.");
        _contextId = context.ContextId;
        _transaction = context.Database.CurrentTransaction;
        _connection = RelationalConnectionIdentity.Capture(context);
        _backend = _connection.BackendProcessId;
        _connection.StateChange += ConnectionChanged;
    }

    private void RequirePhysicalOwner(IHumanLearningMutation mutation)
    {
        RequireOwner(mutation, transactional: true);
        if (context.ContextId != _contextId || !ReferenceEquals(context.Database.CurrentTransaction, _transaction)
            || _connection is null || !_connection.Matches(context) || _connection.State != ConnectionState.Open
            || _connection.BackendProcessId != _backend
            || _interrupted)
            throw new UnauthorizedAccessException("The original learning transaction or connection changed.");
    }

    private static void RequireMaterial(int version)
    {
        if (version != LearningLessonCatalog.MaterialVersion)
            throw Conflict("The learning material is stale or unsupported.");
    }

    private static ConcurrencyConflictException Conflict(string message) => new("learning_conflict", message);
    private void ConnectionChanged(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState != ConnectionState.Open) _interrupted = true;
    }

    public void Dispose()
    {
        if (_connection is not null) _connection.StateChange -= ConnectionChanged;
    }
}
