using System.Globalization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.StorageObject.Validators;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Commands;

public class CreateStorageUploadSessionCommandHandler
    : IRequestHandler<CreateStorageUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>>
{
    private const string PrivacyErasureFencedFailureCode = "privacy_erasure_fenced";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);

    private readonly IStoragePolicyResolver _storagePolicyResolver;
    private readonly IStorageUploadSessionRepository _uploadSessionRepository;
    private readonly IStorageUsageCounterRepository _usageCounterRepository;
    private readonly IPrivacyErasureStateRepository _privacyErasureStateRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BusinessMetrics _metrics;

    public CreateStorageUploadSessionCommandHandler(
        IStoragePolicyResolver storagePolicyResolver,
        IStorageUploadSessionRepository uploadSessionRepository,
        IStorageUsageCounterRepository usageCounterRepository,
        IPrivacyErasureStateRepository privacyErasureStateRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        BusinessMetrics metrics)
    {
        _storagePolicyResolver = storagePolicyResolver;
        _uploadSessionRepository = uploadSessionRepository;
        _usageCounterRepository = usageCounterRepository;
        _privacyErasureStateRepository = privacyErasureStateRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _metrics = metrics;
    }

    public async Task<BaseCommandResponse<StorageUploadSessionDto>> Handle(
        CreateStorageUploadSessionCommand request,
        CancellationToken cancellationToken)
    {
        Guid? userId = _currentUserService.UserId;
        if (await IsFencedAsync(userId, cancellationToken))
        {
            return FencedFailure();
        }

        var validator = new CreateStorageUploadSessionDtoValidator();
        var validationResult = await validator.ValidateAsync(request.UploadSessionDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            _metrics.RecordStorageUploadSession(null, "create", "failed", "validation_failed");

            return await MaskIfFencedAsync(
                userId,
                Failure(
                    "Upload session reservation failed.",
                    validationResult.Errors.Select(error => error.ErrorMessage)),
                cancellationToken);
        }

        var tenantId = _tenantContext.TenantId;
        var policy = await _storagePolicyResolver.ResolveAsync(
            tenantId,
            CreatePolicyRequest(request.UploadSessionDto),
            cancellationToken);

        if (request.UploadSessionDto.ExpectedSizeBytes > policy.MaxUploadBytes)
        {
            _metrics.RecordStorageUploadSession(
                policy.Provider,
                "create",
                "failed",
                FailureCodes.StorageUploadTooLarge);

            return await MaskIfFencedAsync(
                userId,
                Failure(
                    "Upload exceeds the configured per-file limit.",
                    [
                        $"ExpectedSizeBytes must be less than or equal to {policy.MaxUploadBytes} bytes."
                    ],
                    FailureCodes.StorageUploadTooLarge),
                cancellationToken);
        }

        var response = await _unitOfWork.ExecuteSerializableAsync(
            async ct => await ReserveSessionAsync(request.UploadSessionDto, tenantId, userId, policy, ct),
            cancellationToken);

        RecordCreateMetrics(response, policy.Provider, request.UploadSessionDto.ExpectedSizeBytes);

        return response;
    }

    private void RecordCreateMetrics(
        BaseCommandResponse<StorageUploadSessionDto> response,
        string provider,
        long expectedSizeBytes)
    {
        var idempotentReplay = IsIdempotentReplay(response);
        var outcome = response.IsSuccess
            ? (idempotentReplay ? "idempotent" : "succeeded")
            : "failed";

        _metrics.RecordStorageUploadSession(provider, "create", outcome, response.FailureCode);

        if (response.IsSuccess && !idempotentReplay)
        {
            _metrics.RecordStorageQuotaReservation(provider, "reserve", "succeeded");
            _metrics.RecordStorageQuotaBytes(expectedSizeBytes, provider, "reserve", "succeeded");
            return;
        }

        if (!response.IsSuccess && response.FailureCode == FailureCodes.QuotaExceeded)
        {
            _metrics.RecordStorageQuotaReservation(provider, "reserve", "failed", response.FailureCode);
        }
    }

    private async Task<BaseCommandResponse<StorageUploadSessionDto>> ReserveSessionAsync(
        CreateStorageUploadSessionDto dto,
        Guid tenantId,
        Guid? userId,
        ResolvedStoragePolicy policy,
        CancellationToken cancellationToken)
    {
        if (await IsFencedAsync(userId, cancellationToken))
        {
            return FencedFailure();
        }

        var idempotencyKey = dto.IdempotencyKey.Trim();
        var existing = await _uploadSessionRepository.GetByTenantAndIdempotencyKeyForUpdateAsync(
            tenantId,
            idempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            var existingCounter = await _usageCounterRepository.GetByTenantAndProviderAsync(
                tenantId,
                existing.Provider,
                cancellationToken);

            return Success(
                existing,
                policy,
                existingCounter,
                "Upload session already exists for this idempotency key.");
        }

        var usageCounter = await _usageCounterRepository.GetOrCreateAsync(
            tenantId,
            policy.Provider,
            cancellationToken);
        var usageBeforeReserve = await GetTenantUsageSnapshotAsync(tenantId, usageCounter, cancellationToken);

        if (!CanReserve(usageBeforeReserve, dto.ExpectedSizeBytes, policy.TenantQuotaBytes))
        {
            var error = $"Tenant storage quota is {policy.TenantQuotaBytes} bytes; used={usageBeforeReserve.UsedBytes}, reserved={usageBeforeReserve.ReservedBytes}, attempted={dto.ExpectedSizeBytes}.";
            return BaseCommandResponse.Quota<StorageUploadSessionDto>(
                "Upload would exceed the tenant storage quota.",
                new QuotaExceededDetails(
                    GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
                    ToQuotaDetailValue(policy.TenantQuotaBytes),
                    ToQuotaDetailValue(usageBeforeReserve.UsedBytes + usageBeforeReserve.ReservedBytes),
                    ToQuotaDetailValue(usageBeforeReserve.UsedBytes + usageBeforeReserve.ReservedBytes + dto.ExpectedSizeBytes),
                    "tenant",
                    tenantId),
                error);
        }

        usageCounter.Reserve(dto.ExpectedSizeBytes, policy.TenantQuotaBytes);
        await _usageCounterRepository.Update(usageCounter);

        var usageAfterReserve = usageBeforeReserve with
        {
            ReservedBytes = usageBeforeReserve.ReservedBytes + dto.ExpectedSizeBytes
        };

        var utcNow = DateTime.UtcNow;
        var session = new StorageUploadSession
        {
            TenantId = tenantId,
            UserId = userId,
            Provider = policy.Provider,
            RouteKey = policy.RouteKey,
            PolicyMaxUploadBytes = policy.MaxUploadBytes,
            PolicyVersion = policy.PolicyVersion.ToString(CultureInfo.InvariantCulture),
            ExpectedSizeBytes = dto.ExpectedSizeBytes,
            ReservedBytes = dto.ExpectedSizeBytes,
            ContentType = NormalizeContentType(dto.ContentType),
            OriginalFileName = NormalizeOptional(dto.OriginalFileName),
            SafeDisplayName = ResolveSafeDisplayName(dto),
            Extension = ResolveExtension(dto),
            Purpose = dto.Purpose,
            Visibility = dto.Visibility,
            OwningResourceKind = NormalizeOptional(dto.OwningResourceKind),
            OwningResourceId = dto.OwningResourceId,
            Status = StorageUploadSessionStates.Reserved,
            IdempotencyKey = idempotencyKey,
            ExpiresAt = utcNow.Add(SessionLifetime)
        };

        session = await _uploadSessionRepository.Create(session);

        return Success(session, policy, usageCounter, "Upload session reserved successfully.", usageAfterReserve);
    }

    private async Task<bool> IsFencedAsync(Guid? userId, CancellationToken cancellationToken) =>
        userId is Guid subjectId &&
        await _privacyErasureStateRepository.GetBySubjectAsync(subjectId, cancellationToken) is not null;

    private async Task<BaseCommandResponse<StorageUploadSessionDto>> MaskIfFencedAsync(
        Guid? userId,
        BaseCommandResponse<StorageUploadSessionDto> response,
        CancellationToken cancellationToken) =>
        await IsFencedAsync(userId, cancellationToken) ? FencedFailure() : response;

    private async Task<(long UsedBytes, long ReservedBytes)> GetTenantUsageSnapshotAsync(
        Guid tenantId,
        StorageUsageCounter selectedCounter,
        CancellationToken cancellationToken)
    {
        var counters = await _usageCounterRepository.GetByTenantAsync(tenantId, cancellationToken);
        var usedBytes = selectedCounter.UsedBytes;
        var reservedBytes = selectedCounter.ReservedBytes;

        foreach (var counter in counters.Where(counter => counter.Provider != selectedCounter.Provider))
        {
            usedBytes += counter.UsedBytes;
            reservedBytes += counter.ReservedBytes;
        }

        return (usedBytes, reservedBytes);
    }

    private static bool CanReserve((long UsedBytes, long ReservedBytes) usage, long attemptedBytes, long quotaBytes)
        => usage.UsedBytes + usage.ReservedBytes + attemptedBytes <= quotaBytes;

    private static int ToQuotaDetailValue(long value) =>
        value >= int.MaxValue ? int.MaxValue : checked((int)value);

    private static BaseCommandResponse<StorageUploadSessionDto> Success(
        StorageUploadSession session,
        ResolvedStoragePolicy policy,
        StorageUsageCounter? usageCounter,
        string message,
        (long UsedBytes, long ReservedBytes)? usageSnapshot = null) =>
        BaseCommandResponse.Success(
            Map(session, policy, usageCounter, usageSnapshot),
            message);

    private static BaseCommandResponse<StorageUploadSessionDto> Failure(
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation<StorageUploadSessionDto>(errors, message)
            : BaseCommandResponse.Failure<StorageUploadSessionDto>(failureCode, message, errors);

    private static BaseCommandResponse<StorageUploadSessionDto> FencedFailure() =>
        BaseCommandResponse.Failure<StorageUploadSessionDto>(
            PrivacyErasureFencedFailureCode,
            "Upload session reservation failed.");

    internal static StorageUploadSessionDto Map(
        StorageUploadSession session,
        ResolvedStoragePolicy policy,
        StorageUsageCounter? usageCounter,
        (long UsedBytes, long ReservedBytes)? usageSnapshot = null)
    {
        var maxUploadBytes = session.PolicyMaxUploadBytes > 0
            ? session.PolicyMaxUploadBytes
            : policy.MaxUploadBytes;

        return new StorageUploadSessionDto
        {
            Id = session.Id,
            TenantId = session.TenantId,
            UserId = session.UserId,
            Provider = session.Provider,
            RouteKey = session.RouteKey,
            PolicyMaxUploadBytes = maxUploadBytes,
            PolicyVersion = session.PolicyVersion,
            ExpectedSizeBytes = session.ExpectedSizeBytes,
            ReservedBytes = session.ReservedBytes,
            ContentType = session.ContentType,
            OriginalFileName = session.OriginalFileName,
            SafeDisplayName = session.SafeDisplayName,
            Extension = session.Extension,
            Purpose = session.Purpose,
            Visibility = session.Visibility,
            Status = session.Status,
            IdempotencyKey = session.IdempotencyKey,
            StorageObjectId = session.StorageObjectId,
            StoredSizeBytes = session.StorageObject?.Size,
            Sha256Checksum = session.Sha256Checksum,
            ExpiresAt = session.ExpiresAt,
            UploadStartedAt = session.UploadStartedAt,
            FinalizedAt = session.FinalizedAt,
            CanceledAt = session.CanceledAt,
            FailedAt = session.FailedAt,
            MaxUploadBytes = maxUploadBytes,
            TenantQuotaBytes = policy.TenantQuotaBytes,
            UsedBytes = usageSnapshot?.UsedBytes ?? usageCounter?.UsedBytes ?? 0,
            TotalReservedBytes = usageSnapshot?.ReservedBytes ?? usageCounter?.ReservedBytes ?? 0
        };
    }

    private static StoragePolicyIntent CreatePolicyRequest(CreateStorageUploadSessionDto dto)
        => new(
            dto.Purpose,
            dto.Visibility,
            NormalizeContentType(dto.ContentType),
            dto.OwningResourceKind,
            dto.OwningResourceId,
            dto.ExpectedSizeBytes);

    private static string NormalizeContentType(string contentType)
        => contentType.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsIdempotentReplay(BaseCommandResponse<StorageUploadSessionDto> response)
        => response.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true;

    private static string ResolveSafeDisplayName(CreateStorageUploadSessionDto dto)
        => NormalizeOptional(dto.SafeDisplayName)
            ?? NormalizeOptional(dto.OriginalFileName)
            ?? "upload";

    private static string? ResolveExtension(CreateStorageUploadSessionDto dto)
    {
        var extension = NormalizeOptional(dto.Extension);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.TrimStart('.').ToLowerInvariant();
        }

        var fileName = NormalizeOptional(dto.OriginalFileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..].ToLowerInvariant()
            : null;
    }
}
