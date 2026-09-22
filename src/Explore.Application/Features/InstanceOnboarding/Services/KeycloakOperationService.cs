using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain.Keycloak;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public sealed class KeycloakOperationApplyContext
{
    public KeycloakOperationApplyContext(
        Guid operationId,
        string actor,
        long setupGeneration,
        KeycloakTarget target,
        string digest,
        string? apiClientId,
        string administratorUsername,
        string administratorPassword,
        DateTimeOffset nowUtc)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Operation identity is required.",
                nameof(operationId));
        }

        OperationId = operationId;
        Actor = Required(actor, nameof(actor));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(setupGeneration);
        SetupGeneration = setupGeneration;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Digest = Required(digest, nameof(digest));
        ApiClientId = apiClientId;
        AdministratorUsername = Required(
            administratorUsername,
            nameof(administratorUsername));
        AdministratorPassword = Required(
            administratorPassword,
            nameof(administratorPassword));
        NowUtc = nowUtc;
    }

    public Guid OperationId { get; }
    public string Actor { get; }
    public long SetupGeneration { get; }
    public KeycloakTarget Target { get; }
    public string Digest { get; }
    public string? ApiClientId { get; }
    public string AdministratorUsername { get; }
    public string AdministratorPassword { get; }
    public DateTimeOffset NowUtc { get; }

    public override string ToString() =>
        $"{nameof(KeycloakOperationApplyContext)} "
        + $"{{ OperationId = {OperationId:D} }}";

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        return value.Trim();
    }
}

public sealed class KeycloakOperationService
{
    private readonly KeycloakOperationPolicy _policy = new();
    private readonly IKeycloakOperationRepository? _repository;
    private readonly IKeycloakOperationCoordinator? _coordinator;
    private readonly IKeycloakAdminOperationClient? _adminClient;

    public KeycloakOperationService()
    {
    }

    public KeycloakOperationService(
        IKeycloakOperationRepository repository,
        IKeycloakOperationCoordinator coordinator,
        IKeycloakAdminOperationClient adminClient)
    {
        _repository = repository;
        _coordinator = coordinator;
        _adminClient = adminClient;
    }

    public KeycloakChangeSet? Plan(KeycloakInspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_policy.HasConflictingClientIds(snapshot))
        {
            throw new InvalidOperationException(
                "keycloak_client_id_collision");
        }

        var steps = new List<KeycloakChangeStep>();
        foreach (KeycloakMapperSemantic semantic
                 in _policy.GetRequiredMapperRepairs(snapshot))
        {
            if (_policy.HasConflictingMapper(snapshot, semantic))
            {
                throw new InvalidOperationException(
                    "keycloak_mapper_collision");
            }

            KeycloakEffectiveMapperSnapshot[] directCandidates =
                snapshot.EffectiveMappers
                    .Where(mapper =>
                        mapper.Semantic == semantic
                        && mapper.Origin == KeycloakMapperOrigin.Direct
                        && (semantic != KeycloakMapperSemantic.Audience
                            || string.Equals(
                                mapper.Audience,
                                snapshot.ApiClientId,
                                StringComparison.Ordinal)))
                    .ToArray();
            if (directCandidates.Length > 1)
            {
                throw new InvalidOperationException(
                    "keycloak_mapper_ambiguous");
            }

            KeycloakEffectiveMapperSnapshot? existing =
                directCandidates.SingleOrDefault();
            string mapperName = MapperName(snapshot, semantic);
            steps.Add(new KeycloakChangeStep(
                stepId: $"mapper:{semantic.ToString().ToLowerInvariant()}",
                kind: existing is null
                    ? KeycloakStep.CreateMapper
                    : KeycloakStep.UpdateMapper,
                resourceKind: KeycloakResourceKind.ProtocolMapper,
                targetId: existing?.ProviderId
                    ?? $"{snapshot.BlazorClientId}:{mapperName}",
                precondition: existing is null
                    ? KeycloakStepPrecondition.MustBeAbsent
                    : KeycloakStepPrecondition.MustMatchFingerprint,
                expectedFingerprint: existing is null
                    ? null
                    : MapperFingerprint(existing),
                desiredFingerprint: DesiredMapperFingerprint(
                    snapshot,
                    semantic),
                bindingFingerprint: BindingFingerprint(snapshot)));
        }

        return steps.Count == 0 ? null : new KeycloakChangeSet(steps);
    }

    public async Task<KeycloakOperation?> CreateAsync(
        Guid instanceId,
        Uri authority,
        KeycloakInspectionSnapshot snapshot,
        string actor,
        long setupGeneration,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        IKeycloakOperationRepository repository = RequireRepository();
        KeycloakChangeSet? changeSet = Plan(snapshot);
        if (changeSet is null)
        {
            return null;
        }

        var operation = new KeycloakOperation(
            changeSet,
            new KeycloakTarget(
                instanceId,
                authority.AbsoluteUri,
                snapshot.Realm,
                snapshot.BlazorClientId),
            actor,
            setupGeneration,
            ComputeDigest(changeSet),
            createdAtUtc,
            expiresAtUtc);
        await repository.AddAsync(operation, cancellationToken);
        return operation;
    }

    public async Task<KeycloakOperation> ApplyAsync(
        KeycloakOperationApplyContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IKeycloakOperationRepository repository = RequireRepository();
        IKeycloakOperationCoordinator coordinator = RequireCoordinator();
        KeycloakOperation operation =
            await repository.GetAsync(
                context.OperationId,
                cancellationToken)
            ?? throw new NotFoundException(
                nameof(KeycloakOperation),
                context.OperationId);
        Guid expectedStamp = operation.ConcurrencyStamp;
        EnsureContextBinding(context, operation);
        try
        {
            operation.AuthorizeApply(
                context.Actor,
                context.SetupGeneration,
                context.Target,
                context.Digest,
                context.NowUtc);
        }
        catch (InvalidOperationException exception)
        {
            if (operation.State == KeycloakOperationState.Expired
                && operation.ConcurrencyStamp != expectedStamp)
            {
                await repository.SaveAsync(
                    operation,
                    expectedStamp,
                    cancellationToken);
            }

            throw new KeycloakOperationConflictException(
                "The Keycloak operation approval is no longer valid.",
                exception);
        }

        // Intent must be durable before the provider adapter is invoked.
        await repository.SaveAsync(
            operation,
            expectedStamp,
            cancellationToken);

        try
        {
            return await coordinator.ExecuteAsync(
                operation,
                token => ExecuteStepsAsync(operation.Id, context, token),
                cancellationToken);
        }
        catch (KeycloakOperationOverlapException)
        {
            KeycloakOperation blocked =
                (await repository.GetAsync(
                    operation.Id,
                    cancellationToken))!;
            await SettleRemainingStepsAsync(
                blocked,
                currentStepId: null,
                KeycloakStepOutcomeKind.FailedBeforeWrite,
                context.NowUtc,
                cancellationToken);
            return (await repository.GetAsync(
                operation.Id,
                cancellationToken))!;
        }
    }

    public async Task<KeycloakOperation> ReconcileAsync(
        KeycloakOperationApplyContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IKeycloakOperationRepository repository = RequireRepository();
        IKeycloakAdminOperationClient adminClient = RequireAdminClient();
        KeycloakOperation operation =
            await repository.GetAsync(
                context.OperationId,
                cancellationToken)
            ?? throw new NotFoundException(
                nameof(KeycloakOperation),
                context.OperationId);
        if (operation.State is not (
                KeycloakOperationState.Applying
                or KeycloakOperationState.OutcomeUnknown))
        {
            return operation;
        }
        EnsureContextBinding(context, operation);

        foreach (KeycloakChangeStep step in operation.ChangeSet.Steps)
        {
            KeycloakStepOutcome? existing = operation.StepOutcomes.Items
                .SingleOrDefault(outcome => string.Equals(
                    outcome.StepId,
                    step.StepId,
                    StringComparison.Ordinal));
            if (existing is not null
                && existing.Kind != KeycloakStepOutcomeKind.OutcomeUnknown)
            {
                continue;
            }

            KeycloakMapperOperationResult inspection =
                await adminClient.InspectApprovedMapperAsync(
                    BuildMapperRequest(context, operation, step),
                    cancellationToken);
            var outcome = ToOutcome(step, inspection);
            Guid expectedStamp = operation.ConcurrencyStamp;
            if (existing is null)
            {
                operation.RecordStepOutcome(outcome);
            }
            else
            {
                operation.ReconcileStepOutcome(outcome);
            }

            await repository.SaveAsync(
                operation,
                expectedStamp,
                cancellationToken);
        }

        Guid settlementStamp = operation.ConcurrencyStamp;
        operation.SettleFromStepOutcomes(context.NowUtc);
        await repository.SaveAsync(
            operation,
            settlementStamp,
            cancellationToken);
        return operation;
    }

    private async Task<KeycloakOperation> ExecuteStepsAsync(
        Guid operationId,
        KeycloakOperationApplyContext context,
        CancellationToken cancellationToken)
    {
        IKeycloakOperationRepository repository = RequireRepository();
        IKeycloakAdminOperationClient adminClient = RequireAdminClient();
        KeycloakOperation operation =
            (await repository.GetAsync(operationId, cancellationToken))!;

        foreach (KeycloakChangeStep step in operation.ChangeSet.Steps)
        {
            operation = (await repository.GetAsync(
                operationId,
                cancellationToken))!;
            if (operation.IsCancellationRequested)
            {
                await SettleRemainingStepsAsync(
                    operation,
                    currentStepId: null,
                    KeycloakStepOutcomeKind.SkippedCancelled,
                    context.NowUtc,
                    cancellationToken);
                return (await repository.GetAsync(
                    operationId,
                    cancellationToken))!;
            }

            KeycloakMapperOperationResult result =
                await adminClient.ApplyApprovedMapperAsync(
                    BuildMapperRequest(context, operation, step),
                    cancellationToken);

            // Refresh after remote I/O so cancellation/concurrent state is observed.
            operation = (await repository.GetAsync(
                operationId,
                cancellationToken))!;
            Guid expectedStamp = operation.ConcurrencyStamp;
            operation.RecordStepOutcome(ToOutcome(step, result));
            if (result.Outcome == KeycloakStepOutcomeKind.OutcomeUnknown)
            {
                operation.MarkOutcomeUnknown();
                await repository.SaveAsync(
                    operation,
                    expectedStamp,
                    cancellationToken);
                return operation;
            }

            await repository.SaveAsync(
                operation,
                expectedStamp,
                cancellationToken);
            if (result.Outcome is KeycloakStepOutcomeKind.Conflict
                or KeycloakStepOutcomeKind.FailedBeforeWrite)
            {
                await SettleRemainingStepsAsync(
                    operation,
                    step.StepId,
                    KeycloakStepOutcomeKind.FailedBeforeWrite,
                    context.NowUtc,
                    cancellationToken);
                return (await repository.GetAsync(
                    operationId,
                    cancellationToken))!;
            }
        }

        operation = (await repository.GetAsync(
            operationId,
            cancellationToken))!;
        Guid settlementStamp = operation.ConcurrencyStamp;
        operation.SettleFromStepOutcomes(context.NowUtc);
        await repository.SaveAsync(
            operation,
            settlementStamp,
            cancellationToken);
        return operation;
    }

    private async Task SettleRemainingStepsAsync(
        KeycloakOperation operation,
        string? currentStepId,
        KeycloakStepOutcomeKind outcomeKind,
        DateTimeOffset settledAtUtc,
        CancellationToken cancellationToken)
    {
        IKeycloakOperationRepository repository = RequireRepository();
        Guid expectedStamp = operation.ConcurrencyStamp;
        HashSet<string> recorded = operation.StepOutcomes.Items
            .Select(outcome => outcome.StepId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (KeycloakChangeStep step in operation.ChangeSet.Steps)
        {
            if (recorded.Contains(step.StepId)
                || string.Equals(
                    step.StepId,
                    currentStepId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            operation.RecordStepOutcome(new KeycloakStepOutcome(
                step.StepId,
                outcomeKind));
        }

        operation.SettleFromStepOutcomes(settledAtUtc);
        await repository.SaveAsync(
            operation,
            expectedStamp,
            cancellationToken);
    }

    private static KeycloakMapperOperationRequest BuildMapperRequest(
        KeycloakOperationApplyContext context,
        KeycloakOperation operation,
        KeycloakChangeStep step)
    {
        KeycloakMapperSemantic semantic = StepSemantic(step);
        var snapshot = new KeycloakInspectionSnapshot(
            operation.Target.Realm,
            operation.Target.Client,
            context.ApiClientId,
            realmExists: true);
        return new KeycloakMapperOperationRequest(
            new Uri(operation.Target.Authority, UriKind.Absolute),
            operation.Target.Realm,
            operation.Target.Client,
            context.ApiClientId,
            MapperName(snapshot, semantic),
            semantic,
            step,
            context.AdministratorUsername,
            context.AdministratorPassword);
    }

    private static KeycloakStepOutcome ToOutcome(
        KeycloakChangeStep step,
        KeycloakMapperOperationResult result) =>
        new(
            step.StepId,
            result.Outcome,
            result.ProviderResourceId,
            result.ObservedFingerprint);

    private static void EnsureContextBinding(
        KeycloakOperationApplyContext context,
        KeycloakOperation operation)
    {
        if (!string.Equals(
                context.Actor.Trim(),
                operation.Actor,
                StringComparison.Ordinal)
            || context.SetupGeneration != operation.SetupGeneration
            || context.Target != operation.Target
            || !string.Equals(
                context.Digest.Trim(),
                operation.Digest,
                StringComparison.Ordinal))
        {
            throw new KeycloakOperationConflictException(
                "The reconciliation authority no longer matches the receipt.");
        }

        var currentProjection = new KeycloakInspectionSnapshot(
            operation.Target.Realm,
            operation.Target.Client,
            context.ApiClientId,
            realmExists: true);
        if (operation.ChangeSet.Steps.Any(step =>
                !string.Equals(
                    step.BindingFingerprint,
                    BindingFingerprint(currentProjection),
                    StringComparison.Ordinal)))
        {
            throw new KeycloakOperationConflictException(
                "The current Keycloak target no longer matches the reviewed approval.");
        }
    }

    private static KeycloakMapperSemantic StepSemantic(
        KeycloakChangeStep step) =>
        step.StepId switch
        {
            "mapper:subject" => KeycloakMapperSemantic.Subject,
            "mapper:audience" => KeycloakMapperSemantic.Audience,
            _ => throw new KeycloakOperationConflictException(
                "The approved mapper semantic is not supported.")
        };

    private IKeycloakOperationRepository RequireRepository() =>
        _repository
        ?? throw new InvalidOperationException(
            "Execution services are not configured.");

    private IKeycloakOperationCoordinator RequireCoordinator() =>
        _coordinator
        ?? throw new InvalidOperationException(
            "Execution services are not configured.");

    private IKeycloakAdminOperationClient RequireAdminClient() =>
        _adminClient
        ?? throw new InvalidOperationException(
            "Execution services are not configured.");

    public static string MapperName(
        KeycloakInspectionSnapshot snapshot,
        KeycloakMapperSemantic semantic) =>
        semantic == KeycloakMapperSemantic.Subject
            ? "subject"
            : $"{snapshot.ApiClientId}-audience";

    public static string MapperFingerprint(
        KeycloakEffectiveMapperSnapshot mapper) =>
        Hash(
            $"{mapper.Semantic}|{mapper.Audience}|"
            + $"{mapper.AddsToAccessToken}|{mapper.AddsToIdToken}");

    public static string DesiredMapperFingerprint(
        KeycloakInspectionSnapshot snapshot,
        KeycloakMapperSemantic semantic) =>
        Hash(
            semantic == KeycloakMapperSemantic.Subject
                ? $"{semantic}||True|True"
                : $"{semantic}|{snapshot.ApiClientId}|True|False");

    public static string BindingFingerprint(
        KeycloakInspectionSnapshot snapshot) =>
        Hash(
            $"{snapshot.Realm}|{snapshot.BlazorClientId}|"
            + $"{snapshot.ApiClientId}");

    public static string ComputeDigest(KeycloakChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        string projection = string.Join(
            '\n',
            changeSet.Steps.Select(step =>
                $"{step.StepId}|{step.Kind}|{step.ResourceKind}|"
                + $"{step.TargetId}|{step.Precondition}|"
                + $"{step.ExpectedFingerprint}|{step.DesiredFingerprint}|"
                + $"{step.BindingFingerprint}"));
        return Hash(projection);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
