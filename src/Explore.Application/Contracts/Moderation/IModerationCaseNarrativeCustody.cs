using System.Security.Cryptography;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Mandatory current custody owner for new intake. Acquire precedes native target staging;
/// staging shares its transaction and final recheck follows the same atomic SaveChanges.
/// Historical acknowledgement is deliberately outside this interface and never decrypts.
/// </summary>
public interface IModerationCaseNarrativeCustody
{
    Task<bool> AcquireAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<bool> StageAsync(ModerationCaseIntakeStage stage, ReadOnlyMemory<byte> material, CancellationToken cancellationToken);
    Task<bool> RecheckAsync(CancellationToken cancellationToken);
    Task<ModerationCaseNarrativeDisclosure?> ReadAsync(ModerationCaseAccessRequest request, CancellationToken cancellationToken);
    /// <summary>
    /// Qualifies and writes at the terminal output seam while retaining the original native
    /// transaction and writer fences. Delivery is never retried after the callback starts.
    /// The callback must finish using the borrowed bytes before returning; the owner clears them.
    /// </summary>
    Task<bool> DiscloseAsync(ModerationCaseAccessRequest request,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken);
    Task<int> PurgeExpiredAsync(int maximumRows, CancellationToken cancellationToken);
    Task<int> PurgeForErasureAsync(Guid reporterUserId, int maximumRows, CancellationToken cancellationToken);
}

/// <summary>
/// Short-lived qualified plaintext handoff, not a persisted entity or serializable audit value.
/// The transport owns disposal after writing the protected response and must disable caching.
/// Disposal clears the owned buffer, including previously borrowed views.
/// </summary>
public sealed class ModerationCaseNarrativeDisclosure : IDisposable
{
    private byte[] material;
    public ModerationCaseNarrativeDisclosure(ReadOnlySpan<byte> value) => material = value.ToArray();
    public ReadOnlyMemory<byte> Material => material;
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(material);
        material = [];
    }
    public override string ToString() => nameof(ModerationCaseNarrativeDisclosure);
}
