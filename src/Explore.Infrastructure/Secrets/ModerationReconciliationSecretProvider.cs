using Explore.Application.Contracts.Secrets;
using Explore.ServiceDefaults.Secrets;
using SecretKit.Core;

namespace Explore.Infrastructure.Secrets;

public sealed class ModerationReconciliationSecretProvider(SecretAuthorityAcquisition owner)
    : IModerationReconciliationSecretProvider
{
    public async ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = await owner.PrepareAvailableGroupAsync(
            [new("/api", "MODERATION__RECONCILIATION__APIKEY", "credential")],
            cancellationToken);
        if (prepared.Status == SecretStatus.Absent)
            return null;
        if (!prepared.IsSuccess)
            throw new InvalidOperationException("moderation_reconciliation_secret_unavailable");
        using var lease = prepared.Value;
        cancellationToken.ThrowIfCancellationRequested();
        return lease.Configuration["credential"];
    }
}
