using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Retains one native receipt projection across cancellation. The projection observes
/// provider evidence; this holder neither changes that evidence nor authorizes a write.
/// </summary>
public sealed class SecretMutationAttempt
{
    private Func<SecretMutationEvidence>? _projection;

    public SecretMutationEvidence Evidence =>
        Volatile.Read(ref _projection)?.Invoke() ?? SecretMutationEvidence.NotDispatched;

    public void AttachProjection(Func<SecretMutationEvidence> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (Interlocked.CompareExchange(ref _projection, projection, null) is not null)
            throw new InvalidOperationException("secret_mutation_attempt_already_attached");
    }

    public override string ToString() => "secret_mutation_attempt";
}
