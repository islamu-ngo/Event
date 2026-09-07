using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Authentication;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Services.Federation;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Federation.Atproto.Services;

public sealed record AtprotoEventPublicationInput(
    Guid TenantId,
    Guid UserId,
    Guid EventId,
    Guid SourceVersion,
    PdsSyncOperation Operation,
    Guid OutboxId,
    DateTime CreatedAtUtc,
    bool RestoreOnly = false);

public sealed record AtprotoPublicationPlanningResult(
    bool Enqueued,
    string? ReasonCode,
    PdsSyncOutbox? Outbox)
{
    public static AtprotoPublicationPlanningResult Skipped(string reasonCode) => new(false, reasonCode, null);
    public static AtprotoPublicationPlanningResult Added(PdsSyncOutbox outbox) => new(true, null, outbox);
}

public sealed record AtprotoDeliveryGateResult(bool Allowed, string? ReasonCode)
{
    public static AtprotoDeliveryGateResult Permit() => new(true, null);
    public static AtprotoDeliveryGateResult Deny(string reasonCode) => new(false, reasonCode);
}

public interface IAtprotoDeliveryGate
{
    Task<AtprotoDeliveryGateResult> CheckDeliveryAsync(
        PdsSyncOutbox outbox,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);
}

public sealed record AtprotoLocationPrivacyCorrectionInput(
    Guid TenantId,
    Guid EventId,
    Guid CorrectionId,
    DateTime CreatedAtUtc);

public interface IAtprotoLocationPrivacyCorrectionPlanner
{
    Task<AtprotoPublicationPlanningResult> PlanLocationPrivacyCorrectionAsync(
        AtprotoLocationPrivacyCorrectionInput correction,
        CancellationToken cancellationToken);
}

public sealed class AtprotoEventPublicationPlanner(
    AtprotoEventGovernanceResolver governanceResolver,
    IEventRepository eventRepository,
    IAtprotoRecordRepository recordRepository,
    IUserAuthenticationTokenRepository sessionRepository,
    IUserExternalLoginRepository externalLoginRepository,
    IAtprotoPublicationPayloadBuilder payloadBuilder,
    IPdsSyncOutboxRepository outboxRepository,
    ILogger<AtprotoEventPublicationPlanner> logger) :
    IAtprotoDeliveryGate,
    IAtprotoLocationPrivacyCorrectionPlanner
{
    public const string EventCollection = "community.lexicon.calendar.event";
    public const string EventSourceType = "Event";
    private const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private static readonly TimeSpan MaximumRemoteCallWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CompensationSafetyWindow = TimeSpan.FromSeconds(5);

    private AtprotoPublicationPlanningResult Skipped(
        string sourceEntityType,
        PdsSyncOperation operation,
        string reasonCode)
    {
        logger.LogInformation(
            "ATProto publication planning skipped for {SourceEntityType} {Operation} with bounded reason {ReasonCode}",
            sourceEntityType,
            operation,
            reasonCode);
        return AtprotoPublicationPlanningResult.Skipped(reasonCode);
    }

    private AtprotoPublicationPlanningResult Skipped(
        AtprotoEventPublicationInput request,
        string reasonCode) => Skipped(EventSourceType, request.Operation, reasonCode);

    public async Task<AtprotoPublicationPlanningResult> PlanLocationPrivacyCorrectionAsync(
        AtprotoLocationPrivacyCorrectionInput correction,
        CancellationToken cancellationToken)
    {
        if (correction.TenantId == Guid.Empty
            || correction.EventId == Guid.Empty
            || correction.CorrectionId == Guid.Empty
            || correction.CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "ATProto location-privacy correction is invalid.",
                nameof(correction));
        }

        if (await outboxRepository.ExistsAsync(
                correction.TenantId,
                correction.CorrectionId,
                cancellationToken))
        {
            return Skipped(EventSourceType, PdsSyncOperation.Update, "correction_already_planned");
        }

        Explore.Domain.Event? eventEntity = await eventRepository.GetAtprotoLifecycleStateAsync(
            correction.TenantId,
            correction.EventId,
            cancellationToken);
        PdsSyncOperation operation = eventEntity is null
            ? PdsSyncOperation.Delete
            : PdsSyncOperation.Update;

        return await PlanEventAsync(
            new AtprotoEventPublicationInput(
                correction.TenantId,
                Guid.Empty,
                correction.EventId,
                eventEntity?.ConcurrencyStamp ?? correction.CorrectionId,
                operation,
                correction.CorrectionId,
                correction.CreatedAtUtc),
            cancellationToken);
    }

    public async Task<AtprotoPublicationPlanningResult> PlanEventAsync(
        AtprotoEventPublicationInput request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var ownerUserId = request.UserId;
        var recordKey = request.OutboxId.ToString("N");
        Guid? atprotoRecordId = null;
        string? expectedCid = null;
        var plannedOperation = request.Operation;
        var createHasExistingRecord = false;
        var createHasUnsettledPredecessor = false;
        PdsSyncOutbox? unsettledPredecessor = await outboxRepository.GetLatestUnsettledMutationAsync(
            request.TenantId,
            EventSourceType,
            request.EventId,
            EventCollection,
            cancellationToken);
        AtprotoOutboundRecordOwnership? ownership = await recordRepository.GetOwnedRecordForSourceAsync(
            request.TenantId,
            EventSourceType,
            request.EventId,
            cancellationToken);
        if (request.Operation != PdsSyncOperation.Create)
        {
            if (unsettledPredecessor is not null)
            {
                ownerUserId = unsettledPredecessor.UserId;
                recordKey = unsettledPredecessor.RecordKey;
                atprotoRecordId = ownership?.AtprotoRecord?.Id;
            }
            else if (ownership?.AtprotoRecord is { TombstonedAt: null } record)
            {
                ownerUserId = ownership.UserId;
                recordKey = record.RecordKey;
                atprotoRecordId = record.Id;
                expectedCid = record.Cid;
            }
            else
            {
                return Skipped(request, "remote_record_missing");
            }
        }
        else if (request.RestoreOnly)
        {
            if (unsettledPredecessor is not null)
            {
                ownerUserId = unsettledPredecessor.UserId;
                recordKey = unsettledPredecessor.RecordKey;
                atprotoRecordId = ownership?.AtprotoRecord?.Id;
                plannedOperation = PdsSyncOperation.Update;
            }
            else if (ownership?.AtprotoRecord is { TombstonedAt: not null } tombstonedRecord)
            {
                ownerUserId = ownership.UserId;
                recordKey = tombstonedRecord.RecordKey;
                atprotoRecordId = tombstonedRecord.Id;
            }
            else
            {
                return Skipped(request, "restore_ownership_missing");
            }
        }
        else
        {
            if (ownership?.AtprotoRecord is { TombstonedAt: null } record)
            {
                ownerUserId = ownership.UserId;
                recordKey = record.RecordKey;
                atprotoRecordId = record.Id;
                expectedCid = record.Cid;
                createHasExistingRecord = true;
            }

            else if (ownership?.AtprotoRecord is { TombstonedAt: not null } tombstonedRecord)
            {
                ownerUserId = ownership.UserId;
                recordKey = tombstonedRecord.RecordKey;
                atprotoRecordId = tombstonedRecord.Id;
            }
            else if (unsettledPredecessor is not null)
            {
                ownerUserId = unsettledPredecessor.UserId;
                recordKey = unsettledPredecessor.RecordKey;
                atprotoRecordId = ownership?.AtprotoRecord?.Id;
                createHasUnsettledPredecessor = true;
            }
        }

        AtprotoEventGovernance governance = await governanceResolver.ResolveAsync(
            request.TenantId,
            ownerUserId,
            cancellationToken);
        if (!governance.EventsEnabled)
        {
            return Skipped(request, "capability_disabled");
        }

        if (!governance.PublishMyEvents)
        {
            return Skipped(request, "consent_missing");
        }

        IReadOnlyList<Explore.Domain.UserAuthenticationToken> sessions =
            await sessionRepository.GetAtprotoSessionsForReadAsync(
                request.TenantId,
                ownerUserId,
                RepositoryBackedAtprotoSession.Provider,
                cancellationToken);
        if (sessions.Count != 1
            || string.IsNullOrWhiteSpace(sessions[0].SubjectDid)
            || !Uri.TryCreate(sessions[0].PdsHost, UriKind.Absolute, out Uri? pdsHost)
            || pdsHost.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(pdsHost.Query)
            || !string.IsNullOrEmpty(pdsHost.Fragment))
        {
            return Skipped(request, "session_missing");
        }

        if (unsettledPredecessor is not null
            && (!string.Equals(sessions[0].SubjectDid, unsettledPredecessor.Did, StringComparison.Ordinal)
                || !SamePds(pdsHost, unsettledPredecessor.PdsHost)))
        {
            return Skipped(request, "session_binding_changed");
        }

        Explore.Domain.UserExternalLogin? login = await externalLoginRepository.GetByProviderAndKey(
            PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(
                Explore.Domain.ValueObjects.AtprotoDid.Parse(sessions[0].SubjectDid)));
        if (login is null
            || login.UserId != ownerUserId
            || login.AuthenticationProviderId != (int)AuthenticationProviderKind.Atproto
            || !string.Equals(login.ProviderKey, sessions[0].SubjectDid, StringComparison.Ordinal))
        {
            return Skipped(request, "account_not_linked");
        }

        bool hasGroundedRemoteMutation = unsettledPredecessor is not null
            || ownership?.AtprotoRecord is { TombstonedAt: null };
        if (request.Operation != PdsSyncOperation.Delete)
        {
            bool isPubliclyEligible = await eventRepository.IsPubliclyEligibleAsync(
                request.TenantId,
                request.EventId,
                cancellationToken);
            if (!isPubliclyEligible)
            {
                if (request.RestoreOnly || !hasGroundedRemoteMutation)
                {
                    return Skipped(request, "privacy_ineligible");
                }

                plannedOperation = PdsSyncOperation.Delete;
            }
            else if (createHasExistingRecord)
            {
                return Skipped(request, "remote_record_exists");
            }
            else if (createHasUnsettledPredecessor)
            {
                return Skipped(request, "publication_pending");
            }
        }

        AtprotoPublicationPayload? payload = null;
        bool hasGroundedIdentity = ownership?.AtprotoRecord is not null || unsettledPredecessor is not null;
        if (plannedOperation != PdsSyncOperation.Delete)
        {
            AtprotoEventPublicationEntityGraph? graph = await eventRepository.GetAtprotoPublicationGraphAsync(
                request.TenantId,
                request.EventId,
                cancellationToken);
            if (graph is null || graph.Event.ConcurrencyStamp != request.SourceVersion)
            {
                if (!hasGroundedIdentity || request.Operation != PdsSyncOperation.Update)
                {
                    return Skipped(request, "source_version_changed");
                }

                Explore.Domain.Event? lifecycle = await eventRepository.GetAtprotoLifecycleStateAsync(
                    request.TenantId,
                    request.EventId,
                    cancellationToken);
                if (lifecycle is null || lifecycle.ConcurrencyStamp != request.SourceVersion)
                {
                    return Skipped(request, "source_version_changed");
                }

                plannedOperation = PdsSyncOperation.Delete;
            }
            else
            {
                if (!HasActiveEventIdentity(graph.Event, sessions[0].SubjectDid))
                {
                    if (request.RestoreOnly || !hasGroundedRemoteMutation)
                    {
                        return Skipped(request, "identity_inactive");
                    }

                    plannedOperation = PdsSyncOperation.Delete;
                }
                else
                {
                    AtprotoPublicationPayloadBuildResult payloadResult = await payloadBuilder.BuildEventAsync(
                        graph,
                        new DateTimeOffset(request.CreatedAtUtc),
                        cancellationToken);
                    if (!payloadResult.IsValid)
                    {
                        if (!hasGroundedIdentity || request.Operation != PdsSyncOperation.Update)
                        {
                            return Skipped(request, payloadResult.FailureCode ?? "payload_invalid");
                        }

                        plannedOperation = PdsSyncOperation.Delete;
                    }
                    else
                    {
                        payload = payloadResult.Payload;
                    }
                }

            }
        }

        var outbox = new PdsSyncOutbox
        {
            Id = request.OutboxId,
            TenantId = request.TenantId,
            UserId = ownerUserId,
            Did = sessions[0].SubjectDid,
            Collection = EventCollection,
            RecordKey = recordKey,
            Operation = plannedOperation,
            Payload = payload?.Json,
            PayloadHash = payload?.Sha256 ?? EmptyPayloadHash,
            IdempotencyKey = request.OutboxId.ToString("N"),
            PdsHost = pdsHost.AbsoluteUri,
            SourceEntityType = EventSourceType,
            SourceEntityId = request.EventId,
            SourceVersion = request.SourceVersion,
            AtprotoRecordId = atprotoRecordId,
            ExpectedCid = expectedCid,
            Status = PdsSyncStatus.Pending,
            CreatedAt = request.CreatedAtUtc,
            NextRetryAt = CompensationNotBefore(unsettledPredecessor, request.CreatedAtUtc),
            MaxRetries = 10
        };

        await outboxRepository.SupersedePriorAsync(
            request.TenantId,
            EventSourceType,
            request.EventId,
            request.OutboxId,
            request.CreatedAtUtc,
            cancellationToken);
        await outboxRepository.AddAsync(outbox, cancellationToken);
        return AtprotoPublicationPlanningResult.Added(outbox);
    }

    private static DateTime? CompensationNotBefore(PdsSyncOutbox? priorCreate, DateTime createdAtUtc)
    {
        if (priorCreate is null)
        {
            return null;
        }

        DateTime? notBefore = priorCreate.NextRetryAt > createdAtUtc
            ? priorCreate.NextRetryAt
            : null;
        if (priorCreate.Status != PdsSyncStatus.Processing)
        {
            return notBefore;
        }

        DateTime remoteWindowEnd = createdAtUtc.Add(MaximumRemoteCallWindow);
        DateTime leaseEnd = priorCreate.LeaseExpiresAt is { Kind: DateTimeKind.Utc } value
            ? value
            : remoteWindowEnd;
        DateTime processingSafetyEnd = (leaseEnd > remoteWindowEnd ? leaseEnd : remoteWindowEnd)
            .Add(CompensationSafetyWindow);
        return notBefore > processingSafetyEnd ? notBefore : processingSafetyEnd;
    }

    private static bool SamePds(Uri current, string expected) =>
        Uri.TryCreate(expected, UriKind.Absolute, out Uri? expectedUri)
        && string.Equals(current.Scheme, expectedUri.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(current.Host, expectedUri.Host, StringComparison.OrdinalIgnoreCase)
        && current.Port == expectedUri.Port
        && string.Equals(current.AbsolutePath.TrimEnd('/'), expectedUri.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);

    private static bool HasActiveEventIdentity(Explore.Domain.Event eventEntity, string did) =>
        eventEntity.Actor.AtprotoIdentities.Any(identity =>
            identity.ActorId == eventEntity.ActorId
            && string.Equals(identity.Did, did, StringComparison.Ordinal)
            && identity.IsActive
            && !identity.IsSuspended
            && !identity.IsDeleted);

    public async Task<AtprotoDeliveryGateResult> CheckDeliveryAsync(
        PdsSyncOutbox outbox,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        if (outbox.SourceEntityType != EventSourceType
            || outbox.Collection != EventCollection
            || outbox.TenantId == Guid.Empty
            || outbox.UserId == Guid.Empty)
        {
            return AtprotoDeliveryGateResult.Deny("ownership_invalid");
        }

        AtprotoEventGovernance governance = await governanceResolver.ResolveAsync(
            outbox.TenantId,
            outbox.UserId,
            cancellationToken);
        if (!governance.EventsEnabled)
        {
            return AtprotoDeliveryGateResult.Deny("capability_disabled");
        }

        if (!governance.PublishMyEvents)
        {
            return AtprotoDeliveryGateResult.Deny("consent_missing");
        }

        IReadOnlyList<Explore.Domain.UserAuthenticationToken> sessions =
            await sessionRepository.GetAtprotoSessionsForReadAsync(
                outbox.TenantId,
                outbox.UserId,
                RepositoryBackedAtprotoSession.Provider,
                cancellationToken);
        if (sessions.Count != 1
            || !string.Equals(sessions[0].SubjectDid, outbox.Did, StringComparison.Ordinal)
            || !string.Equals(sessions[0].PdsHost, outbox.PdsHost, StringComparison.Ordinal))
        {
            return AtprotoDeliveryGateResult.Deny("session_missing");
        }

        Explore.Domain.UserExternalLogin? login = await externalLoginRepository.GetByProviderAndKey(
            PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(
                Explore.Domain.ValueObjects.AtprotoDid.Parse(outbox.Did)));
        if (login is null
            || login.UserId != outbox.UserId
            || login.AuthenticationProviderId != (int)AuthenticationProviderKind.Atproto
            || !string.Equals(login.ProviderKey, outbox.Did, StringComparison.Ordinal))
        {
            return AtprotoDeliveryGateResult.Deny("account_not_linked");
        }

        Explore.Domain.Event? eventEntity = await eventRepository.GetAtprotoLifecycleStateAsync(
            outbox.TenantId,
            outbox.SourceEntityId,
            cancellationToken);
        if (eventEntity is null || eventEntity.ConcurrencyStamp != outbox.SourceVersion)
        {
            return AtprotoDeliveryGateResult.Deny("source_version_changed");
        }

        if (outbox.Operation == PdsSyncOperation.Delete)
        {
            if (outbox.AtprotoRecordId is Guid atprotoRecordId)
            {
                AtprotoOutboundRecordOwnership? ownership = await recordRepository.GetOwnedRecordForSourceAsync(
                    outbox.TenantId,
                    EventSourceType,
                    outbox.SourceEntityId,
                    cancellationToken);
                if (ownership?.AtprotoRecord is not { } record
                    || ownership.AtprotoRecordId != atprotoRecordId
                    || ownership.UserId != outbox.UserId
                    || record.Id != atprotoRecordId
                    || !string.Equals(record.Did, outbox.Did, StringComparison.Ordinal)
                    || !string.Equals(record.Collection, outbox.Collection, StringComparison.Ordinal)
                    || !string.Equals(record.RecordKey, outbox.RecordKey, StringComparison.Ordinal)
                    || outbox.ExpectedCid is not null
                        && !string.Equals(record.Cid, outbox.ExpectedCid, StringComparison.Ordinal))
                {
                    return AtprotoDeliveryGateResult.Deny("ownership_invalid");
                }
            }

            if (!await eventRepository.IsPubliclyEligibleAsync(
                    outbox.TenantId,
                    outbox.SourceEntityId,
                    cancellationToken))
            {
                return AtprotoDeliveryGateResult.Permit();
            }

            AtprotoEventPublicationEntityGraph? removalGraph = await eventRepository.GetAtprotoPublicationGraphAsync(
                outbox.TenantId,
                outbox.SourceEntityId,
                cancellationToken);
            if (removalGraph is null || !HasActiveEventIdentity(removalGraph.Event, outbox.Did))
            {
                return AtprotoDeliveryGateResult.Permit();
            }

            AtprotoPublicationPayloadBuildResult removalProjection = await payloadBuilder.BuildEventAsync(
                removalGraph,
                observedAt,
                cancellationToken);
            return removalProjection.IsValid
                ? AtprotoDeliveryGateResult.Deny("delete_state_invalid")
                : AtprotoDeliveryGateResult.Permit();
        }

        if (!await eventRepository.IsPubliclyEligibleAsync(
                outbox.TenantId,
                outbox.SourceEntityId,
                cancellationToken))
        {
            return AtprotoDeliveryGateResult.Deny("privacy_ineligible");
        }

        AtprotoEventPublicationEntityGraph? graph = await eventRepository.GetAtprotoPublicationGraphAsync(
            outbox.TenantId,
            outbox.SourceEntityId,
            cancellationToken);
        if (graph is null)
        {
            return AtprotoDeliveryGateResult.Deny("privacy_ineligible");
        }

        if (!HasActiveEventIdentity(graph.Event, outbox.Did))
        {
            return AtprotoDeliveryGateResult.Deny("identity_inactive");
        }

        AtprotoPublicationPayloadBuildResult currentPayload = await payloadBuilder.BuildEventAsync(
            graph,
            observedAt,
            cancellationToken);
        return currentPayload.IsValid
               && string.Equals(currentPayload.Payload!.Sha256, outbox.PayloadHash, StringComparison.Ordinal)
               && string.Equals(currentPayload.Payload.Json, outbox.Payload, StringComparison.Ordinal)
            ? AtprotoDeliveryGateResult.Permit()
            : AtprotoDeliveryGateResult.Deny("privacy_or_payload_changed");
    }

    private static void Validate(AtprotoEventPublicationInput request)
    {
        if (request.TenantId == Guid.Empty
            || request.EventId == Guid.Empty
            || request.SourceVersion == Guid.Empty
            || request.OutboxId == Guid.Empty
            || request.CreatedAtUtc.Kind != DateTimeKind.Utc
            || request.Operation is < PdsSyncOperation.Create or > PdsSyncOperation.Delete
            || request.Operation == PdsSyncOperation.Create && request.UserId == Guid.Empty
            || request.RestoreOnly && request.Operation != PdsSyncOperation.Create)
        {
            throw new ArgumentException("ATProto event publication request is invalid.", nameof(request));
        }
    }

}
