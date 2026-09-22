using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.DTOs.Onboarding;
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
        Uri? publicOrigin,
        string? credentialBindingRevision,
        string? runtimeClientSecret,
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
        PublicOrigin = publicOrigin;
        CredentialBindingRevision = credentialBindingRevision;
        RuntimeClientSecret = runtimeClientSecret;
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
    public Uri? PublicOrigin { get; }
    public string? CredentialBindingRevision { get; }
    public string? RuntimeClientSecret { get; }
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
    private readonly IKeycloakAdminClient? _adminClient;

    public KeycloakOperationService()
    {
    }

    public KeycloakOperationService(
        IKeycloakOperationRepository repository,
        IKeycloakOperationCoordinator coordinator,
        IKeycloakAdminClient adminClient)
    {
        _repository = repository;
        _coordinator = coordinator;
        _adminClient = adminClient;
    }

    public KeycloakChangeSet? Plan(KeycloakInspectionSnapshot snapshot) =>
        Plan(snapshot, KeycloakOperationIntent.RepairClient, publicOrigin: null);

    public KeycloakChangeSet? Plan(
        KeycloakInspectionSnapshot snapshot,
        KeycloakOperationIntent intent,
        Uri? publicOrigin,
        string? credentialBindingRevision = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(intent) || _policy.HasConflictingClientIds(snapshot))
        {
            throw new InvalidOperationException("keycloak_client_id_collision");
        }

        var steps = new List<KeycloakChangeStep>();
        bool provisioning = intent is not KeycloakOperationIntent.RepairClient;
        Uri? approvedPublicOrigin = provisioning
            ? RequirePublicOrigin(publicOrigin)
            : null;
        if (provisioning
            && string.IsNullOrWhiteSpace(credentialBindingRevision))
        {
            throw new InvalidOperationException(
                "keycloak_credential_binding_revision_unavailable");
        }

        switch (intent)
        {
            case KeycloakOperationIntent.CreateRealm:
                if (snapshot.RealmExists)
                {
                    throw new InvalidOperationException("keycloak_realm_exists");
                }
                RequireAbsentClients(snapshot);
                steps.Add(CreateRealmStep(
                    snapshot,
                    approvedPublicOrigin!,
                    credentialBindingRevision!));
                AddCreateClientSteps(
                    steps,
                    snapshot,
                    approvedPublicOrigin!,
                    credentialBindingRevision!);
                break;
            case KeycloakOperationIntent.CreateClients:
                if (!snapshot.RealmExists)
                {
                    throw new InvalidOperationException("keycloak_realm_absent");
                }
                RequireAbsentClients(snapshot);
                AddCreateClientSteps(
                    steps,
                    snapshot,
                    approvedPublicOrigin!,
                    credentialBindingRevision!);
                break;
            case KeycloakOperationIntent.RepairClient:
                if (!snapshot.RealmExists || !snapshot.BlazorClient.IsUnambiguous)
                {
                    throw new InvalidOperationException("keycloak_client_absent_or_ambiguous");
                }
                KeycloakClientShape shape = snapshot.BlazorClient.Shape
                    ?? throw new InvalidOperationException("keycloak_client_shape_unknown");
                if (shape.BearerOnly || shape.PublicClient || !shape.StandardFlowEnabled)
                {
                    throw new InvalidOperationException("keycloak_client_incompatible");
                }
                break;
        }

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
                expectedIdentityFingerprint: existing is null
                    ? null
                    : MapperIdentityFingerprint(existing),
                desiredFingerprint: DesiredMapperFingerprint(
                    snapshot,
                    semantic),
                bindingFingerprint: BindingFingerprint(
                    snapshot,
                    approvedPublicOrigin,
                    provisioning ? credentialBindingRevision : null),
                desired: KeycloakDesiredProjection.Mapper(
                    mapperName,
                    semantic,
                    semantic == KeycloakMapperSemantic.Audience
                        ? snapshot.ApiClientId
                        : null)));
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
        KeycloakOperationIntent intent = KeycloakOperationIntent.RepairClient,
        Uri? publicOrigin = null,
        string? credentialBindingRevision = null,
        CancellationToken cancellationToken = default)
    {
        IKeycloakOperationRepository repository = RequireRepository();
        KeycloakChangeSet? changeSet = Plan(
            snapshot,
            intent,
            publicOrigin,
            credentialBindingRevision);
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
        IKeycloakAdminClient adminClient = RequireAdminClient();
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

            KeycloakStepOutcome outcome;
            if (IsMapperStep(step))
            {
                KeycloakMapperOperationResult inspection =
                    await adminClient.InspectApprovedMapperAsync(
                        BuildMapperRequest(context, operation, step),
                        cancellationToken);
                outcome = ToOutcome(step, inspection);
            }
            else
            {
                KeycloakProvisioningOperationResult inspection =
                    await adminClient.InspectApprovedProvisioningAsync(
                        BuildProvisioningRequest(
                            context,
                            operation,
                            step,
                            existing?.ProviderResourceId),
                        cancellationToken);
                outcome = ToOutcome(step, inspection);
            }
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
        IKeycloakAdminClient adminClient = RequireAdminClient();
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

            KeycloakStepOutcome result;
            if (IsMapperStep(step))
            {
                result = ToOutcome(
                    step,
                    await adminClient.ApplyApprovedMapperAsync(
                        BuildMapperRequest(context, operation, step),
                        cancellationToken));
            }
            else
            {
                result = ToOutcome(
                    step,
                    await adminClient.ApplyApprovedProvisioningAsync(
                        BuildProvisioningRequest(
                            context,
                            operation,
                            step,
                            providerResourceId: null),
                        cancellationToken));
            }

            // Refresh after remote I/O so cancellation/concurrent state is observed.
            operation = (await repository.GetAsync(
                operationId,
                cancellationToken))!;
            Guid expectedStamp = operation.ConcurrencyStamp;
            operation.RecordStepOutcome(result);
            if (result.Kind == KeycloakStepOutcomeKind.OutcomeUnknown)
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
            if (result.Kind is KeycloakStepOutcomeKind.Conflict
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

    private static KeycloakProvisioningOperationRequest
        BuildProvisioningRequest(
            KeycloakOperationApplyContext context,
            KeycloakOperation operation,
            KeycloakChangeStep step,
            string? providerResourceId) =>
        new(
            new Uri(operation.Target.Authority, UriKind.Absolute),
            operation.Target.Realm,
            operation.Target.Client,
            context.ApiClientId,
            step,
            context.RuntimeClientSecret,
            context.AdministratorUsername,
            context.AdministratorPassword,
            providerResourceId);

    private static bool IsMapperStep(KeycloakChangeStep step) =>
        step.Kind is KeycloakStep.CreateMapper
            or KeycloakStep.UpdateMapper;

    private static KeycloakStepOutcome ToOutcome(
        KeycloakChangeStep step,
        KeycloakProvisioningOperationResult result) =>
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
        bool provisioning = operation.ChangeSet.Steps.Any(step =>
            step.Kind is KeycloakStep.CreateRealm
                or KeycloakStep.CreateClient);
        string currentBindingFingerprint = BindingFingerprint(
            currentProjection,
            provisioning ? context.PublicOrigin : null,
            provisioning ? context.CredentialBindingRevision : null);
        if (operation.ChangeSet.Steps.Any(step =>
                !string.Equals(
                    step.BindingFingerprint,
                    currentBindingFingerprint,
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

    private IKeycloakAdminClient RequireAdminClient() =>
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
            $"{mapper.ProviderId}|{mapper.Name}|{mapper.Protocol}|"
            + $"{mapper.MapperType}|{mapper.ClaimName}|"
            + $"{mapper.Semantic}|{mapper.Audience}|"
            + $"{mapper.AddsToAccessToken}|{mapper.AddsToIdToken}");

    public static string MapperSemanticFingerprint(
        KeycloakEffectiveMapperSnapshot mapper) =>
        Hash(
            $"{mapper.Semantic}|{mapper.Audience}|"
            + $"{mapper.AddsToAccessToken}|{mapper.AddsToIdToken}");

    public static string MapperIdentityFingerprint(
        KeycloakEffectiveMapperSnapshot mapper) =>
        Hash(
            $"{mapper.ProviderId}|{mapper.Name}|{mapper.Protocol}|"
            + $"{mapper.MapperType}|{mapper.ClaimName}");

    public static string DesiredMapperFingerprint(
        KeycloakInspectionSnapshot snapshot,
        KeycloakMapperSemantic semantic) =>
        Hash(
            semantic == KeycloakMapperSemantic.Subject
                ? $"{semantic}||True|True"
                : $"{semantic}|{snapshot.ApiClientId}|True|False");

    public static string BindingFingerprint(
        KeycloakInspectionSnapshot snapshot,
        Uri? publicOrigin = null,
        string? credentialBindingRevision = null) =>
        Hash(
            $"{snapshot.Realm}|{snapshot.BlazorClientId}|"
            + $"{snapshot.ApiClientId}|{publicOrigin?.AbsoluteUri}|"
            + $"{credentialBindingRevision}");

    private static void RequireAbsentClients(KeycloakInspectionSnapshot snapshot)
    {
        if (!snapshot.BlazorClient.IsProvenAbsent
            || (snapshot.ApiClient is not null && !snapshot.ApiClient.IsProvenAbsent))
        {
            throw new InvalidOperationException("keycloak_client_exists_or_ambiguous");
        }
    }

    private static Uri RequirePublicOrigin(Uri? origin)
    {
        if (origin is null
            || !origin.IsAbsoluteUri
            || origin.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new InvalidOperationException("keycloak_public_origin_invalid");
        }

        return new Uri(origin.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static KeycloakChangeStep CreateRealmStep(
        KeycloakInspectionSnapshot snapshot,
        Uri publicOrigin,
        string credentialBindingRevision)
    {
        var desired = KeycloakDesiredProjection.Realm(
            snapshot.Realm,
            Guid.CreateVersion7().ToString("D"));
        return new KeycloakChangeStep(
            "realm:create",
            KeycloakStep.CreateRealm,
            KeycloakResourceKind.Realm,
            snapshot.Realm,
            KeycloakStepPrecondition.MustBeAbsent,
            null,
            null,
            DesiredProvisioningFingerprint(desired),
            BindingFingerprint(
                snapshot,
                publicOrigin,
                credentialBindingRevision),
            desired);
    }

    private static void AddCreateClientSteps(
        ICollection<KeycloakChangeStep> steps,
        KeycloakInspectionSnapshot snapshot,
        Uri publicOrigin,
        string credentialBindingRevision)
    {
        string origin = publicOrigin.GetLeftPart(UriPartial.Authority);
        var bff = KeycloakDesiredProjection.ConfidentialClient(
            snapshot.BlazorClientId,
            [new Uri(publicOrigin, "signin-oidc").AbsoluteUri],
            [origin]);
        steps.Add(new KeycloakChangeStep(
            "client:bff", KeycloakStep.CreateClient, KeycloakResourceKind.Client,
            snapshot.BlazorClientId, KeycloakStepPrecondition.MustBeAbsent,
            null, null, DesiredProvisioningFingerprint(bff),
            BindingFingerprint(
                snapshot,
                publicOrigin,
                credentialBindingRevision),
            bff));
        if (!string.IsNullOrWhiteSpace(snapshot.ApiClientId))
        {
            KeycloakDesiredProjection api = KeycloakDesiredProjection.BearerOnlyClient(snapshot.ApiClientId);
            steps.Add(new KeycloakChangeStep(
                "client:api", KeycloakStep.CreateClient, KeycloakResourceKind.Client,
                snapshot.ApiClientId, KeycloakStepPrecondition.MustBeAbsent,
                null, null, DesiredProvisioningFingerprint(api),
                BindingFingerprint(
                    snapshot,
                    publicOrigin,
                    credentialBindingRevision),
                api));
        }
    }

    public static string ComputeDigest(KeycloakChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        string projection = string.Join(
            '\n',
            changeSet.Steps.Select(step =>
                $"{step.StepId}|{step.Kind}|{step.ResourceKind}|"
                + $"{step.TargetId}|{step.Precondition}|"
                + $"{step.ExpectedFingerprint}|"
                + $"{step.ExpectedIdentityFingerprint}|"
                + $"{step.DesiredFingerprint}|"
                + $"{step.BindingFingerprint}|"
                + $"{DesiredProjectionValue(step.Desired)}"));
        return Hash(projection);
    }

    public static string DesiredProvisioningFingerprint(
        KeycloakDesiredProjection desired) =>
        Hash(DesiredProjectionValue(desired));

    private static string DesiredProjectionValue(
        KeycloakDesiredProjection? desired) =>
        desired is null
            ? string.Empty
            : $"{desired.Kind}|{desired.ResourceName}|"
              + $"{string.Join(',', desired.RedirectUris)}|"
              + $"{string.Join(',', desired.WebOrigins)}|"
              + $"{desired.Audience}|"
              + $"{desired.ProviderResourceId}";

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
