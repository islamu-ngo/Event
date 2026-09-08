namespace Explore.Application.Settings;

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;

public sealed class PublicationPolicyMutationBoundary : IPublicationPolicyMutationBoundary
{
    private const string SuccessMessage = "The publication policy was updated.";

    private readonly ISettingMutationLock _mutationLock;
    private readonly ICoordinatedSettingMutationStore _store;

    public PublicationPolicyMutationBoundary(
        ISettingMutationLock mutationLock,
        ICoordinatedSettingMutationStore store)
    {
        _mutationLock = mutationLock;
        _store = store;
    }

    public Task<PublicationPolicyMutationResult> ApplyTenantAsync(
        PublicationPolicyTenantMutationRequest request,
        CancellationToken cancellationToken = default) =>
        _mutationLock.ExecuteManyAsync(
            PublicationPolicySettingKeys.All,
            token => ApplyTenantInCurrentTransactionAsync(request, token),
            cancellationToken);

    public async Task<PublicationPolicyMutationResult>
        ApplyTenantInCurrentTransactionAsync(
            PublicationPolicyTenantMutationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PublicationPolicyMutationSnapshot snapshot =
            await _store.ReadTenantSnapshotAsync(
                request.TenantId,
                cancellationToken);

        if (!TryApplyLockedSystemBehavior(
                request,
                snapshot.SystemValues,
                out ImmutableArray<PublicationPolicySettingMutation>
                    effectiveMutations,
                out PublicationPolicyMutationResult? lockedFailure))
        {
            return lockedFailure!;
        }

        PublicationPolicyCompilationResult compilation =
            PublicationPolicyProposedStateCompiler.CompileTenant(
                new PublicationPolicyTenantCompilationInput(
                    request.TenantId,
                    snapshot.SystemValues,
                    snapshot.TenantValues,
                    effectiveMutations));
        if (!compilation.Success)
            return Invalid();

        ReportingIntakePolicyEvaluation evaluation =
            ReportingIntakePolicyEvaluator.Evaluate(
                compilation.TenantStates[0].State);
        if (!evaluation.Allowed)
            return Unsafe(evaluation);

        ImmutableArray<PublicationPolicySettingMutation> orderedMutations =
            OrderMutations(effectiveMutations);
        CoordinatedSettingMutationWriteResult writeResult =
            await _store.WriteTenantAsync(
                request.TenantId,
                orderedMutations,
                request.ActorUserId,
                request.OccurredAtUtc,
                cancellationToken);

        return Succeeded(MapTenantNotifications(writeResult, request));
    }

    public Task<PublicationPolicyMutationResult> ApplyInstanceAsync(
        PublicationPolicyInstanceMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _mutationLock.ExecuteManyAsync(
            PublicationPolicySettingKeys.All,
            token => ApplyInstanceInCurrentTransactionAsync(request, token),
            cancellationToken);
    }

    public async Task<PublicationPolicyMutationResult>
        ApplyInstanceInCurrentTransactionAsync(
            PublicationPolicyInstanceMutationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PublicationPolicyMutationSnapshot snapshot =
            await _store.ReadInstanceSnapshotAsync(cancellationToken);
        PublicationPolicyCompilationResult compilation =
            PublicationPolicyProposedStateCompiler.CompileInstance(
                new PublicationPolicyInstanceCompilationInput(
                    snapshot.SystemValues,
                    snapshot.TenantValues,
                    request.Mutations));
        if (!compilation.Success)
            return Invalid();

        if (compilation.BaseTenantState is not ReportingIntakePolicyState baseState)
            return Invalid();

        ReportingIntakePolicyEvaluation baseEvaluation =
            ReportingIntakePolicyEvaluator.Evaluate(baseState);
        if (!baseEvaluation.Allowed)
            return Unsafe(baseEvaluation);

        foreach (PublicationPolicyCompiledTenantState tenantState
                 in compilation.TenantStates)
        {
            ReportingIntakePolicyEvaluation tenantEvaluation =
                ReportingIntakePolicyEvaluator.Evaluate(tenantState.State);
            if (!tenantEvaluation.Allowed)
                return Unsafe(tenantEvaluation);
        }

        ImmutableArray<PublicationPolicySettingMutation> orderedMutations =
            OrderMutations(request.Mutations);
        CoordinatedSettingMutationWriteResult writeResult =
            await _store.WriteInstanceAsync(
                orderedMutations,
                request.ActorUserId,
                request.OccurredAtUtc,
                cancellationToken);

        return Succeeded(
            MapInstanceNotifications(writeResult, request, orderedMutations));
    }

    private static bool TryApplyLockedSystemBehavior(
        PublicationPolicyTenantMutationRequest request,
        ImmutableArray<PublicationPolicySystemValueSnapshot> systemValues,
        out ImmutableArray<PublicationPolicySettingMutation> effectiveMutations,
        out PublicationPolicyMutationResult? failure)
    {
        effectiveMutations = request.Mutations;
        failure = null;
        if (!Enum.IsDefined(request.LockedSystemBehavior))
        {
            failure = Invalid();
            return false;
        }

        var lockedKeys = new HashSet<string>(StringComparer.Ordinal);
        if (!systemValues.IsDefault)
        {
            foreach (PublicationPolicySystemValueSnapshot? systemValue in systemValues)
            {
                if (systemValue is { IsLocked: true })
                    lockedKeys.Add(systemValue.Key);
            }
        }

        bool touchesLockedKey = !request.Mutations.IsDefault
            && request.Mutations.Any(mutation => mutation is not null && lockedKeys.Contains(mutation.Key));
        if (!touchesLockedKey)
            return true;

        if (request.LockedSystemBehavior == PublicationPolicyLockedSystemBehavior.Reject)
        {
            failure = new PublicationPolicyMutationResult(
                Success: false,
                FailureCode: PublicationPolicyMutationFailureCodes.LockedPolicy,
                Message: PublicationPolicyMutationMessages.LockedPolicy,
                DeferredNotifications: []);
            return false;
        }

        var transformed = ImmutableArray.CreateBuilder<PublicationPolicySettingMutation>(request.Mutations.Length);
        foreach (PublicationPolicySettingMutation? mutation in request.Mutations)
        {
            transformed.Add(mutation is not null && lockedKeys.Contains(mutation.Key)
                ? new PublicationPolicySettingMutation(
                    mutation.Key,
                    PublicationPolicyMutationKind.Remove,
                    JsonValue: null,
                    request.TenantId,
                    IsLocked: null)
                : mutation!);
        }

        effectiveMutations = transformed.MoveToImmutable();
        return true;
    }

    private static ImmutableArray<PublicationPolicySettingMutation> OrderMutations(
        ImmutableArray<PublicationPolicySettingMutation> mutations) =>
        mutations
            .OrderBy(mutation => CanonicalOrder(mutation.Key))
            .ToImmutableArray();

    private static ImmutableArray<SettingChangedNotification> MapTenantNotifications(
        CoordinatedSettingMutationWriteResult writeResult,
        PublicationPolicyTenantMutationRequest request) =>
        OrderedChanges(writeResult)
            .Select(change => new SettingChangedNotification(
                change.Key,
                change.OldValue,
                change.NewValue,
                SettingSource.TenantOverride,
                request.TenantId,
                request.ActorUserId,
                request.OccurredAtUtc))
            .ToImmutableArray();

    private static ImmutableArray<SettingChangedNotification> MapInstanceNotifications(
        CoordinatedSettingMutationWriteResult writeResult,
        PublicationPolicyInstanceMutationRequest request,
        ImmutableArray<PublicationPolicySettingMutation> mutations)
    {
        IReadOnlyDictionary<string, PublicationPolicySettingMutation> mutationsByKey = mutations
            .ToDictionary(mutation => mutation.Key, StringComparer.Ordinal);

        return OrderedChanges(writeResult)
            .Select(change => new SettingChangedNotification(
                change.Key,
                change.OldValue,
                change.NewValue,
                mutationsByKey.TryGetValue(change.Key, out PublicationPolicySettingMutation? mutation)
                    && mutation.IsLocked == true
                        ? SettingSource.SystemLocked
                        : SettingSource.SystemDefault,
                tenantId: null,
                request.ActorUserId,
                request.OccurredAtUtc))
            .ToImmutableArray();
    }

    private static IEnumerable<CoordinatedSettingValueChange> OrderedChanges(
        CoordinatedSettingMutationWriteResult writeResult) =>
        PublicationPolicySettingKeys.All.SelectMany(key =>
            writeResult.Changes.Where(change => change is not null && change.Key == key));

    private static int CanonicalOrder(string key)
    {
        for (int index = 0; index < PublicationPolicySettingKeys.All.Count; index++)
        {
            if (string.Equals(PublicationPolicySettingKeys.All[index], key, StringComparison.Ordinal))
                return index;
        }

        return int.MaxValue;
    }

    private static PublicationPolicyMutationResult Invalid() => new(
        Success: false,
        FailureCode: PublicationPolicyMutationFailureCodes.InvalidPolicy,
        Message: PublicationPolicyMutationMessages.InvalidPolicy,
        DeferredNotifications: []);

    private static PublicationPolicyMutationResult Unsafe(ReportingIntakePolicyEvaluation evaluation) => new(
        Success: false,
        FailureCode: evaluation.ReasonCode,
        Message: evaluation.Message,
        DeferredNotifications: []);

    private static PublicationPolicyMutationResult Succeeded(
        ImmutableArray<SettingChangedNotification> notifications) => new(
        Success: true,
        FailureCode: null,
        Message: SuccessMessage,
        DeferredNotifications: notifications);
}
