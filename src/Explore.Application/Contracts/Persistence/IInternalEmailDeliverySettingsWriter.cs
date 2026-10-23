using System.Collections.Immutable;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Explicit trusted setup/lifecycle entry. Audit metadata does not select this route or
/// supply human authority. Administrative HTTP/CQS callers use IEmailDeliverySettingsWriter.
/// </summary>
public interface IInternalEmailDeliverySettingsWriter
{
    Task<EmailDeliverySettingsWriteResult> ApplyInternalAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task<EmailDeliverySettingsWriteResult> DisableInternalAsync(
        Guid? tenantId, Guid? actorUserId, CancellationToken cancellationToken = default);
}
