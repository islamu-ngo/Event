using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Storage;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Services.Registration;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Services;

public sealed class StorageObjectContentReader : IStorageObjectContentReader
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IFileStorageProviderResolver _providerResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<StorageObjectContentReader> _logger;
    private readonly BusinessMetrics _metrics;
    private readonly TimeProvider _timeProvider;

    public StorageObjectContentReader(
        IStorageObjectRepository storageObjectRepository,
        IFileStorageProviderResolver providerResolver,
        ICurrentUserService currentUserService,
        ILogger<StorageObjectContentReader> logger,
        BusinessMetrics metrics,
        TimeProvider? timeProvider = null)
    {
        _storageObjectRepository = storageObjectRepository;
        _providerResolver = providerResolver;
        _currentUserService = currentUserService;
        _logger = logger;
        _metrics = metrics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StorageObjectContentResult?> OpenAsync(
        Guid storageObjectId,
        bool publicImagesOnly,
        CancellationToken cancellationToken)
    {
        if (storageObjectId == Guid.Empty)
        {
            _metrics.RecordStorageRead(null, "failed", "validation_failed", null);
            return null;
        }

        var storageObject = await _storageObjectRepository.GetById(storageObjectId);
        if (storageObject is null)
        {
            _metrics.RecordStorageRead(null, "failed", "metadata_not_found", null);
            return null;
        }

        if (await _storageObjectRepository.IsRegistrationAnswerFileQuarantinedAsync(storageObjectId, cancellationToken))
        {
            _metrics.RecordStorageRead(storageObject.Provider, "failed", "registration_file_quarantined", storageObject.Visibility);
            return null;
        }

        if (!CanRead(storageObject, publicImagesOnly))
        {
            _metrics.RecordStorageRead(
                storageObject.Provider,
                "failed",
                "access_denied",
                storageObject.Visibility);

            return null;
        }

        RegistrationAnswerFile? answerFile = await _storageObjectRepository.GetRegistrationAnswerFileAsync(
            storageObject.Id, storageObject.TenantId, cancellationToken);
        bool registrationOwned = IsRegistrationOwned(storageObject, answerFile);
        RegistrationOrder? order = registrationOwned
            ? await _storageObjectRepository.GetRegistrationContentOrderAsync(storageObject, answerFile, cancellationToken)
            : null;
        if (!CanDiscloseRegistrationContent(storageObject, answerFile, order, registrationOwned,
                _timeProvider.GetUtcNow().UtcDateTime))
        {
            _metrics.RecordStorageRead(storageObject.Provider, "failed", "registration_data_retention_expired", storageObject.Visibility);
            return null;
        }

        if (string.IsNullOrWhiteSpace(storageObject.ObjectKey))
        {
            _logger.LogWarning("Storage object {StorageObjectId} has no provider object key.", storageObjectId);
            _metrics.RecordStorageRead(
                storageObject.Provider,
                "failed",
                "missing_object_key",
                storageObject.Visibility);

            return null;
        }

        try
        {
            var provider = _providerResolver.GetRequired(storageObject.Provider);
            var readResult = await provider.OpenReadAsync(
                new FileStorageReadInput(storageObject.ObjectKey, storageObject.ContentType),
                cancellationToken);

            if (!CanDiscloseRegistrationContent(storageObject, answerFile, order, registrationOwned,
                    _timeProvider.GetUtcNow().UtcDateTime))
            {
                await readResult.Content.DisposeAsync();
                _metrics.RecordStorageRead(storageObject.Provider, "failed", "registration_data_retention_expired", storageObject.Visibility);
                return null;
            }

            _metrics.RecordStorageRead(
                storageObject.Provider,
                "succeeded",
                null,
                storageObject.Visibility);
            _metrics.RecordStorageReadBytes(
                readResult.Length,
                storageObject.Provider,
                "succeeded",
                storageObject.Visibility);

            return new StorageObjectContentResult(
                readResult.Content,
                storageObject.ContentType ?? "application/octet-stream",
                readResult.Length,
                readResult.LastModified,
                storageObject.Sha256Checksum,
                ResolveSafeDisplayName(storageObject),
                !SafeRasterContentPolicy.IsSafeRasterMetadata(
                    storageObject.ContentType,
                    storageObject.Extension));
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning(
                "Storage provider object was not found for storage object {StorageObjectId}. FailureType={FailureType}",
                storageObjectId,
                "object_not_found");
            _metrics.RecordStorageRead(
                storageObject.Provider,
                "failed",
                "object_not_found",
                storageObject.Visibility);

            return null;
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning(
                "Storage provider {Provider} is unavailable for storage object {StorageObjectId}. FailureType={FailureType}",
                storageObject.Provider,
                storageObjectId,
                "provider_unavailable");
            _metrics.RecordStorageRead(
                storageObject.Provider,
                "failed",
                "provider_unavailable",
                storageObject.Visibility);

            return null;
        }
    }

    internal static bool IsRegistrationOwned(StorageObject storageObject, RegistrationAnswerFile? answerFile) =>
        answerFile is not null || storageObject.RegistrationContentRetentionUntilUtc is not null ||
        storageObject.OwningResourceKind is "registration_submission_sink" or RegistrationAnswerFileStorageOwnership.ResourceKind;

    internal static bool CanDiscloseRegistrationContent(
        StorageObject storageObject, RegistrationAnswerFile? answerFile, RegistrationOrder? order,
        bool registrationOwned, DateTime utcNow)
    {
        if (storageObject.RegistrationContentRetentionUntilUtc is { } deadline && utcNow >= deadline)
            return false;
        if (!registrationOwned) return true;
        if (order is null || answerFile is { IsDeleted: true } ||
            answerFile is not null && !answerFile.IsReleased)
            return false;
        if (AnonymousRegistrationRetentionPolicy.AppliesTo(order) &&
            storageObject.OwningResourceKind == "registration_submission_sink" &&
            storageObject.RegistrationContentRetentionUntilUtc is null)
            return false;
        return AnonymousRegistrationRetentionPolicy.CanDisclose(
            order, storageObject.RegistrationContentRetentionUntilUtc, utcNow);
    }

    private bool CanRead(StorageObject storageObject, bool publicImagesOnly)
    {
        if (!string.Equals(storageObject.LifecycleState, StorageObjectLifecycleStates.Active, StringComparison.Ordinal))
        {
            return false;
        }

        if (publicImagesOnly)
        {
            return SafeRasterContentPolicy.IsSafePublicImageMetadata(storageObject);
        }

        if (string.Equals(storageObject.Visibility, StorageObjectVisibilities.PublicImage, StringComparison.Ordinal))
        {
            return SafeRasterContentPolicy.IsSafePublicImageMetadata(storageObject);
        }

        if (string.Equals(storageObject.Visibility, StorageObjectVisibilities.AuthenticatedTenant, StringComparison.Ordinal))
        {
            return _currentUserService.IsAuthenticated;
        }

        if (string.Equals(storageObject.Visibility, StorageObjectVisibilities.PrivateOwner, StringComparison.Ordinal))
        {
            return _currentUserService.UserId.HasValue && storageObject.CreatedBy == _currentUserService.UserId;
        }

        return false;
    }

    private static string ResolveSafeDisplayName(StorageObject storageObject)
    {
        string candidate = string.IsNullOrWhiteSpace(storageObject.SafeDisplayName)
            ? storageObject.FullName
            : storageObject.SafeDisplayName;
        candidate = candidate.Trim();

        return candidate.Length is > 0 and <= 255
            && candidate is not "." and not ".."
            && !candidate.Any(char.IsControl)
            && !candidate.Contains('/', StringComparison.Ordinal)
            && !candidate.Contains('\\', StringComparison.Ordinal)
                ? candidate
                : "download";
    }
}
