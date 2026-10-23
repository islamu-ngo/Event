namespace Explore.Application.Settings;

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.Settings;

public sealed class PublicationPolicyMutationBoundary : IPublicationPolicyMutationBoundary
{
    private const string SuccessMessage = "The publication policy was updated.";

    private readonly ISettingMutationLock _mutationLock;
    private readonly ICoordinatedSettingMutationStore _store;
    private readonly IHumanSettingsMutationPreparation _preparation;
    private static readonly AsyncLocal<WriteAdmission?> CurrentWrite = new();
    private static readonly AsyncLocal<ImportWriteAdmission?> CurrentImportWrite = new();

    public async Task<PublicationPolicyMutationResult> ApplyTypedInCurrentTransactionAsync(
        TypedSettingsPublication publication, CancellationToken cancellationToken = default)
    {
        await publication.Store.RequirePublicationAsync(publication, cancellationToken);
        var original = publication.Original;
        var snapshot = publication.Snapshot;
        var mutations = publication.Mutations;
        if (original.TenantId is { } tenant)
        {
            var request = new PublicationPolicyTenantMutationRequest(tenant, original.Actor,
                publication.OccurredAt, mutations, original.Onboarding is not null
                    ? PublicationPolicyLockedSystemBehavior.RemoveOverride : PublicationPolicyLockedSystemBehavior.Reject);
            if (!TryApplyLockedSystemBehavior(request, snapshot.SystemValues, out var narrowed, out var locked))
                return locked!;
            if (!narrowed.SequenceEqual(mutations))
                throw new UnauthorizedAccessException("The acquired original compiler must retain exact publication cleanup.");
            var compiled = PublicationPolicyProposedStateCompiler.CompileTenant(
                new(tenant, snapshot.SystemValues, snapshot.TenantValues, mutations));
            if (!compiled.Success) return Invalid();
            var evaluation = ReportingIntakePolicyEvaluator.Evaluate(compiled.TenantStates[0].State);
            if (!evaluation.Allowed) return Unsafe(evaluation);
            var write = await WriteAsync(tenant, mutations, original.Actor, publication.OccurredAt,
                () => publication.Store.WritePublicationAsync(publication, cancellationToken), cancellationToken);
            return Succeeded(MapTenantNotifications(write, request));
        }
        var instance = PublicationPolicyProposedStateCompiler.CompileInstance(
            new(snapshot.SystemValues, snapshot.TenantValues, mutations));
        if (!instance.Success || instance.BaseTenantState is not { } baseState) return Invalid();
        var baseEvaluation = ReportingIntakePolicyEvaluator.Evaluate(baseState);
        if (!baseEvaluation.Allowed) return Unsafe(baseEvaluation);
        foreach (var tenantState in instance.TenantStates)
        {
            var evaluation = ReportingIntakePolicyEvaluator.Evaluate(tenantState.State);
            if (!evaluation.Allowed) return Unsafe(evaluation);
        }
        var result = await WriteAsync(null, mutations, original.Actor, publication.OccurredAt,
            () => publication.Store.WritePublicationAsync(publication, cancellationToken), cancellationToken);
        return Succeeded(MapInstanceNotifications(result, new(original.Actor, publication.OccurredAt, mutations), mutations));
    }

    public async Task<PublicationPolicyMutationResult> ApplyImportInCurrentTransactionAsync(
        HumanConfigurationImportPublication publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        // This store belongs to the actual section compiler's retained source. Its read
        // checks the private import issuer, not an ambient CurrentProof discriminator.
        var snapshot = await publication.Store.ReadAsync(publication, cancellationToken);
        var mutations = publication.Mutations;
        Guid actor = publication.Subject.PrincipalId;
        if (publication.TenantId is { } tenant)
        {
            var request = new PublicationPolicyTenantMutationRequest(tenant, actor, publication.OccurredAt,
                mutations, PublicationPolicyLockedSystemBehavior.Reject);
            if (!TryApplyLockedSystemBehavior(request, snapshot.SystemValues, out mutations, out var locked))
                return locked!;
            var compiled = PublicationPolicyProposedStateCompiler.CompileTenant(
                new(tenant, snapshot.SystemValues, snapshot.TenantValues, mutations));
            if (!compiled.Success) return Invalid();
            var evaluation = ReportingIntakePolicyEvaluator.Evaluate(compiled.TenantStates[0].State);
            if (!evaluation.Allowed) return Unsafe(evaluation);
            return Succeeded(MapTenantNotifications(await WriteImportAsync(publication, cancellationToken), request));
        }
        var instance = PublicationPolicyProposedStateCompiler.CompileInstance(
            new(snapshot.SystemValues, snapshot.TenantValues, mutations));
        if (!instance.Success || instance.BaseTenantState is not { } baseState) return Invalid();
        var baseEvaluation = ReportingIntakePolicyEvaluator.Evaluate(baseState);
        if (!baseEvaluation.Allowed) return Unsafe(baseEvaluation);
        foreach (var tenantState in instance.TenantStates)
        {
            var evaluation = ReportingIntakePolicyEvaluator.Evaluate(tenantState.State);
            if (!evaluation.Allowed) return Unsafe(evaluation);
        }
        var write = await WriteImportAsync(publication, cancellationToken);
        return Succeeded(MapInstanceNotifications(write,
            new(actor, publication.OccurredAt, mutations), mutations));
    }

    private static async Task<CoordinatedSettingMutationWriteResult> WriteImportAsync(
        HumanConfigurationImportPublication publication, CancellationToken cancellationToken)
    {
        if (CurrentImportWrite.Value is not null)
            throw new UnauthorizedAccessException("Import publication cannot nest or replace its writer admission.");
        var admission = new ImportWriteAdmission(publication);
        CurrentImportWrite.Value = admission;
        try { return await publication.Store.WriteAsync(publication, cancellationToken); }
        finally { admission.Active = false; CurrentImportWrite.Value = null; }
    }

    public static void RequireImportWrite(HumanConfigurationImportPublication publication)
    {
        if (CurrentImportWrite.Value is not { Active: true } admission
            || !ReferenceEquals(admission.Publication, publication))
            throw new UnauthorizedAccessException("The actual import publication boundary must admit this exact writer.");
    }

    private sealed class ImportWriteAdmission(HumanConfigurationImportPublication publication)
    {
        internal HumanConfigurationImportPublication Publication => publication;
        internal volatile bool Active = true;
    }

    public PublicationPolicyMutationBoundary(
        ISettingMutationLock mutationLock,
        ICoordinatedSettingMutationStore store,
        IHumanSettingsMutationPreparation preparation)
    {
        _mutationLock = mutationLock;
        _store = store;
        _preparation = preparation;
    }

    public Task<PublicationPolicyMutationResult> ApplyTenantAsync(
        PublicationPolicyTenantMutationRequest request,
        CancellationToken cancellationToken = default) =>
        _mutationLock.ExecuteManyAsync(
            PublicationPolicySettingKeys.All,
            token => ApplyTenantInCurrentTransactionAsync(request, token),
            cancellationToken);

    public Task<PublicationPolicyMutationResult>
        ApplyTenantInCurrentTransactionAsync(
            PublicationPolicyTenantMutationRequest request,
            CancellationToken cancellationToken = default)
    {
        if (request.Mutations.IsDefaultOrEmpty || request.Mutations.Any(item =>
            item.TenantId != request.TenantId || item.IsLocked.HasValue
            || !Enum.IsDefined(item.Kind)
            || item.Kind == PublicationPolicyMutationKind.Set && item.JsonValue is null
            || item.Kind == PublicationPolicyMutationKind.Remove && item.JsonValue is not null))
            throw new UnauthorizedAccessException("Publication mutations must retain their original tenant shape.");
        return _preparation.ExecutePublicationJoinedAsync(
            request.Mutations.Select(item => item.Key).ToImmutableArray(), request.TenantId,
            request.ActorUserId ?? throw new UnauthorizedAccessException("The original publication actor is required."),
            request.LockedSystemBehavior,
            (mutations, token) => ApplyTenantCoreAsync(request with { Mutations = mutations }, token),
            cancellationToken);
    }

    private async Task<PublicationPolicyMutationResult> ApplyTenantCoreAsync(
        PublicationPolicyTenantMutationRequest request, CancellationToken cancellationToken)
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
            await WriteAsync(request.TenantId, orderedMutations, request.ActorUserId!.Value,
                request.OccurredAtUtc, () => _store.WriteTenantAsync(
                request.TenantId,
                orderedMutations,
                request.ActorUserId,
                request.OccurredAtUtc,
                cancellationToken), cancellationToken);

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

    public Task<PublicationPolicyMutationResult>
        ApplyInstanceInCurrentTransactionAsync(
            PublicationPolicyInstanceMutationRequest request,
            CancellationToken cancellationToken = default)
    {
        if (request.Mutations.IsDefaultOrEmpty || request.Mutations.Any(item =>
            item.TenantId.HasValue || !Enum.IsDefined(item.Kind)
            || item.Kind == PublicationPolicyMutationKind.Set && item.JsonValue is null
            || item.Kind == PublicationPolicyMutationKind.Remove
                && (item.JsonValue is not null || item.IsLocked.HasValue)))
            throw new UnauthorizedAccessException("Publication mutations must retain their original instance shape.");
        return _preparation.ExecutePublicationJoinedAsync(
            request.Mutations.Select(item => item.Key).ToImmutableArray(), null, request.ActorUserId,
            PublicationPolicyLockedSystemBehavior.Reject,
            (mutations, token) => ApplyInstanceCoreAsync(request with { Mutations = mutations }, token),
            cancellationToken);
    }

    private async Task<PublicationPolicyMutationResult> ApplyInstanceCoreAsync(
        PublicationPolicyInstanceMutationRequest request, CancellationToken cancellationToken)
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
            await WriteAsync(null, orderedMutations, request.ActorUserId,
                request.OccurredAtUtc, () => _store.WriteInstanceAsync(
                orderedMutations,
                request.ActorUserId,
                request.OccurredAtUtc,
                cancellationToken), cancellationToken);

        return Succeeded(
            MapInstanceNotifications(writeResult, request, orderedMutations));
    }

    private async Task<CoordinatedSettingMutationWriteResult> WriteAsync(Guid? tenant,
        ImmutableArray<PublicationPolicySettingMutation> mutations, Guid actor, DateTime occurred,
        Func<Task<CoordinatedSettingMutationWriteResult>> write, CancellationToken cancellationToken)
    {
        var anchor = await _preparation.RequirePublicationReadAsync(tenant, cancellationToken);
        var effects = ImmutableArray.CreateBuilder<HumanSettingsMutation>();
        foreach (var item in mutations)
        {
            effects.Add(anchor with
            {
                Key = item.Key,
                Kind = item.Kind == PublicationPolicyMutationKind.Remove
                    ? HumanSettingsMutationKind.Remove : HumanSettingsMutationKind.SetValue,
                Value = item.JsonValue,
                IsLocked = null
            });
            if (item.IsLocked.HasValue)
                effects.Add(anchor with
                {
                    Key = item.Key,
                    Kind = HumanSettingsMutationKind.SetLock,
                    Value = null,
                    IsLocked = item.IsLocked
                });
        }
        return await _preparation.ExecuteCurrentJoinedAsync(effects.ToImmutable(), async (proof, token) =>
        {
            var previous = CurrentWrite.Value;
            CurrentWrite.Value = new(tenant, mutations, actor, occurred, _preparation, proof, anchor);
            try { return await write(); }
            finally { CurrentWrite.Value = previous; }
        }, cancellationToken);
    }

    public static async Task RequireCurrentWriteAsync(Guid? tenant,
        ImmutableArray<PublicationPolicySettingMutation> mutations, Guid? actor, DateTime occurred,
        CancellationToken token)
    {
        var admission = CurrentWrite.Value;
        if (admission is null || admission.Tenant != tenant || admission.Actor != actor
            || admission.Occurred != occurred || !admission.Mutations.SequenceEqual(mutations))
            throw new UnauthorizedAccessException("Publication storage requires the original compiled policy effects.");
        await admission.Preparation.RequireOwnedAsync(admission.Proof, admission.Anchor, token);
    }

    private sealed record WriteAdmission(Guid? Tenant, ImmutableArray<PublicationPolicySettingMutation> Mutations,
        Guid Actor, DateTime Occurred, IHumanSettingsMutationPreparation Preparation,
        IPreparedHumanSettingsMutation Proof, HumanSettingsMutation Anchor);

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
            .OrderBy(mutation => PolicyOrder(mutation.Key))
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

    private static int PolicyOrder(string key)
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
