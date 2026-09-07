using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Commands;

public class CancelStorageUploadSessionCommandHandler
    : IRequestHandler<CancelStorageUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>>
{
    private readonly IStoragePolicyResolver _storagePolicyResolver;
    private readonly IStorageUploadSessionRepository _uploadSessionRepository;
    private readonly IStorageUsageCounterRepository _usageCounterRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BusinessMetrics _metrics;

    public CancelStorageUploadSessionCommandHandler(
        IStoragePolicyResolver storagePolicyResolver,
        IStorageUploadSessionRepository uploadSessionRepository,
        IStorageUsageCounterRepository usageCounterRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        BusinessMetrics metrics)
    {
        _storagePolicyResolver = storagePolicyResolver;
        _uploadSessionRepository = uploadSessionRepository;
        _usageCounterRepository = usageCounterRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _metrics = metrics;
    }

    public async Task<BaseCommandResponse<StorageUploadSessionDto>> Handle(
        CancelStorageUploadSessionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UploadSessionId == Guid.Empty)
        {
            _metrics.RecordStorageUploadSession(null, "cancel", "failed", "validation_failed");

            return Failure("Upload session cancellation failed.", ["UploadSessionId is required."]);
        }

        var tenantId = _tenantContext.TenantId;
        var policy = await _storagePolicyResolver.ResolveAsync(tenantId, cancellationToken);

        var response = await _unitOfWork.ExecuteInTransactionAsync(
            async ct => await CancelSessionAsync(request.UploadSessionId, tenantId, policy, ct),
            cancellationToken);

        RecordCancelMetrics(response, policy.Provider);

        return response;
    }

    private void RecordCancelMetrics(BaseCommandResponse<StorageUploadSessionDto> response, string fallbackProvider)
    {
        var provider = response.Id?.Provider ?? fallbackProvider;
        var outcome = response.IsSuccess
            ? (IsAlreadyClosed(response) ? "idempotent" : "succeeded")
            : "failed";

        _metrics.RecordStorageUploadSession(provider, "cancel", outcome, response.FailureCode);

        if (response.IsSuccess && !IsAlreadyClosed(response) && response.Id is not null)
        {
            _metrics.RecordStorageQuotaReservation(provider, "release", "succeeded");
            _metrics.RecordStorageQuotaBytes(response.Id.ReservedBytes, provider, "release", "succeeded");
        }
    }

    private async Task<BaseCommandResponse<StorageUploadSessionDto>> CancelSessionAsync(
        Guid uploadSessionId,
        Guid tenantId,
        Models.Storage.ResolvedStoragePolicy policy,
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

        var counter = await _usageCounterRepository.GetByTenantAndProviderAsync(
            tenantId,
            session.Provider,
            cancellationToken);

        if (session.Status == StorageUploadSessionStates.Finalized)
        {
            return Failure(
                "Finalized upload sessions cannot be canceled.",
                ["Finalized upload sessions cannot be canceled."],
                FailureCodes.StorageUploadSessionFinalized);
        }

        if (session.Status is StorageUploadSessionStates.Canceled
            or StorageUploadSessionStates.Expired
            or StorageUploadSessionStates.Failed)
        {
            return Success(session, policy, counter, "Upload session is already closed.");
        }

        if (counter is not null)
        {
            counter.ReleaseReservation(session.ReservedBytes);
            await _usageCounterRepository.Update(counter);
        }

        var utcNow = DateTime.UtcNow;
        if (session.ExpiresAt <= utcNow)
        {
            session.MarkExpired(utcNow);
        }
        else
        {
            session.Cancel(utcNow);
        }

        await _uploadSessionRepository.Update(session);

        return Success(session, policy, counter, "Upload session canceled successfully.");
    }

    private static BaseCommandResponse<StorageUploadSessionDto> Success(
        StorageUploadSession session,
        Models.Storage.ResolvedStoragePolicy policy,
        StorageUsageCounter? usageCounter,
        string message) =>
        BaseCommandResponse.Success(
            CreateStorageUploadSessionCommandHandler.Map(session, policy, usageCounter),
            message);

    private static BaseCommandResponse<StorageUploadSessionDto> Failure(
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation<StorageUploadSessionDto>(errors, message)
            : BaseCommandResponse.Failure<StorageUploadSessionDto>(failureCode, message, errors);

    private static bool IsAlreadyClosed(BaseCommandResponse<StorageUploadSessionDto> response)
        => response.Message?.Contains("already closed", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsAccessibleSession(StorageUploadSession? session, Guid tenantId)
        => session is not null &&
           session.TenantId == tenantId &&
           _currentUserService.UserId is { } userId &&
           session.UserId == userId;
}
