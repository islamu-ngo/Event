using System.Collections.ObjectModel;

namespace Explore.Blazor.Configuration;

/// <summary>
/// Process-local, value-free evidence for independently inspected coordinated replacements.
/// These correlation identities do not attest credential contents or authorize cleanup.
/// </summary>
public sealed class AuthenticationCredentialAcquisitionReceipts(Guid? authorityProfileRevision = null)
{
    public enum Consumer { GoogleOidc, KeycloakOidc }
    public enum Authority { Environment, Infisical, UserSecrets }

    public sealed record Receipt(Guid ProcessBootId, Consumer Consumer, Authority Authority,
        Guid Generation, bool Installed, Guid? AuthorityProfileRevision);

    private readonly object _sync = new();
    private readonly Dictionary<Consumer, Receipt> _receipts = [];
    public Guid ProcessBootId { get; } = Guid.CreateVersion7();

    public void RecordAcquired(Consumer consumer, Authority authority)
    {
        if (!Enum.IsDefined(consumer) || !Enum.IsDefined(authority))
            throw new ArgumentException("authentication_acquisition_invalid_consumer");
        lock (_sync)
        {
            if (_receipts.ContainsKey(consumer))
                throw new InvalidOperationException("authentication_acquisition_already_recorded");
            _receipts.Add(consumer, new(ProcessBootId, consumer, authority, Guid.CreateVersion7(), false,
                authorityProfileRevision));
        }
    }

    public void SetInstalled(Consumer consumer, bool installed)
    {
        lock (_sync)
        {
            if (_receipts.TryGetValue(consumer, out var receipt))
                _receipts[consumer] = receipt with { Installed = installed };
        }
    }

    public ReadOnlyCollection<Receipt> Snapshot()
    {
        lock (_sync)
            return Array.AsReadOnly(_receipts.Values.OrderBy(receipt => receipt.Consumer).ToArray());
    }
}
