using System.Security.Cryptography;
using System.Text;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services;

public sealed class RetainedAuthorityPrivacyErasureWorkflow(
    IPrivacyErasureReplayCheckpointRepository checkpointRepository,
    IPrivacyErasureStateRepository stateRepository,
    IPrivacyErasureAuthority authority,
    IUnitOfWork unitOfWork,
    PrivacyErasureApplier applier,
    IOptions<PrivacyErasureOptions> options,
    TimeProvider timeProvider) : IPrivacyErasureService
{
    private const int ReplayBatchSize = 100;
    private const int ReceiptByteCount = 32;
    private readonly PrivacyErasureOptions _options = Validate(options.Value);

    public async Task<PrivacyErasureStartDto> EraseUserAsync(
        Guid userId,
        Guid intentId,
        CancellationToken cancellationToken)
    {
        RequireId(userId, nameof(userId));
        RequireUuidV7(intentId, nameof(intentId));
        cancellationToken.ThrowIfCancellationRequested();

        PrivacyErasureSaga? existing = await stateRepository.GetBySubjectAsync(userId, cancellationToken);
        if (existing is not null)
        {
            EnsureMatchingRequest(existing, intentId);
            await ReplayPendingAsync(cancellationToken);
            PrivacyErasureSaga current = await stateRepository.GetByIntentAsync(intentId, cancellationToken)
                ?? throw new InvalidOperationException("The privacy-erasure saga disappeared during replay.");
            return ToStartDto(current, null);
        }

        string receipt = CreateReceipt();
        byte[] receiptHash = HashReceipt(receipt);
        PrivacyErasureRequest request = PrivacyErasureRequest.Create(
            intentId,
            PrivacyErasureSubjectKind.User,
            userId,
            PrivacyErasureReasonCode.AccountDeletion,
            _options.CurrentPolicyVersion);
        PrivacyErasureIntent fact = await AppendWithAmbiguousAcknowledgementRetryAsync(
            request,
            cancellationToken);
        DateTime fencedAtUtc = LaterOf(timeProvider.GetUtcNow().UtcDateTime, fact.RecordedAtUtc);
        bool created = await unitOfWork.ExecuteSerializableAsync(async ct =>
        {
            PrivacyErasureSaga? concurrent = await stateRepository.GetBySubjectAsync(userId, ct);
            if (concurrent is not null)
            {
                EnsureMatchingRequest(concurrent, intentId);
                return false;
            }

            PrivacyErasureSaga saga = PrivacyErasureSaga.Start(
                fact,
                fact.AuthoritySequence,
                receiptHash,
                fencedAtUtc.Add(_options.ReceiptLifetime),
                fencedAtUtc);
            await stateRepository.AddSagaAsync(saga, ct);
            await stateRepository.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

        await ReplayPendingAsync(cancellationToken);
        PrivacyErasureSaga settled = await stateRepository.GetByIntentAsync(intentId, cancellationToken)
            ?? throw new InvalidOperationException("The privacy-erasure saga disappeared after local settlement.");
        return ToStartDto(settled, created ? receipt : null);
    }

    public async Task<Guid?> AuthenticateReceiptAsync(
        string receipt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(receipt) || receipt.Length > 128)
        {
            return null;
        }

        byte[] receiptHash = HashReceipt(receipt);
        try
        {
            PrivacyErasureSaga? saga = await stateRepository.FindByReceiptHashAsync(
                receiptHash,
                cancellationToken);
            DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            return saga?.Authenticates(receiptHash, nowUtc) == true ? saga.IntentId : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(receiptHash);
        }
    }

    public async Task<PrivacyErasureStatusDto?> GetStatusAsync(
        Guid intentId,
        CancellationToken cancellationToken)
    {
        RequireUuidV7(intentId, nameof(intentId));
        PrivacyErasureSaga? saga = await stateRepository.GetByIntentAsync(intentId, cancellationToken);
        return saga is null
            ? null
            : new PrivacyErasureStatusDto(
                StatusCode(saga.Status),
                saga.ProviderWorkCount,
                saga.CompletedProviderWorkCount,
                saga.ReceiptExpiresAtUtc,
                saga.LocalSettledAtUtc,
                saga.CompletedAtUtc);
    }

    public async Task ReplayPendingAsync(CancellationToken cancellationToken)
    {
        PrivacyErasureReplayCheckpoint? latest = await checkpointRepository.GetLatestAsync(cancellationToken);
        PrivacyErasureAuthorityState state = await authority.GetStateAsync(cancellationToken);
        long latestSequence = latest?.AuthoritySequence ?? 0;
        if (latestSequence < state.RetainedFloorSequence)
        {
            throw new StaleRestoreBelowRetainedFloorException();
        }

        if (latestSequence > state.HighWaterSequence)
        {
            throw new PrivacyErasureCheckpointAheadException();
        }

        long afterSequence = latestSequence > state.RetainedFloorSequence
            ? latestSequence - 1
            : latestSequence;
        bool latestVerified = latest is null || latestSequence == state.RetainedFloorSequence;

        while (true)
        {
            IReadOnlyList<PrivacyErasureIntent> facts = await authority.ReadAfterAsync(
                afterSequence,
                ReplayBatchSize,
                cancellationToken);
            if (facts.Count == 0)
            {
                if (!latestVerified || afterSequence < state.HighWaterSequence)
                {
                    throw new PrivacyErasureSequenceGapException();
                }

                return;
            }

            foreach (PrivacyErasureIntent intent in facts)
            {
                if (intent.AuthoritySequence != afterSequence + 1)
                {
                    throw new PrivacyErasureSequenceGapException();
                }

                if (latest?.AuthoritySequence == intent.AuthoritySequence)
                {
                    if (!latest.Matches(intent))
                    {
                        throw new PrivacyErasureSequenceGapException();
                    }

                    latestVerified = true;
                    await applier.InvalidateRetainedIntentAsync(intent, cancellationToken);
                }

                bool covered = await stateRepository.HasCoverageAsync(
                    intent.IntentId,
                    _options.CurrentPolicyVersion,
                    cancellationToken);
                if (latest?.AuthoritySequence < intent.AuthoritySequence || latest is null || !covered)
                {
                    await EnsureFenceForReplayAsync(intent, cancellationToken);
                    PrivacyErasureApplier.PreparedErasure prepared = await applier.PrepareAsync(
                        intent,
                        cancellationToken);
                    PrivacyErasureApplier.AppliedErasure applied = await unitOfWork.ExecuteSerializableAsync(
                        ct => applier.ApplyInCurrentTransactionAsync(intent, prepared, ct),
                        cancellationToken);
                    await applier.InvalidateAfterCommitAsync(applied);
                    latest = await checkpointRepository.GetLatestAsync(cancellationToken);
                    latestVerified = latest is null || latest.AuthoritySequence <= intent.AuthoritySequence;
                }

                afterSequence = intent.AuthoritySequence;
            }
        }
    }

    private async Task EnsureFenceForReplayAsync(
        PrivacyErasureIntent intent,
        CancellationToken cancellationToken)
    {
        if (await stateRepository.GetByIntentAsync(intent.IntentId, cancellationToken) is not null)
        {
            return;
        }

        byte[] inaccessibleReceiptHash = RandomNumberGenerator.GetBytes(ReceiptByteCount);
        DateTime fencedAtUtc = intent.RecordedAtUtc;
        await unitOfWork.ExecuteSerializableAsync(async ct =>
        {
            if (await stateRepository.GetByIntentAsync(intent.IntentId, ct) is null)
            {
                PrivacyErasureSaga saga = PrivacyErasureSaga.Start(
                    intent,
                    intent.AuthoritySequence,
                    inaccessibleReceiptHash,
                    fencedAtUtc.Add(_options.ReceiptLifetime),
                    fencedAtUtc);
                await stateRepository.AddSagaAsync(saga, ct);
                await stateRepository.SaveChangesAsync(ct);
            }

            return true;
        }, cancellationToken);
    }

    private async Task<PrivacyErasureIntent> AppendWithAmbiguousAcknowledgementRetryAsync(
        PrivacyErasureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await authority.AppendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested
            && exception is TimeoutException or IOException or InvalidOperationException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await authority.AppendAsync(request, cancellationToken);
        }
    }

    private static PrivacyErasureStartDto ToStartDto(PrivacyErasureSaga saga, string? receipt) =>
        new(StatusCode(saga.Status), receipt, saga.ReceiptExpiresAtUtc);

    private static string StatusCode(PrivacyErasureSagaStatus status) => status switch
    {
        PrivacyErasureSagaStatus.Fenced => "fenced",
        PrivacyErasureSagaStatus.ProviderPending => "provider_pending",
        PrivacyErasureSagaStatus.Completed => "completed",
        _ => throw new InvalidOperationException("Unsupported privacy-erasure status.")
    };

    private static void EnsureMatchingRequest(PrivacyErasureSaga saga, Guid intentId)
    {
        if (saga.IntentId != intentId)
        {
            throw new ConcurrencyConflictException(
                "privacy_erasure_request_conflict",
                "A privacy-erasure request is already in progress.");
        }
    }

    private static string CreateReceipt()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(ReceiptByteCount);
        try
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static byte[] HashReceipt(string receipt) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(receipt));

    private static DateTime LaterOf(DateTime first, DateTime second) => first >= second ? first : second;

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }

    private static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Idempotency key must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }

    private static PrivacyErasureOptions Validate(PrivacyErasureOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        return value;
    }
}
