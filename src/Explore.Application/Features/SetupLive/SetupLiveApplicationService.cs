namespace Explore.Application.Features.SetupLive;

using System.Security.Cryptography;
using System.Text;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.SetupLive;
using ISLAMU.Wire.Contracts.SetupLive;
using Microsoft.Extensions.Logging;
using DomainEnrollmentState = Explore.Domain.SetupLive.SetupEnrollmentState;
using WireEnrollmentState = ISLAMU.Wire.Contracts.SetupLive.SetupEnrollmentState;
using DomainOperationOutcome = Explore.Domain.SetupLive.SetupSecretBindingOperationOutcome;
using DomainOperationState = Explore.Domain.SetupLive.SetupSecretBindingOperationState;
using WireOperationOutcome = ISLAMU.Wire.Contracts.SetupLive.SetupSecretBindingOperationOutcome;
using WireOperationState = ISLAMU.Wire.Contracts.SetupLive.SetupSecretBindingOperationState;

public enum SetupLiveApplicationStatus
{
    Invalid = 0,
    Success = 1,
    Created = 2,
    Duplicate = 3,
    Forbidden = 4,
    Unavailable = 5,
    Conflict = 6
}

public sealed record SetupLiveEnrollmentResult(
    SetupLiveApplicationStatus Status,
    SetupTargetEnrollmentData? Data = null,
    SetupEnrollmentCapability? Capability = null,
    bool CanMutate = false);

public sealed record SetupLiveReadinessResult(
    SetupLiveApplicationStatus Status,
    IReadOnlyList<SetupSecretBindingReadinessItem>? Items = null,
    bool CanWrite = false);

public sealed record SetupLiveSecretBindingResult(
    SetupLiveApplicationStatus Status,
    SetupSecretBindingOperationData? Data = null);

public sealed class SetupLiveApplicationService(
    ISetupLiveRepository setupLiveRepository,
    ISecretBindingRepository secretBindingRepository,
    IUnitOfWork unitOfWork,
    IActorRepository actorRepository,
    IAuthorizationProvider authorization,
    ITenantContext tenantContext,
    ISetupSecretBindingWriter secretBindingWriter,
    ISetupSecretBindingReadinessReader secretBindingReadiness,
    ISetupSecretBindingCommitmentAuthority commitmentAuthority,
    ISetupSecretBindingOperationCoordinator operationCoordinator,
    ISetupSecretBindingCommitBarrier commitBarrier,
    TimeProvider timeProvider,
    ILogger<SetupLiveApplicationService> logger)
{
    private static readonly EventId MilestoneEvent = new(19_620, "SetupLiveMilestone");
    private static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes(15);

    public async Task<SetupLiveSecretBindingResult> WriteSecretBindingAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capability,
        Guid operationKey,
        string bindingKey,
        ReadOnlyMemory<byte> secretValue,
        CancellationToken cancellationToken)
    {
        if (!IsVersion7(operationKey)
            || secretValue.Length is < 1 or > 65_536
            || !IsSupportedBindingKey(bindingKey))
        {
            return new(SetupLiveApplicationStatus.Unavailable);
        }

        AuthorizedEnrollment? authorized = await AuthorizeCurrentEnrollmentAsync(
            tenantId,
            enrollmentId,
            userId,
            capability,
            AuthorizationActions.Tenants.Update,
            SetupEnrollmentScope.SecretBindingWrite,
            cancellationToken);
        if (authorized is null)
            return new(SetupLiveApplicationStatus.Unavailable);

        SecretBinding? binding = await secretBindingRepository.GetByKeyAndScopeAsync(
            bindingKey,
            SecretScope.Instance,
            scopeId: null,
            cancellationToken);
        if (binding is null || !IsVersion7(binding.Id))
            return new(SetupLiveApplicationStatus.Unavailable);

        SetupSecretBindingCommitment commitment;
        try
        {
            commitment = await commitmentAuthority.CommitAsync(
                new SetupSecretBindingCommitmentRequest(
                    tenantId,
                    authorized.Enrollment.ActorId,
                    enrollmentId,
                    authorized.Enrollment.Generation,
                    operationKey,
                    bindingKey,
                    secretValue),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return new(SetupLiveApplicationStatus.Unavailable);
        }

        string requestFingerprint = Digest(
            $"setup-live-secret-binding-v1\n{tenantId:D}\n{authorized.Enrollment.ActorId:D}\n{enrollmentId:D}\n{authorized.Enrollment.Generation}\n{operationKey:D}\n{binding.Id:D}\n{bindingKey}");
        Guid operationId = Guid.CreateVersion7();
        DateTime createdAt = UtcNow();
        var coordination = new SetupSecretBindingCoordinationRequest(
            tenantId,
            enrollmentId,
            authorized.Enrollment.Generation);
        await using IAsyncDisposable lease = await operationCoordinator.AcquireAsync(
            coordination,
            cancellationToken);

        SecretWriteAdmission admission = await unitOfWork.ExecuteSerializableAsync(
            async token =>
            {
                AuthorizedEnrollment? current = await AuthorizeEnrollmentAsync(
                    tenantId,
                    enrollmentId,
                    userId,
                    capability,
                    AuthorizationActions.Tenants.Update,
                    SetupEnrollmentScope.SecretBindingWrite,
                    token);
                if (current is null
                    || current.Enrollment.Generation
                        != authorized.Enrollment.Generation)
                {
                    return new SecretWriteAdmission(
                        SetupLiveApplicationStatus.Unavailable);
                }

                SetupSecretBindingOperation? existing =
                    await setupLiveRepository.FindOperationAsync(
                        tenantId,
                        operationKey,
                        token);
                if (existing is not null)
                {
                    if (existing.Match(
                            tenantId,
                            current.Enrollment.ActorId,
                            enrollmentId,
                            current.Enrollment.Generation,
                            operationKey,
                            bindingKey,
                            requestFingerprint,
                            commitment.KeyVersion,
                            commitment.Commitment)
                        == SetupReplayDecision.Conflict)
                    {
                        return new SecretWriteAdmission(
                            SetupLiveApplicationStatus.Conflict);
                    }

                    return new SecretWriteAdmission(
                        SetupLiveApplicationStatus.Duplicate,
                        existing);
                }

                var operation = SetupSecretBindingOperation.CreateAccepted(
                    operationId,
                    tenantId,
                    current.Enrollment.ActorId,
                    enrollmentId,
                    current.Enrollment.Generation,
                    operationKey,
                    bindingKey,
                    requestFingerprint,
                    commitment.KeyVersion,
                    commitment.Commitment,
                    createdAt);
                await setupLiveRepository.AddAsync(operation, token);
                await setupLiveRepository.SaveChangesAsync(token);
                return new SecretWriteAdmission(
                    SetupLiveApplicationStatus.Created,
                    operation);
            },
            cancellationToken);
        if (admission.Status == SetupLiveApplicationStatus.Duplicate)
            return new(admission.Status, MapOperation(admission.Operation!));
        if (admission.Status != SetupLiveApplicationStatus.Created)
            return new(admission.Status);

        try
        {
            await LogMilestoneAsync(
                SetupSecretBindingContractMetadata.Operation,
                SetupSecretBindingContractMetadata.BeforeProviderDispatchMilestone);
            await commitBarrier.WaitBeforeProviderDispatchAsync(cancellationToken);
            bool canDispatch = await unitOfWork.ExecuteSerializableAsync(
                async token =>
                {
                    SetupSecretBindingOperation? operation =
                        await setupLiveRepository.FindOperationAsync(
                            tenantId,
                            operationKey,
                            token);
                    SetupTargetEnrollment? enrollment =
                        await setupLiveRepository.FindCurrentEnrollmentAsync(
                            tenantId,
                            enrollmentId,
                            token);
                    if (operation is null || enrollment is null)
                        return false;
                    if (!operation.CanDispatch(enrollment, UtcNow()))
                    {
                        if (operation.State == DomainOperationState.Accepted)
                        {
                            operation.Fail(
                                DomainOperationOutcome.UnavailableEnrollment,
                                UtcNow());
                            await setupLiveRepository.SaveChangesAsync(token);
                        }
                        return false;
                    }
                    return true;
                },
                cancellationToken);
            if (!canDispatch)
                return new(SetupLiveApplicationStatus.Unavailable);

            SetupSecretBindingWriteOutcome outcome =
                await secretBindingWriter.WriteAsync(
                    new SetupSecretBindingWriteRequest(
                        tenantId,
                        enrollmentId,
                        authorized.Enrollment.Generation,
                        admission.Operation!.Id,
                        binding.Id,
                        bindingKey,
                        secretValue),
                    cancellationToken);
            SetupSecretBindingOperation settled =
                await unitOfWork.ExecuteSerializableAsync(
                    async token =>
                    {
                        SetupSecretBindingOperation operation =
                            await setupLiveRepository.FindOperationAsync(
                                tenantId,
                                operationKey,
                                token)
                            ?? throw new InvalidOperationException(
                                "Setup secret-binding operation is missing.");
                        DateTime settledAt = UtcNow();
                        if (outcome == SetupSecretBindingWriteOutcome.Ready)
                            operation.Succeed(settledAt);
                        else
                            operation.Fail(MapFailure(outcome), settledAt);
                        await setupLiveRepository.SaveChangesAsync(token);
                        return operation;
                    },
                    CancellationToken.None);
            return new(
                SetupLiveApplicationStatus.Success,
                MapOperation(settled));
        }
        catch (OperationCanceledException)
        {
            await CancelOperationAsync(tenantId, operationKey);
            throw;
        }
    }

    public async Task<SetupLiveSecretBindingResult> GetSecretBindingOperationAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid operationId,
        Guid userId,
        string? capability,
        CancellationToken cancellationToken)
    {
        if (!IsVersion7(operationId))
            return new(SetupLiveApplicationStatus.Unavailable);
        AuthorizedEnrollment? authorized = await AuthorizeCurrentEnrollmentAsync(
            tenantId,
            enrollmentId,
            userId,
            capability,
            AuthorizationActions.Tenants.View,
            SetupEnrollmentScope.SecretBindingWrite,
            cancellationToken);
        if (authorized is null)
            return new(SetupLiveApplicationStatus.Unavailable);

        await using IAsyncDisposable lease = await operationCoordinator.AcquireAsync(
            new SetupSecretBindingCoordinationRequest(
                tenantId,
                enrollmentId,
                authorized.Enrollment.Generation),
            cancellationToken);
        SetupSecretBindingOperation? operation =
            await setupLiveRepository.FindOperationByIdAsync(
                tenantId,
                operationId,
                cancellationToken);
        return operation is not null
            && operation.EnrollmentId == enrollmentId
            && operation.ActorId == authorized.Enrollment.ActorId
            && operation.EnrollmentGeneration == authorized.Enrollment.Generation
            ? new(
                SetupLiveApplicationStatus.Success,
                MapOperation(operation))
            : new(SetupLiveApplicationStatus.Unavailable);
    }

    public async Task<SetupLiveEnrollmentResult> CreateAsync(
        Guid tenantId,
        Guid userId,
        Guid operationKey,
        CreateSetupTargetEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsVersion7(tenantId)
            || tenantContext.TenantId != tenantId
            || !IsVersion7(userId)
            || !IsVersion7(operationKey))
        {
            return new(SetupLiveApplicationStatus.Invalid);
        }

        if (!await IsAuthorizedAsync(
                tenantId,
                userId,
                AuthorizationActions.Tenants.Update,
                cancellationToken))
        {
            return new(SetupLiveApplicationStatus.Forbidden);
        }

        Explore.Domain.Actor? actor = await actorRepository.GetActorByUserIdAndTenantId(
            userId,
            tenantId,
            cancellationToken);
        if (actor is null || !IsVersion7(actor.Id))
            return new(SetupLiveApplicationStatus.Forbidden);

        string challenge = request.ClientChallenge.ToWireValue();
        string scopeDigest = ScopeDigest(request.RequestedScopes);
        string requestFingerprint = Digest(
            $"setup-live-enrollment-v1\n{tenantId:D}\n{actor.Id:D}\n{challenge}\n{scopeDigest}");
        DateTime createdAt = UtcNow();
        Guid enrollmentId = Guid.CreateVersion7();
        Guid claimId = Guid.CreateVersion7();
        byte[]? capabilityBytes = null;
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(
                async token =>
                {
                    SetupEnrollmentIssuanceClaim? existingClaim =
                        await setupLiveRepository.FindIssuanceClaimAsync(
                            tenantId,
                            operationKey,
                            token);
                    if (existingClaim is not null)
                    {
                        if (existingClaim.Match(
                                tenantId,
                                actor.Id,
                                requestFingerprint)
                            == SetupReplayDecision.Conflict)
                        {
                            return new SetupLiveEnrollmentResult(
                                SetupLiveApplicationStatus.Conflict);
                        }

                        SetupTargetEnrollment? existingEnrollment =
                            await setupLiveRepository.FindEnrollmentAsync(
                                tenantId,
                                existingClaim.EnrollmentId,
                                token);
                        if (existingEnrollment is null
                            || !existingEnrollment.IsAvailable(
                                tenantId,
                                actor.Id,
                                existingClaim.EnrollmentGeneration,
                                UtcNow())
                            || !FixedTimeEquals(
                                existingEnrollment.ScopeDigest,
                                scopeDigest))
                        {
                            return new SetupLiveEnrollmentResult(
                                SetupLiveApplicationStatus.Unavailable);
                        }

                        return new SetupLiveEnrollmentResult(
                            SetupLiveApplicationStatus.Duplicate,
                            MapEnrollment(
                                existingEnrollment,
                                SetupEnrollmentIssuance.AlreadyIssued,
                                UtcNow()),
                            CanMutate: true);
                    }

                    if (!await IsAuthorizedAsync(
                            tenantId,
                            userId,
                            AuthorizationActions.Tenants.Update,
                            token))
                    {
                        return new SetupLiveEnrollmentResult(
                            SetupLiveApplicationStatus.Forbidden);
                    }

                    capabilityBytes ??= RandomNumberGenerator.GetBytes(
                        SetupEnrollmentCapability.ByteLength);
                    SetupEnrollmentCapability capability =
                        SetupEnrollmentCapability.FromBytes(capabilityBytes);
                    var enrollment = SetupTargetEnrollment.Create(
                        enrollmentId,
                        tenantId,
                        actor.Id,
                        Digest(challenge),
                        Digest(capability.ToHeaderValue()),
                        scopeDigest,
                        createdAt,
                        createdAt.Add(EnrollmentLifetime));
                    var claim = SetupEnrollmentIssuanceClaim.Create(
                        claimId,
                        tenantId,
                        actor.Id,
                        operationKey,
                        enrollment.Id,
                        enrollment.Generation,
                        requestFingerprint,
                        createdAt);

                    await setupLiveRepository.AddAsync(enrollment, token);
                    await setupLiveRepository.AddAsync(claim, token);
                    await LogMilestoneAsync("enrollment.create", "before_commit");
                    token.ThrowIfCancellationRequested();
                    await setupLiveRepository.SaveChangesAsync(token);
                    return new SetupLiveEnrollmentResult(
                        SetupLiveApplicationStatus.Created,
                        MapEnrollment(
                            enrollment,
                            SetupEnrollmentIssuance.Issued,
                            createdAt),
                        capability,
                        CanMutate: true);
                },
                cancellationToken);
        }
        finally
        {
            if (capabilityBytes is not null)
                CryptographicOperations.ZeroMemory(capabilityBytes);
        }
    }

    public async Task<SetupLiveEnrollmentResult> GetAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capability,
        CancellationToken cancellationToken)
    {
        AuthorizedEnrollment? authorized = await AuthorizeCurrentEnrollmentAsync(
            tenantId,
            enrollmentId,
            userId,
            capability,
            AuthorizationActions.Tenants.View,
            requiredScope: SetupEnrollmentScope.TargetRead,
            cancellationToken);
        if (authorized is null)
            return new(SetupLiveApplicationStatus.Unavailable);

        bool canMutate = await IsAuthorizedAsync(
            tenantId,
            userId,
            AuthorizationActions.Tenants.Update,
            cancellationToken);
        return new(
            SetupLiveApplicationStatus.Success,
            MapEnrollment(
                authorized.Enrollment,
                SetupEnrollmentIssuance.AlreadyIssued,
                UtcNow()),
            CanMutate: canMutate);
    }

    public async Task<SetupLiveEnrollmentResult> RevokeAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        Guid operationKey,
        string? capability,
        CancellationToken cancellationToken)
    {
        if (!IsVersion7(operationKey))
            return new(SetupLiveApplicationStatus.Unavailable);

        AuthorizedEnrollment? authorized = await AuthorizeCurrentEnrollmentAsync(
            tenantId,
            enrollmentId,
            userId,
            capability,
            AuthorizationActions.Tenants.Update,
            requiredScope: SetupEnrollmentScope.TargetRead,
            cancellationToken);
        if (authorized is null)
            return new(SetupLiveApplicationStatus.Unavailable);

        await using IAsyncDisposable lease = await operationCoordinator.AcquireAsync(
            new SetupSecretBindingCoordinationRequest(
                tenantId,
                enrollmentId,
                authorized.Enrollment.Generation),
            cancellationToken);

        DateTime observedAt = UtcNow();
        Guid claimId = Guid.CreateVersion7();
        string requestFingerprint = Digest(
            $"setup-live-revocation-v1\n{tenantId:D}\n{authorized.Enrollment.ActorId:D}\n{enrollmentId:D}\n{Digest(capability!)}");
        return await unitOfWork.ExecuteSerializableAsync(
            async token =>
            {
                SetupEnrollmentIssuanceClaim? existingClaim =
                    await setupLiveRepository.FindIssuanceClaimAsync(
                        tenantId,
                        operationKey,
                        token);
                if (existingClaim is not null)
                {
                    return existingClaim.Match(
                            tenantId,
                            authorized.Enrollment.ActorId,
                            requestFingerprint)
                        == SetupReplayDecision.Conflict
                        ? new SetupLiveEnrollmentResult(
                            SetupLiveApplicationStatus.Conflict)
                        : new SetupLiveEnrollmentResult(
                            SetupLiveApplicationStatus.Unavailable);
                }

                AuthorizedEnrollment? current = await AuthorizeEnrollmentAsync(
                    tenantId,
                    enrollmentId,
                    userId,
                    capability,
                    AuthorizationActions.Tenants.Update,
                    requiredScope: SetupEnrollmentScope.TargetRead,
                    token);
                if (current is null)
                    return new SetupLiveEnrollmentResult(
                        SetupLiveApplicationStatus.Unavailable);

                var claim = SetupEnrollmentIssuanceClaim.Create(
                    claimId,
                    tenantId,
                    current.Enrollment.ActorId,
                    operationKey,
                    current.Enrollment.Id,
                    current.Enrollment.Generation,
                    requestFingerprint,
                    observedAt);
                current.Enrollment.Revoke(observedAt);
                await setupLiveRepository.AddAsync(claim, token);
                await setupLiveRepository.SaveChangesAsync(token);
                return new SetupLiveEnrollmentResult(
                    SetupLiveApplicationStatus.Success,
                    MapEnrollment(
                        current.Enrollment,
                        SetupEnrollmentIssuance.AlreadyIssued,
                        observedAt));
            },
            cancellationToken);
    }

    public async Task<SetupLiveEnrollmentResult> RotateAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        Guid operationKey,
        string? capability,
        CancellationToken cancellationToken)
    {
        if (!IsVersion7(tenantId)
            || tenantContext.TenantId != tenantId
            || !IsVersion7(enrollmentId)
            || !IsVersion7(userId)
            || !IsVersion7(operationKey)
            || !SetupEnrollmentCapability.TryCreate(
                capability,
                out SetupEnrollmentCapability? currentCapability))
        {
            return new(SetupLiveApplicationStatus.Unavailable);
        }

        Explore.Domain.Actor? actor = await actorRepository.GetActorByUserIdAndTenantId(
            userId,
            tenantId,
            cancellationToken);
        if (actor is null
            || !IsVersion7(actor.Id)
            || !await IsAuthorizedAsync(
                tenantId,
                userId,
                AuthorizationActions.Tenants.Update,
                cancellationToken))
        {
            return new(SetupLiveApplicationStatus.Unavailable);
        }

        string requestFingerprint = Digest(
            $"setup-live-rotation-v1\n{tenantId:D}\n{actor.Id:D}\n{enrollmentId:D}\n{Digest(currentCapability!.ToHeaderValue())}");
        DateTime observedAt = UtcNow();
        Guid claimId = Guid.CreateVersion7();

        SetupEnrollmentIssuanceClaim? observedClaim =
            await setupLiveRepository.FindIssuanceClaimAsync(
                tenantId,
                operationKey,
                cancellationToken);
        long coordinationGeneration;
        if (observedClaim is not null)
        {
            coordinationGeneration = observedClaim.EnrollmentGeneration;
        }
        else
        {
            AuthorizedEnrollment? current = await AuthorizeCurrentEnrollmentAsync(
                tenantId,
                enrollmentId,
                userId,
                currentCapability.ToHeaderValue(),
                AuthorizationActions.Tenants.Update,
                SetupEnrollmentScope.TargetRead,
                cancellationToken);
            if (current is null)
                return new(SetupLiveApplicationStatus.Unavailable);
            coordinationGeneration = current.Enrollment.Generation;
        }

        await using IAsyncDisposable lease = await operationCoordinator.AcquireAsync(
            new SetupSecretBindingCoordinationRequest(
                tenantId,
                enrollmentId,
                coordinationGeneration),
            cancellationToken);

        byte[]? capabilityBytes = null;
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(
                async token =>
                {
                    if (!await IsAuthorizedAsync(
                            tenantId,
                            userId,
                            AuthorizationActions.Tenants.Update,
                            token))
                    {
                        return new SetupLiveEnrollmentResult(
                            SetupLiveApplicationStatus.Unavailable);
                    }

                    SetupEnrollmentIssuanceClaim? existingClaim =
                        await setupLiveRepository.FindIssuanceClaimAsync(
                            tenantId,
                            operationKey,
                            token);
                    if (existingClaim is not null)
                    {
                        if (existingClaim.Match(
                                tenantId,
                                actor.Id,
                                requestFingerprint)
                            == SetupReplayDecision.Conflict)
                        {
                            return new SetupLiveEnrollmentResult(
                                SetupLiveApplicationStatus.Conflict);
                        }

                        SetupTargetEnrollment? existingEnrollment =
                            await setupLiveRepository.FindEnrollmentAsync(
                                tenantId,
                                existingClaim.EnrollmentId,
                                token);
                        if (existingEnrollment is null
                            || !existingEnrollment.IsAvailable(
                                tenantId,
                                actor.Id,
                                existingClaim.EnrollmentGeneration,
                                UtcNow())
                            || !RecoverScopes(existingEnrollment.ScopeDigest)
                                .Contains(SetupEnrollmentScope.TargetRead))
                        {
                            return new SetupLiveEnrollmentResult(
                                SetupLiveApplicationStatus.Unavailable);
                        }

                        return new SetupLiveEnrollmentResult(
                            SetupLiveApplicationStatus.Duplicate,
                            MapEnrollment(
                                existingEnrollment,
                                SetupEnrollmentIssuance.AlreadyIssued,
                                UtcNow()),
                            CanMutate: true);
                    }

                    AuthorizedEnrollment? authorized =
                        await AuthorizeEnrollmentAsync(
                            tenantId,
                            enrollmentId,
                            userId,
                            currentCapability.ToHeaderValue(),
                            AuthorizationActions.Tenants.Update,
                            requiredScope: SetupEnrollmentScope.TargetRead,
                            token);
                    if (authorized is null)
                        return new(SetupLiveApplicationStatus.Unavailable);

                    capabilityBytes ??= RandomNumberGenerator.GetBytes(
                        SetupEnrollmentCapability.ByteLength);
                    SetupEnrollmentCapability rotated =
                        SetupEnrollmentCapability.FromBytes(capabilityBytes);
                    authorized.Enrollment.RotateCapability(
                        Digest(rotated.ToHeaderValue()),
                        authorized.Enrollment.ExpiresAt.Add(EnrollmentLifetime),
                        observedAt);
                    var claim = SetupEnrollmentIssuanceClaim.Create(
                        claimId,
                        tenantId,
                        actor.Id,
                        operationKey,
                        authorized.Enrollment.Id,
                        authorized.Enrollment.Generation,
                        requestFingerprint,
                        observedAt);
                    await setupLiveRepository.AddAsync(claim, token);
                    await setupLiveRepository.SaveChangesAsync(token);
                    return new SetupLiveEnrollmentResult(
                        SetupLiveApplicationStatus.Success,
                        MapEnrollment(
                            authorized.Enrollment,
                            SetupEnrollmentIssuance.Issued,
                            observedAt),
                        rotated,
                        CanMutate: true);
                },
                cancellationToken);
        }
        finally
        {
            if (capabilityBytes is not null)
                CryptographicOperations.ZeroMemory(capabilityBytes);
        }
    }

    public async Task<SetupLiveReadinessResult> ReadinessAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capability,
        CancellationToken cancellationToken)
    {
        AuthorizedEnrollment? authorized = await AuthorizeEnrollmentAsync(
            tenantId,
            enrollmentId,
            userId,
            capability,
            AuthorizationActions.Tenants.View,
            requiredScope: SetupEnrollmentScope.SecretBindingReadiness,
            cancellationToken);
        if (authorized is null)
            return new(SetupLiveApplicationStatus.Unavailable);

        SecretBinding? signing = await secretBindingRepository.GetByKeyAndScopeAsync(
            "setup.signing",
            SecretScope.Instance,
            scopeId: null,
            cancellationToken);
        SecretBinding? encryption = await secretBindingRepository.GetByKeyAndScopeAsync(
            "setup.encryption",
            SecretScope.Instance,
            scopeId: null,
            cancellationToken);
        SetupSecretBindingReadinessState signingState =
            await ReadinessStateAsync(signing, cancellationToken);
        SetupSecretBindingReadinessState encryptionState =
            await ReadinessStateAsync(encryption, cancellationToken);
        bool anyConfigured = signing is not null || encryption is not null;
        bool canWrite = authorized.Scopes.Contains(
                SetupEnrollmentScope.SecretBindingWrite)
            && await IsAuthorizedAsync(
                tenantId,
                userId,
                AuthorizationActions.Tenants.Update,
                cancellationToken);

        SetupSecretBindingReadinessItem[] items =
        [
            new()
            {
                BindingKey = "setup.signing",
                State = signing is null && anyConfigured
                    ? SetupSecretBindingReadinessState.Unconfigured
                    : signingState
            },
            new()
            {
                BindingKey = "setup.encryption",
                State = encryption is null && anyConfigured
                    ? SetupSecretBindingReadinessState.Unconfigured
                    : encryptionState
            }
        ];

        return new(
            SetupLiveApplicationStatus.Success,
            Array.AsReadOnly(items),
            canWrite);
    }

    public async Task<SetupLiveApplicationStatus> ValidateSecretWriteAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capability,
        string bindingKey,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(bindingKey, "setup.signing", StringComparison.Ordinal)
            && !string.Equals(
                bindingKey,
                "setup.encryption",
                StringComparison.Ordinal))
        {
            return SetupLiveApplicationStatus.Unavailable;
        }

        AuthorizedEnrollment? authorized = await AuthorizeCurrentEnrollmentAsync(
            tenantId,
            enrollmentId,
            userId,
            capability,
            AuthorizationActions.Tenants.Update,
            requiredScope: SetupEnrollmentScope.SecretBindingWrite,
            cancellationToken);
        return authorized is null
            ? SetupLiveApplicationStatus.Unavailable
            : SetupLiveApplicationStatus.Success;
    }

    private async Task<AuthorizedEnrollment?> AuthorizeEnrollmentAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capabilityValue,
        string action,
        SetupEnrollmentScope requiredScope,
        CancellationToken cancellationToken)
        => await AuthorizeEnrollmentCoreAsync(
            tenantId,
            enrollmentId,
            userId,
            capabilityValue,
            action,
            requiredScope,
            useCurrentSnapshot: false,
            cancellationToken);

    private async Task<AuthorizedEnrollment?> AuthorizeCurrentEnrollmentAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capabilityValue,
        string action,
        SetupEnrollmentScope requiredScope,
        CancellationToken cancellationToken)
        => await AuthorizeEnrollmentCoreAsync(
            tenantId,
            enrollmentId,
            userId,
            capabilityValue,
            action,
            requiredScope,
            useCurrentSnapshot: true,
            cancellationToken);

    private async Task<AuthorizedEnrollment?> AuthorizeEnrollmentCoreAsync(
        Guid tenantId,
        Guid enrollmentId,
        Guid userId,
        string? capabilityValue,
        string action,
        SetupEnrollmentScope requiredScope,
        bool useCurrentSnapshot,
        CancellationToken cancellationToken)
    {
        if (!IsVersion7(tenantId)
            || tenantContext.TenantId != tenantId
            || !IsVersion7(enrollmentId)
            || !IsVersion7(userId))
        {
            return null;
        }

        Explore.Domain.Actor? actor = await actorRepository.GetActorByUserIdAndTenantId(
            userId,
            tenantId,
            cancellationToken);
        if (actor is null
            || !IsVersion7(actor.Id)
            || !await IsAuthorizedAsync(tenantId, userId, action, cancellationToken))
        {
            return null;
        }

        SetupTargetEnrollment? enrollment = useCurrentSnapshot
            ? await setupLiveRepository.FindCurrentEnrollmentAsync(
                tenantId,
                enrollmentId,
                cancellationToken)
            : await setupLiveRepository.FindEnrollmentAsync(
                tenantId,
                enrollmentId,
                cancellationToken);
        if (enrollment is null
            || !SetupEnrollmentCapability.TryCreate(
                capabilityValue,
                out SetupEnrollmentCapability? capability)
            || !CapabilityMatches(enrollment, capability!)
            || !enrollment.IsAvailable(
                tenantId,
                actor.Id,
                enrollment.Generation,
                UtcNow()))
        {
            return null;
        }

        IReadOnlyList<SetupEnrollmentScope> scopes = RecoverScopes(
            enrollment.ScopeDigest);
        return scopes.Contains(requiredScope)
            ? new AuthorizedEnrollment(enrollment, scopes)
            : null;
    }

    private async Task<bool> IsAuthorizedAsync(
        Guid tenantId,
        Guid userId,
        string action,
        CancellationToken cancellationToken)
    {
        AuthorizationDecision decision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(
                ResourceKinds.Tenant,
                tenantId.ToString("D"),
                action,
                new AuthorizationScope(TenantId: tenantId.ToString("D")),
                new TenantScopedAuthorizationFacts(tenantId),
                Subject: new AuthorizationSubject(userId),
                Tenant: new AuthorizationTenant(TenantId: tenantId)),
            cancellationToken);
        return decision.IsAllowed;
    }

    private static SetupTargetEnrollmentData MapEnrollment(
        SetupTargetEnrollment enrollment,
        SetupEnrollmentIssuance issuance,
        DateTime observedAt) => new()
        {
            EnrollmentId = enrollment.Id,
            State = enrollment.State switch
            {
                DomainEnrollmentState.Revoked => WireEnrollmentState.Revoked,
                DomainEnrollmentState.Expired => WireEnrollmentState.Expired,
                _ when observedAt >= enrollment.ExpiresAt => WireEnrollmentState.Expired,
                _ => WireEnrollmentState.Active
            },
            Generation = enrollment.Generation,
            ExpiresAt = new DateTimeOffset(enrollment.ExpiresAt),
            Scopes = RecoverScopes(enrollment.ScopeDigest),
            Issuance = issuance
        };

    private static SetupSecretBindingOperationData MapOperation(
        SetupSecretBindingOperation operation) => new()
        {
            OperationId = operation.Id,
            State = operation.State switch
            {
                DomainOperationState.Succeeded => WireOperationState.Succeeded,
                DomainOperationState.Failed => WireOperationState.Failed,
                DomainOperationState.Cancelled => WireOperationState.Cancelled,
                _ => WireOperationState.Accepted
            },
            Outcome = operation.Outcome switch
            {
                DomainOperationOutcome.Ready => WireOperationOutcome.Ready,
                DomainOperationOutcome.Unavailable => WireOperationOutcome.Unavailable,
                DomainOperationOutcome.Unauthorized => WireOperationOutcome.Unauthorized,
                DomainOperationOutcome.Invalid => WireOperationOutcome.Invalid,
                DomainOperationOutcome.Cancelled => WireOperationOutcome.Cancelled,
                DomainOperationOutcome.UnavailableEnrollment =>
                    WireOperationOutcome.UnavailableEnrollment,
                _ => WireOperationOutcome.Accepted
            },
            EnrollmentGeneration = operation.EnrollmentGeneration,
            CreatedAt = new DateTimeOffset(operation.CreatedAt),
            SettledAt = operation.SettledAt.HasValue
            ? new DateTimeOffset(operation.SettledAt.Value)
            : null
        };

    private async Task CancelOperationAsync(Guid tenantId, Guid operationKey)
    {
        await unitOfWork.ExecuteSerializableAsync(
            async token =>
            {
                SetupSecretBindingOperation? operation =
                    await setupLiveRepository.FindOperationAsync(
                        tenantId,
                        operationKey,
                        token);
                if (operation?.State == DomainOperationState.Accepted)
                {
                    operation.Cancel(UtcNow());
                    await setupLiveRepository.SaveChangesAsync(token);
                }
                return true;
            },
            CancellationToken.None);
    }

    private static DomainOperationOutcome MapFailure(
        SetupSecretBindingWriteOutcome outcome) => outcome switch
        {
            SetupSecretBindingWriteOutcome.Unauthorized =>
                DomainOperationOutcome.Unauthorized,
            SetupSecretBindingWriteOutcome.Invalid => DomainOperationOutcome.Invalid,
            _ => DomainOperationOutcome.Unavailable
        };

    private async Task<SetupSecretBindingReadinessState> ReadinessStateAsync(
        SecretBinding? binding,
        CancellationToken cancellationToken)
    {
        if (binding is null)
            return SetupSecretBindingReadinessState.Unavailable;
        SetupSecretBindingWriteOutcome outcome =
            await secretBindingReadiness.GetReadinessAsync(
                binding.Id,
                binding.SettingKey,
                cancellationToken);
        return outcome switch
        {
            SetupSecretBindingWriteOutcome.Ready =>
                SetupSecretBindingReadinessState.Ready,
            SetupSecretBindingWriteOutcome.Invalid =>
                SetupSecretBindingReadinessState.Invalid,
            SetupSecretBindingWriteOutcome.Unauthorized =>
                SetupSecretBindingReadinessState.Unauthorized,
            _ => SetupSecretBindingReadinessState.Unavailable
        };
    }

    private static bool IsSupportedBindingKey(string value) =>
        string.Equals(value, "setup.signing", StringComparison.Ordinal)
        || string.Equals(value, "setup.encryption", StringComparison.Ordinal);

    private static IReadOnlyList<SetupEnrollmentScope> RecoverScopes(
        string expectedDigest)
    {
        SetupEnrollmentScope[] all = Enum.GetValues<SetupEnrollmentScope>();
        for (int mask = 1; mask < 1 << all.Length; mask++)
        {
            SetupEnrollmentScope[] candidate = all
                .Where((_, index) => (mask & 1 << index) != 0)
                .ToArray();
            if (FixedTimeEquals(ScopeDigest(candidate), expectedDigest))
                return Array.AsReadOnly(candidate);
        }

        throw new InvalidOperationException("Setup enrollment scope evidence is invalid.");
    }

    private static string ScopeDigest(IEnumerable<SetupEnrollmentScope> scopes) =>
        Digest(string.Join(
            '\n',
            scopes.Select(ScopeWireValue).Order(StringComparer.Ordinal)));

    private static string ScopeWireValue(SetupEnrollmentScope scope) => scope switch
    {
        SetupEnrollmentScope.TargetRead => "target.read",
        SetupEnrollmentScope.SecretBindingReadiness => "secret_binding.readiness",
        SetupEnrollmentScope.SecretBindingWrite => "secret_binding.write",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    private static bool CapabilityMatches(
        SetupTargetEnrollment enrollment,
        SetupEnrollmentCapability capability) =>
        FixedTimeEquals(
            enrollment.CapabilityDigest,
            Digest(capability.ToHeaderValue()));

    private static bool FixedTimeEquals(string left, string right) =>
        left.Length == right.Length
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsVersion7(Guid value)
    {
        if (value == Guid.Empty || value.Version != 7)
            return false;
        Span<byte> bytes = stackalloc byte[16];
        return value.TryWriteBytes(bytes, bigEndian: true, out int written)
            && written == bytes.Length
            && (bytes[8] & 0b1100_0000) == 0b1000_0000;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private async Task LogMilestoneAsync(string operation, string milestone)
    {
        Task emission;
        using (ExecutionContext.SuppressFlow())
        {
            emission = Task.Run(() => logger.LogInformation(
                MilestoneEvent,
                "Setup live milestone {SetupOperation} {SetupMilestone}",
                operation,
                milestone));
        }
        await emission.ConfigureAwait(false);
    }

    private sealed record AuthorizedEnrollment(
        SetupTargetEnrollment Enrollment,
        IReadOnlyList<SetupEnrollmentScope> Scopes);

    private sealed record SecretWriteAdmission(
        SetupLiveApplicationStatus Status,
        SetupSecretBindingOperation? Operation = null);
}
