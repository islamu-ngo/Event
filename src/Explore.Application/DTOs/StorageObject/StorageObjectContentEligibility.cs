using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain.Services.Registration;

namespace Explore.Application.DTOs.StorageObject;

/// <summary>Server-only registration disclosure facts carried to final HAL projection.</summary>
public sealed record StorageObjectContentEligibility(
    bool ContentAllowed,
    bool PresignedDownloadAllowed,
    DateTime? DisclosureUntilUtc)
{
    public static StorageObjectContentEligibility Unrestricted { get; } = new(true, true, null);
    private static StorageObjectContentEligibility Denied { get; } = new(false, false, null);

    public bool CanReadAt(DateTime utcNow) =>
        ContentAllowed && (DisclosureUntilUtc is null || utcNow < DisclosureUntilUtc.Value);

    internal static async Task<StorageObjectContentEligibility> ResolveAsync(
        Domain.StorageObject storageObject,
        IStorageObjectRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var answerFile = await repository.GetRegistrationAnswerFileAsync(
            storageObject.Id, storageObject.TenantId, cancellationToken);
        if (!StorageObjectContentReader.IsRegistrationOwned(storageObject, answerFile))
            return Unrestricted;

        var order = await repository.GetRegistrationContentOrderAsync(storageObject, answerFile, cancellationToken);
        if (!StorageObjectContentReader.CanDiscloseRegistrationContent(
                storageObject, answerFile, order, true, timeProvider.GetUtcNow().UtcDateTime))
            return Denied;

        return new(true, !AnonymousRegistrationRetentionPolicy.AppliesTo(order!),
            AnonymousRegistrationRetentionPolicy.GetDisclosureDeadline(
                order!, storageObject.RegistrationContentRetentionUntilUtc));
    }
}
