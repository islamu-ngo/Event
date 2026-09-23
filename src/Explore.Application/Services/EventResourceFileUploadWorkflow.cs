using System.Globalization;
using System.Security.Cryptography;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventResource;
using Explore.Application.DTOs.EventResource.Validators;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Exceptions;
using Explore.Application.Features.StorageObjects.Handlers.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;

namespace Explore.Application.Services;

/// <summary>
/// One transaction owner for resource reservations and attachments. Provider writes run between
/// short transactions against a durable delete-requested staging identity: crashes never require
/// an in-memory cleanup queue. Only a freshly authorized attachment promotes that identity.
/// </summary>
public sealed class EventResourceFileUploadWorkflow(
    IEventResourceRepository resources, IStorageUploadSessionRepository sessions,
    IStorageObjectRepository objects, IStorageUsageCounterRepository counters,
    IPrivacyErasureStateRepository privacy, IStoragePolicyResolver storagePolicy,
    IFileStorageProviderResolver providers, IUnitOfWork unitOfWork,
    EventResourceAuthorityOrchestrator authority, ITenantContext tenant,
    ICurrentUserService user, IMachinePrincipalAccessor machine, TimeProvider clock)
{
    public async Task<BaseCommandResponse<StorageUploadSessionDto>> ReserveAsync(Guid resourceId,
        CreateEventResourceUploadSessionDto dto, CancellationToken cancellationToken)
    {
        var validation = await new CreateEventResourceUploadSessionDtoValidator().ValidateAsync(dto, cancellationToken);
        if (resourceId == Guid.Empty || !validation.IsValid)
            return BaseCommandResponse.Validation<StorageUploadSessionDto>(["The resource upload intent is invalid."]);
        if (await FencedAsync(cancellationToken)) return Fail("privacy_erasure_fenced");
        await using var authorized = await AuthorizeAsync(resourceId, cancellationToken);
        if (authorized.Lease is not { } lease) return Denied(authorized.Outcome);
        var policy = await PolicyAsync(resourceId, dto.ContentType, dto.ExpectedSizeBytes, cancellationToken);
        if (!Permitted(lease, dto.ContentType, dto.ExpectedSizeBytes, policy)) return Fail("resource_upload_policy_denied");
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var outcome = await authority.RecheckMutationAsync(lease, dto.ExpectedVersion, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return Denied(outcome);
                if (await FencedAsync(ct)) return Fail("privacy_erasure_fenced");
                var existing = await sessions.GetByTenantAndIdempotencyKeyForUpdateAsync(tenant.TenantId, dto.IdempotencyKey, ct);
                if (existing is not null)
                {
                    if (!Owned(existing) || existing.OwningResourceId != resourceId || existing.ExpectedResourceVersion != dto.ExpectedVersion
                        || existing.ExpectedSizeBytes != dto.ExpectedSizeBytes || existing.ContentType != dto.ContentType
                        || existing.SafeDisplayName != dto.SafeDisplayName
                        || existing.Extension != CreateEventResourceUploadSessionDtoValidator.ExtensionFor(dto.ContentType)
                        || existing.Status != StorageUploadSessionStates.Reserved)
                        return Fail(FailureCodes.ConcurrencyConflict);
                    return Success(existing, policy, await counters.GetByTenantAndProviderAsync(tenant.TenantId, existing.Provider, ct));
                }
                var counter = await counters.GetOrCreateAsync(tenant.TenantId, policy.Provider, ct);
                var quotaDenial = await CheckQuotaAsync(counter, dto.ExpectedSizeBytes, policy.TenantQuotaBytes, ct);
                if (quotaDenial is not null) return quotaDenial;
                counter.Reserve(dto.ExpectedSizeBytes, policy.TenantQuotaBytes);
                await counters.Update(counter);
                var session = new StorageUploadSession
                {
                    Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, UserId = user.UserId,
                    Provider = policy.Provider, RouteKey = policy.RouteKey, PolicyMaxUploadBytes = policy.MaxUploadBytes,
                    PolicyVersion = policy.PolicyVersion.ToString(CultureInfo.InvariantCulture),
                    ExpectedSizeBytes = dto.ExpectedSizeBytes, ReservedBytes = dto.ExpectedSizeBytes,
                    ContentType = dto.ContentType, SafeDisplayName = dto.SafeDisplayName,
                    Extension = CreateEventResourceUploadSessionDtoValidator.ExtensionFor(dto.ContentType),
                    Purpose = StorageObjectPurposes.EventResource, Visibility = StorageObjectVisibilities.PrivateOwner,
                    OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = resourceId,
                    Status = StorageUploadSessionStates.Reserved, IdempotencyKey = dto.IdempotencyKey,
                    ExpiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(15)
                };
                session.BindEventResourceVersion(dto.ExpectedVersion);
                await sessions.Create(session);
                return Success(session, policy, counter);
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException) { return Fail(FailureCodes.ConcurrencyConflict); }
    }

    public async Task<BaseCommandResponse<StorageUploadSessionDto>> FinalizeAsync(
        FinalizeStorageUploadSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await sessions.GetForAuthorizationAsync(request.UploadSessionId, cancellationToken);
        if (!Owned(session)) return Fail(FailureCodes.StorageUploadSessionNotFound);
        if (await FencedAsync(cancellationToken)) return Fail("privacy_erasure_fenced");
        await using var authorized = await AuthorizeAsync(session!.OwningResourceId!.Value, cancellationToken);
        if (authorized.Lease is not { } lease) return Denied(authorized.Outcome);
        var policy = await PolicyAsync(session.OwningResourceId.Value, session.ContentType, session.ExpectedSizeBytes, cancellationToken);
        if (!Permitted(lease, session.ContentType, session.ExpectedSizeBytes, policy) || policy.Provider != session.Provider)
            return Fail("resource_upload_policy_denied");
        if (session.Status == StorageUploadSessionStates.Finalized)
            return await ReplayAsync(session.Id, lease, policy, cancellationToken);
        if (session.Status != StorageUploadSessionStates.Reserved) return Fail(FailureCodes.StorageUploadSessionInvalidState);
        if (session.ExpiresAt <= clock.GetUtcNow().UtcDateTime) return Fail(FailureCodes.StorageUploadSessionExpired);
        if (request.Content is null || !request.Content.CanRead
            || request.ContentLength is { } length && length != session.ExpectedSizeBytes
            || request.ContentType is { } contentType && !string.Equals(contentType, session.ContentType, StringComparison.OrdinalIgnoreCase))
            return Fail(FailureCodes.StorageUploadSizeMismatch);

        var inspection = await EventResourceDocumentInspection.InspectAsync(request.Content, session.ContentType,
            session.Extension, session.ExpectedSizeBytes, Math.Min(policy.MaxUploadBytes, lease.Snapshot.Facts.Access.GovernancePolicy!.MaxUploadBytes), cancellationToken);
        await using var ownedInspection = ReferenceEquals(inspection.Content, request.Content) ? null : inspection.Content;
        if (!inspection.Success) return Fail(FailureCodes.StorageUploadContentSignatureMismatch);
        var inspected = inspection.Content;
        string checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(inspected, cancellationToken));
        inspected.Position = 0;
        try
        {
            var staged = await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var current = await sessions.GetByIdForUpdateAsync(session.Id, ct);
                if (!Owned(current)) return Fail(FailureCodes.StorageUploadSessionNotFound);
                var outcome = await authority.RecheckMutationAsync(lease, current!.ExpectedResourceVersion!.Value, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return Denied(outcome);
                if (await FencedAsync(ct)) return Fail("privacy_erasure_fenced");
                if (current.Status != StorageUploadSessionStates.Reserved) return Fail(FailureCodes.StorageUploadSessionInvalidState);
                if (current.ExpiresAt <= clock.GetUtcNow().UtcDateTime) return Fail(FailureCodes.StorageUploadSessionExpired);
                current.ReserveObjectKey($"tenants/{current.TenantId:N}/uploads/{current.Id:N}.{current.Extension}");
                current.MarkUploading(clock.GetUtcNow().UtcDateTime);
                var stagedObject = NewStagedObject(current);
                await objects.Create(stagedObject);
                current.StageEventResourceObject(stagedObject.Id);
                await sessions.Update(current);
                return Success(current, policy, null);
            }, cancellationToken);
            if (!staged.IsSuccess) return staged;
        }
        catch (ConcurrencyConflictException) { return Fail(FailureCodes.ConcurrencyConflict); }

        // Fresh detached session now contains the durable provider key and staged object identity.
        session = (await sessions.GetForAuthorizationAsync(session.Id, cancellationToken))!;
        FileStorageWriteResult write;
        try
        {
            write = await providers.GetRequired(session.Provider).WriteAsync(new FileStorageWriteInput(
                session.TenantId, inspected, session.ContentType, session.SafeDisplayName, session.Extension,
                session.ExpectedSizeBytes, session.ExpectedSizeBytes, session.ObjectKey), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            return await CloseFailedAsync(session.Id, FailureCodes.StorageUploadWriteFailed, cancellationToken);
        }
        if (write.Provider != session.Provider || write.ObjectKey != session.ObjectKey || write.SizeBytes != session.ExpectedSizeBytes
            || !string.Equals(write.ContentType, session.ContentType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(write.Sha256Checksum, checksum, StringComparison.OrdinalIgnoreCase))
            return await CloseFailedAsync(session.Id, FailureCodes.StorageUploadWriteFailed, cancellationToken);

        // Do not catch database failure as a provider failure. The committed staging row remains
        // delete-requested and the existing reconciler can act on it even without provider inventory.
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var current = await sessions.GetByIdForUpdateAsync(session.Id, ct);
                if (!Owned(current)) return Fail(FailureCodes.StorageUploadSessionNotFound);
                var outcome = await authority.RecheckMutationAsync(lease, current!.ExpectedResourceVersion!.Value, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return await FailUploadingAsync(current, Denied(outcome).FailureCode!, ct);
                if (await FencedAsync(ct)) return await FailUploadingAsync(current, "privacy_erasure_fenced", ct);
                if (current.Status != StorageUploadSessionStates.Uploading || current.StorageObjectId != session.StorageObjectId)
                    return Fail(FailureCodes.StorageUploadSessionInvalidState);
                if (current.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
                    return await FailUploadingAsync(current, FailureCodes.StorageUploadSessionExpired, ct);
                var latestPolicy = await PolicyAsync(current.OwningResourceId!.Value, current.ContentType, current.ExpectedSizeBytes, ct);
                if (!Permitted(lease, current.ContentType, current.ExpectedSizeBytes, latestPolicy) || latestPolicy.Provider != current.Provider)
                    return await FailUploadingAsync(current, "resource_upload_policy_denied", ct);
                var resource = await resources.GetByIdForUpdateAsync(current.TenantId, lease.Snapshot.Facts.Access.Parent.EventId,
                    current.OwningResourceId.Value, ct);
                if (resource is null || resource.IsDeleted || resource.ConcurrencyStamp != current.ExpectedResourceVersion
                    || resource.PublicationStateId == (int)EventResourcePublicationStateEnum.Archived
                    || resource.EventResourceDeliveryTypeId != (int)EventResourceDeliveryTypeEnum.StoredFile)
                    return await FailUploadingAsync(current, FailureCodes.ConcurrencyConflict, ct);
                var stagedObject = await resources.GetStorageObjectAsync(current.TenantId, current.StorageObjectId!.Value, ct);
                if (stagedObject is null || stagedObject.IsDeleted || stagedObject.LifecycleState != StorageObjectLifecycleStates.DeleteRequested
                    || stagedObject.OwningResourceId != resource.Id || stagedObject.ObjectKey != current.ObjectKey
                    || stagedObject.OwningResourceKind != StorageOwningResourceKinds.EventResource
                    || stagedObject.Purpose != StorageObjectPurposes.EventResource || stagedObject.Visibility != StorageObjectVisibilities.PrivateOwner)
                    return await FailUploadingAsync(current, FailureCodes.StorageUploadSessionInvalidState, ct);
                var counter = await counters.GetOrCreateAsync(current.TenantId, current.Provider, ct);
                var quotaDenial = await CheckQuotaAsync(counter, 0, latestPolicy.TenantQuotaBytes, ct);
                if (quotaDenial is not null)
                {
                    await FailUploadingAsync(current, "resource_upload_policy_denied", ct);
                    return quotaDenial;
                }
                StorageObject? previous = null;
                if (resource.StorageObjectId is { } previousId)
                {
                    previous = await resources.GetStorageObjectAsync(current.TenantId, previousId, ct);
                    if (previous is null || previous.OwningResourceKind != StorageOwningResourceKinds.EventResource
                        || previous.OwningResourceId != resource.Id)
                        return await FailUploadingAsync(current, FailureCodes.ConcurrencyConflict, ct);
                }
                outcome = await authority.RecheckMutationAsync(lease, current.ExpectedResourceVersion.Value, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return await FailUploadingAsync(current, Denied(outcome).FailureCode!, ct);
                stagedObject.Sha256Checksum = checksum;
                stagedObject.RecordEventResourceInspection(stagedObject.Id, checksum);
                stagedObject.LifecycleState = StorageObjectLifecycleStates.Active;
                await objects.Update(stagedObject);
                var now = clock.GetUtcNow().UtcDateTime;
                resource.SetStoredFile(stagedObject.Id, current.ExpectedResourceVersion.Value, user.UserId!.Value, now);
                resources.Update(resource);
                if (previous is not null)
                {
                    previous.RequestDelete();
                    await objects.Update(previous);
                }
                counter.FinalizeReservation(current.ReservedBytes);
                await counters.Update(counter);
                if (lease.Snapshot.Facts.Access.GovernancePolicy!.AuditRetentionDays > 0)
                    await resources.AddAuditEntryAsync(EventResourceAuditEntry.Create(current.TenantId, resource.Id, user.UserId,
                        EventResourceAuditAction.ConfigureDelivery, EventResourceAuditOutcome.Succeeded,
                        EventResourceAuditReason.OrganizerMutation, now), ct);
                await resources.SaveChangesAsync(ct);
                current.Finalize(stagedObject.Id, current.ObjectKey!, checksum, now);
                current.RecordFinalizedResourceVersion(resource.ConcurrencyStamp);
                await sessions.Update(current);
                return Success(current, latestPolicy, counter);
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException) { return Fail(FailureCodes.ConcurrencyConflict); }
    }

    public async Task<BaseCommandResponse<StorageUploadSessionDto>> CancelAsync(Guid uploadSessionId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetForAuthorizationAsync(uploadSessionId, cancellationToken);
        if (!Owned(session)) return Fail(FailureCodes.StorageUploadSessionNotFound);
        if (await FencedAsync(cancellationToken)) return Fail("privacy_erasure_fenced");
        await using var authorized = await AuthorizeAsync(session!.OwningResourceId!.Value, cancellationToken);
        if (authorized.Lease is not { } lease) return Denied(authorized.Outcome);
        var policy = await PolicyAsync(session.OwningResourceId.Value, session.ContentType, session.ExpectedSizeBytes, cancellationToken);
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var current = await sessions.GetByIdForUpdateAsync(uploadSessionId, ct);
                if (!Owned(current)) return Fail(FailureCodes.StorageUploadSessionNotFound);
                var outcome = await authority.RecheckMutationAsync(lease, current!.ExpectedResourceVersion!.Value, ct);
                if (outcome != EventResourceAuthorityOutcome.Allowed) return Denied(outcome);
                if (await FencedAsync(ct)) return Fail("privacy_erasure_fenced");
                if (current.Status == StorageUploadSessionStates.Finalized) return Fail(FailureCodes.StorageUploadSessionFinalized);
                var counter = await counters.GetByTenantAndProviderAsync(current.TenantId, current.Provider, ct);
                if (current.Status is StorageUploadSessionStates.Reserved or StorageUploadSessionStates.Uploading)
                {
                    if (counter is not null) { counter.ReleaseReservation(current.ReservedBytes); await counters.Update(counter); }
                    current.Cancel(clock.GetUtcNow().UtcDateTime);
                    await sessions.Update(current);
                }
                return Success(current, policy, counter);
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException) { return Fail(FailureCodes.ConcurrencyConflict); }
    }

    private Task<BaseCommandResponse<StorageUploadSessionDto>> ReplayAsync(Guid id, EventResourceAuthorityLease lease,
        ResolvedStoragePolicy policy, CancellationToken cancellationToken) => unitOfWork.ExecuteSerializableAsync(async ct =>
    {
        var current = await sessions.GetByIdForUpdateAsync(id, ct);
        if (!Owned(current) || current!.Status != StorageUploadSessionStates.Finalized || current.FinalizedResourceVersion is not { } version)
            return Fail(FailureCodes.StorageUploadSessionInvalidState);
        var outcome = await authority.RecheckMutationAsync(lease, version, ct);
        if (outcome != EventResourceAuthorityOutcome.Allowed) return Denied(outcome);
        if (await FencedAsync(ct)) return Fail("privacy_erasure_fenced");
        if (lease.Snapshot.Facts.StorageObjectId != current.StorageObjectId) return Fail(FailureCodes.ConcurrencyConflict);
        var attached = await resources.GetStorageObjectAsync(current.TenantId, current.StorageObjectId!.Value, ct);
        if (attached is null || attached.IsDeleted || attached.LifecycleState != StorageObjectLifecycleStates.Active
            || attached.OwningResourceId != current.OwningResourceId || attached.OwningResourceKind != StorageOwningResourceKinds.EventResource
            || !attached.HasBoundDocumentInspection || attached.Sha256Checksum != current.Sha256Checksum)
            return Fail(FailureCodes.ConcurrencyConflict);
        return Success(current, policy, await counters.GetByTenantAndProviderAsync(current.TenantId, current.Provider, ct));
    }, cancellationToken);

    private Task<BaseCommandResponse<StorageUploadSessionDto>> CloseFailedAsync(Guid id, string code, CancellationToken ct) =>
        unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var current = await sessions.GetByIdForUpdateAsync(id, token);
            return Owned(current) ? await FailUploadingAsync(current!, code, token) : Fail(FailureCodes.StorageUploadSessionNotFound);
        }, ct);

    private async Task<BaseCommandResponse<StorageUploadSessionDto>> FailUploadingAsync(StorageUploadSession session, string code, CancellationToken ct)
    {
        if (session.Status == StorageUploadSessionStates.Uploading)
        {
            var counter = await counters.GetByTenantAndProviderAsync(session.TenantId, session.Provider, ct);
            if (counter is not null) { counter.ReleaseReservation(session.ReservedBytes); await counters.Update(counter); }
            session.Fail(code, null, clock.GetUtcNow().UtcDateTime);
            await sessions.Update(session);
        }
        return Fail(code);
    }

    private async Task<BaseCommandResponse<StorageUploadSessionDto>?> CheckQuotaAsync(
        StorageUsageCounter selected, long additional, long limit, CancellationToken ct)
    {
        decimal total = selected.UsedBytes + (decimal)selected.ReservedBytes;
        foreach (var counter in await counters.GetByTenantAsync(tenant.TenantId, ct))
            if (counter.Provider != selected.Provider) total += counter.UsedBytes + (decimal)counter.ReservedBytes;
        return total + additional <= limit ? null : BaseCommandResponse.Quota<StorageUploadSessionDto>(
            "Upload would exceed the tenant storage quota.", new QuotaExceededDetails(
                GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, QuotaValue(limit), QuotaValue(total),
                QuotaValue(total + additional), "tenant", tenant.TenantId));
    }

    private static int QuotaValue(decimal value) => (int)Math.Min(value, int.MaxValue);

    private Task<ResolvedStoragePolicy> PolicyAsync(Guid resourceId, string type, long size, CancellationToken ct) =>
        storagePolicy.ResolveAsync(tenant.TenantId, new StoragePolicyIntent(StorageObjectPurposes.EventResource,
            StorageObjectVisibilities.PrivateOwner, type, StorageOwningResourceKinds.EventResource, resourceId, size), ct);

    private static bool Permitted(EventResourceAuthorityLease lease, string type, long size, ResolvedStoragePolicy policy) =>
        lease.Snapshot.Facts.Access.GovernancePolicy is { } governance
        && governance.EnabledDeliveryTypes.Contains(EventResourceDeliveryTypeEnum.StoredFile)
        && governance.PermittedFileTypes.Contains(type) && size > 0 && size <= governance.MaxUploadBytes && size <= policy.MaxUploadBytes
        && policy.Provider is StorageProviders.Local or StorageProviders.S3Compatible;

    private bool Owned(StorageUploadSession? session) => session is not null && session.TenantId == tenant.TenantId
        && user.IsAuthenticated && user.UserId is { } id && session.UserId == id && !machine.IsMachineCaller
        && session.Purpose == StorageObjectPurposes.EventResource && session.Visibility == StorageObjectVisibilities.PrivateOwner
        && session.OwningResourceKind == StorageOwningResourceKinds.EventResource && session.OwningResourceId is not null
        && session.ExpectedResourceVersion is not null;

    private async Task<bool> FencedAsync(CancellationToken ct) => user.UserId is { } id
        && await privacy.GetBySubjectAsync(id, ct) is not null;

    private Task<EventResourceAuthorityResult> AuthorizeAsync(Guid id, CancellationToken ct) => authority.AuthorizeAsync(
        new(tenant.TenantId, id, user.IsAuthenticated ? user.UserId : null, machine.IsMachineCaller, "update"),
        (facts, token) => Task.FromResult<IEventResourcePrivatePreparation>(new Preparation(facts.AttachmentGeneration)), ct);

    private sealed class Preparation(string generation) : IEventResourcePrivatePreparation
    {
        public string AttachmentGeneration => generation;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static StorageObject NewStagedObject(StorageUploadSession session)
    {
        var id = Guid.CreateVersion7();
        return new()
        {
            Id = id, TenantId = session.TenantId, Tenant = null!, FileTypeId = (int)FileTypeEnum.Document, FileType = null!,
            Uri = $"/api/storageobject/{id}/content", Provider = session.Provider, ObjectKey = session.ObjectKey,
            FullName = session.SafeDisplayName, SafeDisplayName = session.SafeDisplayName, Extension = session.Extension!,
            ContentType = session.ContentType, Size = session.ExpectedSizeBytes, Purpose = StorageObjectPurposes.EventResource,
            Visibility = StorageObjectVisibilities.PrivateOwner, OwningResourceKind = StorageOwningResourceKinds.EventResource,
            OwningResourceId = session.OwningResourceId, LifecycleState = StorageObjectLifecycleStates.DeleteRequested
        };
    }

    private static BaseCommandResponse<StorageUploadSessionDto> Success(StorageUploadSession session, ResolvedStoragePolicy policy,
        StorageUsageCounter? counter) => BaseCommandResponse.Success(CreateStorageUploadSessionCommandHandler.Map(session, policy, counter));
    private static BaseCommandResponse<StorageUploadSessionDto> Fail(string code) => code switch
    {
        // A typed upload DTO is a success payload, not an identity-only conflict payload.
        FailureCodes.ConcurrencyConflict => BaseCommandResponse.Failure<StorageUploadSessionDto>("event_resource_upload_conflict"),
        FailureCodes.NotFound => BaseCommandResponse.NotFound<StorageUploadSessionDto>(),
        FailureCodes.AuthenticationRequired => BaseCommandResponse.Authentication<StorageUploadSessionDto>(),
        _ => BaseCommandResponse.Failure<StorageUploadSessionDto>(code)
    };
    private static BaseCommandResponse<StorageUploadSessionDto> Denied(EventResourceAuthorityOutcome outcome) => outcome switch
    {
        EventResourceAuthorityOutcome.NotFound => BaseCommandResponse.NotFound<StorageUploadSessionDto>(),
        EventResourceAuthorityOutcome.AuthenticationRequired => BaseCommandResponse.Authentication<StorageUploadSessionDto>(),
        EventResourceAuthorityOutcome.VersionConflict => Fail(FailureCodes.ConcurrencyConflict),
        EventResourceAuthorityOutcome.Forbidden => Fail("event_resource_forbidden"),
        _ => Fail("event_resource_unavailable")
    };
}
