using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

internal sealed class SecretKitProviderTransport : ISecretReader
{
    internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public SecretAuthority Authority { get; } = new();

    public ValueTask<SecretOutcome<SecretRead>> ReadAsync(
        SecretReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = (InfisicalSecretAddress)reference.Address;
        if (!Values.TryGetValue(address.Key, out string? value))
            return ValueTask.FromResult(SecretOutcome<SecretRead>.Failure(SecretStatus.Absent));
        return ValueTask.FromResult(SecretOutcome<SecretRead>.Success(new SecretRead(
            SecretMaterial.FromText(value),
            new SecretObservation(reference, "1", DateTimeOffset.UtcNow, SecretValidity.Valid, null))));
    }
}
