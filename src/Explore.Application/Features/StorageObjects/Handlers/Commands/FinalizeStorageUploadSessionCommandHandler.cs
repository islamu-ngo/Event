using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.StorageObject.Validators;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Commands;

public class FinalizeStorageUploadSessionCommandHandler
    : IRequestHandler<FinalizeStorageUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>>
{
    private const string PrivacyErasureFencedFailureCode = "privacy_erasure_fenced";

    private readonly IFileStorageProviderResolver _providerResolver;
    private readonly IStoragePolicyResolver _storagePolicyResolver;
    private readonly IStorageUploadSessionRepository _uploadSessionRepository;
    private readonly IStorageUsageCounterRepository _usageCounterRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IPrivacyErasureStateRepository _privacyErasureStateRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BusinessMetrics _metrics;

    public FinalizeStorageUploadSessionCommandHandler(
        IFileStorageProviderResolver providerResolver,
        IStoragePolicyResolver storagePolicyResolver,
        IStorageUploadSessionRepository uploadSessionRepository,
        IStorageUsageCounterRepository usageCounterRepository,
        IStorageObjectRepository storageObjectRepository,
        IPrivacyErasureStateRepository privacyErasureStateRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        BusinessMetrics metrics)
    {
        _providerResolver = providerResolver;
        _storagePolicyResolver = storagePolicyResolver;
        _uploadSessionRepository = uploadSessionRepository;
        _usageCounterRepository = usageCounterRepository;
        _storageObjectRepository = storageObjectRepository;
        _privacyErasureStateRepository = privacyErasureStateRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _metrics = metrics;
    }

    public async Task<BaseCommandResponse<StorageUploadSessionDto>> Handle(
        FinalizeStorageUploadSessionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UploadSessionId == Guid.Empty)
        {
            _metrics.RecordStorageUploadSession(null, "finalize", "failed", "validation_failed");

            return Failure("Upload finalization failed.", ["UploadSessionId is required."]);
        }

        if (request.Content is null || !request.Content.CanRead)
        {
            _metrics.RecordStorageUploadSession(null, "finalize", "failed", "validation_failed");

            return Failure("Upload finalization failed.", ["A readable upload content stream is required."]);
        }

        Guid? userId = _currentUserService.UserId;
        if (await IsFencedAsync(userId, cancellationToken))
        {
            return FencedFailure();
        }

        var tenantId = _tenantContext.TenantId;

        var sessionResponse = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => await MarkUploadingAsync(request, tenantId, ct),
            cancellationToken);

        if (!sessionResponse.IsSuccess || sessionResponse.Id is null)
        {
            RecordFinalizeFailure(sessionResponse, null);
            return sessionResponse;
        }

        if (sessionResponse.Id.Status == StorageUploadSessionStates.Finalized)
        {
            _metrics.RecordStorageUploadSession(sessionResponse.Id.Provider, "finalize", "idempotent");
            return sessionResponse;
        }

        var session = await _uploadSessionRepository.GetByIdForUpdateAsync(request.UploadSessionId, cancellationToken);
        if (!IsAccessibleSession(session, tenantId))
        {
            var failure = Failure(
                "Upload session was not found.",
                ["Upload session was not found."],
                FailureCodes.StorageUploadSessionNotFound);
            _metrics.RecordStorageUploadSession(null, "finalize", "failed", failure.FailureCode);
            return failure;
        }

        Stream? contentForProvider = null;
        try
        {
            var contentInspection = await StorageContentSignaturePolicy.InspectAsync(
                request.Content,
                session.ContentType,
                session.Extension,
                session.ExpectedSizeBytes,
                cancellationToken);

            contentForProvider = contentInspection.Content;
            if (!contentInspection.Success)
            {
                var failure = await _unitOfWork.ExecuteInTransactionAsync(
                    async ct => await FailSessionAsync(
                        session.Id,
                        tenantId,
                        FailureCodes.StorageUploadContentSignatureMismatch,
                        "Upload content did not match the reserved content policy.",
                        contentInspection.Errors,
                        ct),
                    cancellationToken);

                _metrics.RecordStorageUploadSession(session.Provider, "finalize", "failed", failure.FailureCode);
                _metrics.RecordStorageUploadBytes(session.ExpectedSizeBytes, session.Provider, "failed", failure.FailureCode);
                _metrics.RecordStorageQuotaReservation(session.Provider, "release", "succeeded");
                _metrics.RecordStorageQuotaBytes(session.ReservedBytes, session.Provider, "release", "succeeded");

                return failure;
            }

            if (await IsFencedAsync(session.UserId, cancellationToken))
            {
                await FailFencedSessionAsync(session.Id, tenantId, cancellationToken);
                return FencedFailure();
            }

            var provider = _providerResolver.GetRequired(session.Provider);
            var writeResult = await provider.WriteAsync(
                new FileStorageWriteInput(
                    tenantId,
                    contentForProvider,
                    session.ContentType,
                    session.SafeDisplayName,
                    session.Extension,
                    session.ExpectedSizeBytes,
                    session.ExpectedSizeBytes,
                    session.ObjectKey),
                cancellationToken);

            if (await IsFencedAsync(session.UserId, cancellationToken))
            {
                await FailFencedSessionAsync(session.Id, tenantId, cancellationToken);
                return FencedFailure();
            }

            var writeResultErrors = ValidateWriteResult(session, tenantId, writeResult);
            if (writeResultErrors.Count > 0)
            {
                var failure = await _unitOfWork.ExecuteInTransactionAsync(
                    async ct => await FailSessionAsync(
                        session.Id,
                        tenantId,
                        FailureCodes.StorageUploadWriteFailed,
                        "Storage provider returned invalid upload metadata.",
                        writeResultErrors,
                        ct),
                    cancellationToken);

                _metrics.RecordStorageUploadSession(session.Provider, "finalize", "failed", failure.FailureCode);
                _metrics.RecordStorageUploadBytes(session.ExpectedSizeBytes, session.Provider, "failed", failure.FailureCode);
                _metrics.RecordStorageQuotaReservation(session.Provider, "release", "succeeded");
                _metrics.RecordStorageQuotaBytes(session.ReservedBytes, session.Provider, "release", "succeeded");

                return failure;
            }

            var response = await _unitOfWork.ExecuteInTransactionAsync(
                async ct => await FinalizeAsync(session.Id, tenantId, writeResult, ct),
                cancellationToken);

            if (response.IsSuccess)
            {
                _metrics.RecordStorageUploadSession(writeResult.Provider, "finalize", "succeeded");
                _metrics.RecordStorageUploadBytes(writeResult.SizeBytes, writeResult.Provider, "succeeded");
                _metrics.RecordStorageQuotaReservation(writeResult.Provider, "commit", "succeeded");
                _metrics.RecordStorageQuotaBytes(writeResult.SizeBytes, writeResult.Provider, "commit", "succeeded");
            }
            else
            {
                _metrics.RecordStorageUploadSession(session.Provider, "finalize", "failed", response.FailureCode);
            }

            return response;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            var response = await _unitOfWork.ExecuteInTransactionAsync(
                async ct => await FailSessionAsync(
                    session.Id,
                    tenantId,
                    FailureCodes.StorageUploadWriteFailed,
                    "Storage provider could not accept the upload.",
                    null,
                    ct),
                cancellationToken);

            _metrics.RecordStorageUploadSession(session.Provider, "finalize", "failed", response.FailureCode);
            _metrics.RecordStorageUploadBytes(session.ExpectedSizeBytes, session.Provider, "failed", response.FailureCode);
            _metrics.RecordStorageQuotaReservation(session.Provider, "release", "succeeded");
            _metrics.RecordStorageQuotaBytes(session.ReservedBytes, session.Provider, "release", "succeeded");

            return response;
        }
        finally
        {
            if (contentForProvider is not null && !ReferenceEquals(contentForProvider, request.Content))
            {
                await contentForProvider.DisposeAsync();
            }
        }
    }

    private void RecordFinalizeFailure(
        BaseCommandResponse<StorageUploadSessionDto> response,
        string? fallbackProvider)
    {
        var provider = response.Id?.Provider ?? fallbackProvider;
        _metrics.RecordStorageUploadSession(provider, "finalize", "failed", response.FailureCode);

        if (response.FailureCode == FailureCodes.StorageUploadSessionExpired && response.Id is not null)
        {
            _metrics.RecordStorageQuotaReservation(provider, "release", "succeeded");
            _metrics.RecordStorageQuotaBytes(response.Id.ReservedBytes, provider, "release", "succeeded");
        }
    }

    private async Task<BaseCommandResponse<StorageUploadSessionDto>> MarkUploadingAsync(
        FinalizeStorageUploadSessionCommand request,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var session = await _uploadSessionRepository.GetByIdForUpdateAsync(request.UploadSessionId, cancellationToken);
        if (!IsAccessibleSession(session, tenantId))
        {
            return Failure(
                "Upload session was not found.",
                ["Upload session was not found."],
                FailureCodes.StorageUploadSessionNotFound);
        }

        var counter = await _usageCounterRepository.GetByTenantAndProviderAsync(
            tenantId,
            session.Provider,
            cancellationToken);

        if (session.Status == StorageUploadSessionStates.Finalized)
        {
            return Success(session, counter, "Upload session is already finalized.");
        }

        if (session.Status != StorageUploadSessionStates.Reserved)
        {
            return Failure(
                "Upload session cannot accept bytes in its current state.",
                [$"Upload session status is {session.Status}."],
                FailureCodes.StorageUploadSessionInvalidState);
        }

        var utcNow = DateTime.UtcNow;
        if (session.ExpiresAt <= utcNow)
        {
            if (counter is not null)
            {
                counter.ReleaseReservation(session.ReservedBytes);
                await _usageCounterRepository.Update(counter);
            }

            session.MarkExpired(utcNow);
            await _uploadSessionRepository.Update(session);

            return Failure(
                "Upload session has expired.",
                ["Upload session has expired."],
                FailureCodes.StorageUploadSessionExpired);
        }

        if (request.ContentLength is { } contentLength && contentLength != session.ExpectedSizeBytes)
        {
            return Failure(
                "Upload content length does not match the reserved byte count.",
                [$"ContentLength must equal {session.ExpectedSizeBytes} bytes."],
                FailureCodes.StorageUploadSizeMismatch);
        }

        if (!string.IsNullOrWhiteSpace(request.ContentType) &&
            !string.Equals(request.ContentType.Trim(), session.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                "Upload content type does not match the reserved content type.",
                [$"ContentType must equal {session.ContentType}."],
                FailureCodes.StorageUploadContentTypeMismatch);
        }

        session.ReserveObjectKey(BuildReservedObjectKey(session));
        session.MarkUploading(utcNow);
        await _uploadSessionRepository.Update(session);

        return Success(session, counter, "Upload session is ready to accept bytes.");
    }

    private async Task<BaseCommandResponse<StorageUploadSessionDto>> FinalizeAsync(
        Guid uploadSessionId,
        Guid tenantId,
        FileStorageWriteResult writeResult,
        CancellationToken cancellationToken)
    {
        var session = await _uploadSessionRepository.GetByIdForUpdateAsync(uploadSessionId, cancellationToken);
        if (!IsAccessibleSession(session, tenantId))
        {
            return Failure(
                "Upload session was not found.",
                ["Upload session was not found."],
                FailureCodes.StorageUploadSessionNotFound);
        }

        var counter = await _usageCounterRepository.GetOrCreateAsync(tenantId, session.Provider, cancellationToken);
        if (session.Status == StorageUploadSessionStates.Finalized)
        {
            return Success(session, counter, "Upload session is already finalized.");
        }

        if (session.Status != StorageUploadSessionStates.Uploading)
        {
            return Failure(
                "Upload session cannot be finalized in its current state.",
                [$"Upload session status is {session.Status}."],
                FailureCodes.StorageUploadSessionInvalidState);
        }

        var storageObject = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            FileTypeId = ResolveFileTypeId(session.ContentType, session.Extension),
            FileType = null!,
            Uri = $"/api/storageobject/{session.Id}/content",
            ObjectKey = writeResult.ObjectKey,
            Provider = session.Provider,
            FullName = session.SafeDisplayName,
            SafeDisplayName = session.SafeDisplayName,
            Extension = ResolveRequiredExtension(session.Extension, session.SafeDisplayName),
            ContentType = session.ContentType,
            Sha256Checksum = writeResult.Sha256Checksum!,
            Size = writeResult.SizeBytes,
            Visibility = session.Visibility,
            Purpose = session.Purpose,
            LifecycleState = StorageObjectLifecycleStates.Active,
            OwningResourceKind = session.OwningResourceKind,
            OwningResourceId = session.OwningResourceId,
            TenantId = tenantId,
            Tenant = null!,
            ActorId = null
        };
        storageObject.Uri = $"/api/storageobject/{storageObject.Id}/content";

        storageObject = await _storageObjectRepository.Create(storageObject);
        counter.FinalizeReservation(writeResult.SizeBytes);
        await _usageCounterRepository.Update(counter);

        session.Finalize(storageObject.Id, writeResult.ObjectKey, writeResult.Sha256Checksum!, DateTime.UtcNow);
        session.StorageObject = storageObject;
        await _uploadSessionRepository.Update(session);

        return Success(session, counter, "Upload session finalized successfully.");
    }

    private async Task<BaseCommandResponse<StorageUploadSessionDto>> FailSessionAsync(
        Guid uploadSessionId,
        Guid tenantId,
        string failureCode,
        string failureMessage,
        IReadOnlyList<string>? errors,
        CancellationToken cancellationToken)
    {
        var session = await _uploadSessionRepository.GetByIdForUpdateAsync(uploadSessionId, cancellationToken);
        if (!IsAccessibleSession(session, tenantId))
        {
            return Failure("Upload session was not found.", ["Upload session was not found."], FailureCodes.StorageUploadSessionNotFound);
        }

        var counter = await _usageCounterRepository.GetByTenantAndProviderAsync(
            tenantId,
            session.Provider,
            cancellationToken);

        if (session.Status != StorageUploadSessionStates.Finalized)
        {
            if (counter is not null)
            {
                counter.ReleaseReservation(session.ReservedBytes);
                await _usageCounterRepository.Update(counter);
            }

            session.Fail(failureCode, failureMessage, DateTime.UtcNow);
            await _uploadSessionRepository.Update(session);
        }

        return BaseCommandResponse.Failure(
            failureCode,
            failureMessage,
            errors is { Count: > 0 } ? errors : [failureMessage],
            CreateStorageUploadSessionCommandHandler.Map(session, CreatePolicyFromSession(session), counter));
    }

    private static IReadOnlyList<string> ValidateWriteResult(
        StorageUploadSession session,
        Guid tenantId,
        FileStorageWriteResult writeResult)
    {
        var errors = new List<string>();

        if (!string.Equals(writeResult.Provider, session.Provider, StringComparison.Ordinal))
        {
            errors.Add("Storage provider result did not match the reserved provider.");
        }

        var expectedObjectKeyPrefix = $"tenants/{tenantId:N}/";
        if (string.IsNullOrWhiteSpace(writeResult.ObjectKey) ||
            !StorageObjectMetadataValidation.BeValidObjectKey(writeResult.ObjectKey) ||
            !writeResult.ObjectKey.StartsWith(expectedObjectKeyPrefix, StringComparison.Ordinal) ||
            !string.Equals(writeResult.ObjectKey, session.ObjectKey, StringComparison.Ordinal))
        {
            errors.Add("Storage provider returned an invalid object key.");
        }

        if (writeResult.SizeBytes != session.ExpectedSizeBytes)
        {
            errors.Add("Storage provider byte count did not match the reserved byte count.");
        }

        if (!string.Equals(writeResult.ContentType?.Trim(), session.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Storage provider content type did not match the reserved content type.");
        }

        if (string.IsNullOrWhiteSpace(writeResult.Sha256Checksum) ||
            !StorageObjectMetadataValidation.BeValidSha256HexDigest(writeResult.Sha256Checksum))
        {
            errors.Add("Storage provider checksum was missing or invalid.");
        }

        return errors;
    }

    private static BaseCommandResponse<StorageUploadSessionDto> Success(
        StorageUploadSession session,
        StorageUsageCounter? usageCounter,
        string message) =>
        BaseCommandResponse.Success(
            CreateStorageUploadSessionCommandHandler.Map(session, CreatePolicyFromSession(session), usageCounter),
            message);

    private static ResolvedStoragePolicy CreatePolicyFromSession(StorageUploadSession session)
    {
        var maxUploadBytes = session.PolicyMaxUploadBytes > 0
            ? session.PolicyMaxUploadBytes
            : session.ExpectedSizeBytes;
        var routeKey = string.IsNullOrWhiteSpace(session.RouteKey) ? StorageRouteKeys.General : session.RouteKey;
        var route = new ResolvedStorageRoutePolicy(
            routeKey,
            session.Provider,
            maxUploadBytes,
            SettingSource.SystemDefault,
            SettingSource.SystemDefault);

        _ = int.TryParse(session.PolicyVersion, out var policyVersion);

        return new ResolvedStoragePolicy(
            session.TenantId,
            session.Provider,
            maxUploadBytes,
            0,
            maxUploadBytes,
            TenantOverridesAllowed: false,
            TenantStorageLocked: true,
            ProviderSource: SettingSource.SystemDefault,
            MaxUploadSource: SettingSource.SystemDefault,
            QuotaSource: SettingSource.SystemDefault,
            routeKey,
            policyVersion <= 0 ? 1 : policyVersion,
            [route],
            route);
    }

    private static BaseCommandResponse<StorageUploadSessionDto> Failure(
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation<StorageUploadSessionDto>(errors, message)
            : BaseCommandResponse.Failure<StorageUploadSessionDto>(failureCode, message, errors);

    private static int ResolveFileTypeId(string contentType, string? extension)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return (int)FileTypeEnum.Image;
        }

        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return (int)FileTypeEnum.Video;
        }

        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return (int)FileTypeEnum.Audio;
        }

        var normalizedExtension = extension?.TrimStart('.').ToLowerInvariant();
        if (normalizedExtension is "pdf" or "doc" or "docx" or "txt" or "rtf" or "odt")
        {
            return (int)FileTypeEnum.Document;
        }

        return (int)FileTypeEnum.Other;
    }

    private static string ResolveRequiredExtension(string? extension, string safeDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.TrimStart('.').ToLowerInvariant();
        }

        var dotIndex = safeDisplayName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < safeDisplayName.Length - 1
            ? safeDisplayName[(dotIndex + 1)..].ToLowerInvariant()
            : "bin";
    }

    private static string BuildReservedObjectKey(StorageUploadSession session) =>
        $"tenants/{session.TenantId:N}/uploads/{session.Id:N}.{ResolveRequiredExtension(session.Extension, session.SafeDisplayName)}";

    private async Task<bool> IsFencedAsync(Guid? userId, CancellationToken cancellationToken) =>
        userId is Guid subjectId &&
        await _privacyErasureStateRepository.GetBySubjectAsync(subjectId, cancellationToken) is not null;

    private async Task FailFencedSessionAsync(
        Guid uploadSessionId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        _ = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => await FailSessionAsync(
                uploadSessionId,
                tenantId,
                PrivacyErasureFencedFailureCode,
                "Upload finalization was blocked by privacy erasure.",
                null,
                ct),
            cancellationToken);
    }

    private static BaseCommandResponse<StorageUploadSessionDto> FencedFailure() =>
        BaseCommandResponse.Failure<StorageUploadSessionDto>(
            PrivacyErasureFencedFailureCode,
            "Upload finalization failed.");

    private bool IsAccessibleSession(StorageUploadSession? session, Guid tenantId)
        => session is not null &&
           session.TenantId == tenantId &&
           _currentUserService.UserId is { } userId &&
           session.UserId == userId;
}
