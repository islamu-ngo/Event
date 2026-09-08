using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.OrganizerPaymentConnections;

public sealed class OrganizerPaymentReadinessReconciliationService(
    IOrganizerPaymentProviderConnectionRepository repository,
    IOrganizerPaymentOnboardingProvider provider,
    IUnitOfWork unitOfWork,
    IOptions<OrganizerPaymentReadinessReconciliationOptions> options,
    TimeProvider timeProvider)
{
    private readonly OrganizerPaymentReadinessReconciliationOptions _options = options.Value;

    public async Task<OrganizerPaymentReadinessReconciliationResult> ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        DateTime observedBefore = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-_options.StaleIntervalMinutes);
        IReadOnlyList<OrganizerPaymentProviderConnection> due = await repository.ListDueReadinessChecksAsync(
            observedBefore,
            _options.BatchSize,
            cancellationToken);

        var failures = new List<OrganizerPaymentReadinessReconciliationFailure>();
        var failureCount = 0;
        var processed = 0;
        var updated = 0;
        var skipped = 0;

        foreach (OrganizerPaymentProviderConnection dueConnection in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            OrganizerPaymentProviderReadinessResult result;
            try
            {
                result = await provider.GetReadinessAsync(
                    new OrganizerPaymentProviderReadinessRequest(
                        dueConnection.ProviderCode,
                        dueConnection.ConnectPlatformId,
                        dueConnection.ExternalAccountId),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failureCount++;
                AddFailure(failures, "organizer_payment_readiness_exception", null);
                continue;
            }

            if (!result.Success || result.Readiness is null)
            {
                failureCount++;
                AddFailure(failures, result.FailureCode ?? "organizer_payment_readiness_failed", result.ProviderRequestId);
                continue;
            }

            OrganizerPaymentReadinessApplyResult applyResult = await ApplyReadinessAsync(
                dueConnection.TenantId,
                dueConnection.Id,
                result.Readiness,
                cancellationToken);

            if (applyResult == OrganizerPaymentReadinessApplyResult.Updated)
            {
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        return new OrganizerPaymentReadinessReconciliationResult(due.Count, processed, updated, skipped, failureCount, failures.ToArray());
    }

    private async Task<OrganizerPaymentReadinessApplyResult> ApplyReadinessAsync(
        Guid tenantId,
        Guid connectionId,
        OrganizerPaymentProviderReadiness readiness,
        CancellationToken cancellationToken) => await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            OrganizerPaymentProviderConnection? connection = await repository.GetByTenantAndIdForUpdateAsync(
                tenantId,
                connectionId,
                token);

            if (connection is null
                || connection.StatusId is (int)OrganizerPaymentProviderConnectionStatusEnum.Disabled or (int)OrganizerPaymentProviderConnectionStatusEnum.Replaced
                || connection.LastReadinessObservedAt is { } observedAt && readiness.ObservedAt <= observedAt)
            {
                return OrganizerPaymentReadinessApplyResult.Skipped;
            }

            connection.ApplyReadiness(OrganizerPaymentReadinessMapper.ToObservation(readiness));
            await repository.SaveChangesAsync(token);
            return OrganizerPaymentReadinessApplyResult.Updated;
        }, cancellationToken);

    private static void AddFailure(
        List<OrganizerPaymentReadinessReconciliationFailure> failures,
        string failureCode,
        string? providerRequestId)
    {
        if (failures.Count < 10)
        {
            failures.Add(new OrganizerPaymentReadinessReconciliationFailure(Bound(failureCode, 120), Bound(providerRequestId, 120)));
        }
    }

    private static string? Bound(string? value, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is 0 || normalized.Length > maxLength || normalized.Any(char.IsControl)
            ? null
            : normalized;
    }

    private enum OrganizerPaymentReadinessApplyResult
    {
        Skipped,
        Updated
    }
}

public sealed record OrganizerPaymentReadinessReconciliationResult(
    int DueCount,
    int ProcessedCount,
    int UpdatedCount,
    int SkippedCount,
    int FailureCount,
    IReadOnlyList<OrganizerPaymentReadinessReconciliationFailure> Failures);

public sealed record OrganizerPaymentReadinessReconciliationFailure(
    string? FailureCode,
    string? ProviderRequestId);
