using System.Security.Cryptography;

namespace Explore.Domain;

public sealed class PrivacyErasureSaga
{
    private PrivacyErasureSaga()
    {
    }

    public Guid IntentId { get; private set; }
    public PrivacyErasureSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public int PolicyVersion { get; private set; }
    public long FenceToken { get; private set; }
    public byte[]? ReceiptHash { get; private set; }
    public DateTime ReceiptExpiresAtUtc { get; private set; }
    public DateTime FencedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }
    public PrivacyErasureSagaStatus Status { get; private set; }
    public int ProviderWorkCount { get; private set; }
    public int CompletedProviderWorkCount { get; private set; }
    public DateTime? LocalSettledAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static PrivacyErasureSaga Start(
        PrivacyErasureIntent intent,
        long fenceToken,
        byte[] receiptHash,
        DateTime receiptExpiresAtUtc,
        DateTime fencedAtUtc,
        Guid? concurrencyToken = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(receiptHash);
        if (fenceToken <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fenceToken));
        }

        if (receiptHash.Length != 32)
        {
            throw new ArgumentException("Receipt hashes must be SHA-256 values.", nameof(receiptHash));
        }

        RequireUtc(fencedAtUtc, nameof(fencedAtUtc));
        RequireUtc(receiptExpiresAtUtc, nameof(receiptExpiresAtUtc));
        if (receiptExpiresAtUtc <= fencedAtUtc)
        {
            throw new ArgumentException("Receipt expiry must follow the fence timestamp.", nameof(receiptExpiresAtUtc));
        }

        Guid token = concurrencyToken ?? Guid.CreateVersion7();
        if (token == Guid.Empty || token.Version != 7 || token.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Concurrency tokens must be RFC 4122 UUIDv7 values.", nameof(concurrencyToken));
        }

        return new PrivacyErasureSaga
        {
            IntentId = intent.IntentId,
            SubjectKind = intent.SubjectKind,
            SubjectId = intent.SubjectId,
            PolicyVersion = intent.PolicyVersion,
            FenceToken = fenceToken,
            ReceiptHash = [.. receiptHash],
            ReceiptExpiresAtUtc = receiptExpiresAtUtc,
            FencedAtUtc = fencedAtUtc,
            ConcurrencyToken = token,
            Status = PrivacyErasureSagaStatus.Fenced,
            UpdatedAtUtc = fencedAtUtc
        };
    }

    public bool Authenticates(ReadOnlySpan<byte> candidateHash, DateTime nowUtc)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (ReceiptHash is not { Length: 32 } receiptHash || nowUtc >= ReceiptExpiresAtUtc)
        {
            return false;
        }

        return candidateHash.Length == receiptHash.Length
            && CryptographicOperations.FixedTimeEquals(candidateHash, receiptHash);
    }

    public void ClearExpiredReceiptHash(DateTime clearedAtUtc)
    {
        RequireUtc(clearedAtUtc, nameof(clearedAtUtc));
        if (clearedAtUtc < ReceiptExpiresAtUtc)
        {
            throw new InvalidOperationException("The receipt has not expired.");
        }

        if (ReceiptHash is null)
        {
            return;
        }

        ReceiptHash = null;
        RotateConcurrency(clearedAtUtc);
    }

    public void MarkLocalSettled(
        DateTime settledAtUtc,
        int providerWorkCount,
        Guid expectedConcurrencyToken)
    {
        EnsureConcurrency(expectedConcurrencyToken);
        RequireUtc(settledAtUtc, nameof(settledAtUtc));
        if (Status != PrivacyErasureSagaStatus.Fenced)
        {
            throw new InvalidOperationException("Only a fenced erasure saga can settle local work.");
        }

        if (settledAtUtc < FencedAtUtc)
        {
            throw new ArgumentException("Local settlement cannot precede the fence.", nameof(settledAtUtc));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(providerWorkCount);
        LocalSettledAtUtc = settledAtUtc;
        ProviderWorkCount = providerWorkCount;
        Status = providerWorkCount == 0
            ? PrivacyErasureSagaStatus.Completed
            : PrivacyErasureSagaStatus.ProviderPending;
        CompletedAtUtc = providerWorkCount == 0 ? settledAtUtc : null;
        RotateConcurrency(settledAtUtc);
    }

    public void MarkProviderWorkCompleted(DateTime completedAtUtc, Guid expectedConcurrencyToken)
    {
        EnsureConcurrency(expectedConcurrencyToken);
        RequireUtc(completedAtUtc, nameof(completedAtUtc));
        if (Status != PrivacyErasureSagaStatus.ProviderPending || LocalSettledAtUtc is null)
        {
            throw new InvalidOperationException("Provider work can complete only after local settlement.");
        }

        if (completedAtUtc < LocalSettledAtUtc.Value)
        {
            throw new ArgumentException("Provider completion cannot precede local settlement.", nameof(completedAtUtc));
        }

        if (CompletedProviderWorkCount >= ProviderWorkCount)
        {
            throw new InvalidOperationException("All provider work is already complete.");
        }

        CompletedProviderWorkCount++;
        if (CompletedProviderWorkCount == ProviderWorkCount)
        {
            Status = PrivacyErasureSagaStatus.Completed;
            CompletedAtUtc = completedAtUtc;
        }

        RotateConcurrency(completedAtUtc);
    }

    private void EnsureConcurrency(Guid expectedConcurrencyToken)
    {
        if (expectedConcurrencyToken == Guid.Empty || expectedConcurrencyToken != ConcurrencyToken)
        {
            throw new InvalidOperationException("The privacy-erasure saga changed concurrently.");
        }
    }

    private void RotateConcurrency(DateTime changedAtUtc)
    {
        ConcurrencyToken = Guid.CreateVersion7();
        UpdatedAtUtc = changedAtUtc;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }
    }
}
