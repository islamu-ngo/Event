using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.Storage;

namespace Explore.Application.Services;

public sealed class EventResourceContentService(
    EventResourceAuthorityOrchestrator authority,
    IEventResourceRepository resources,
    IStorageProviderBindingService providers,
    ITenantContext tenant,
    ICurrentUserService user,
    IMachinePrincipalAccessor machine)
{
    public Task<EventResourceAuthorityResult> PrepareAsync(Guid resourceId, DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken) =>
        authority.AuthorizeAsync(new(tenant.TenantId, resourceId,
            user.IsAuthenticated ? user.UserId : null, machine.IsMachineCaller, "download")
        {
            DeadlineUtc = deadlineUtc
        }, async (facts, ct) =>
        {
            if (facts.StorageObjectId is not { } storageId)
                throw new InvalidOperationException("Resource file is unavailable.");
            var storage = await resources.GetStorageObjectAsync(tenant.TenantId, storageId, ct);
            if (storage is null || !EventResourceFileSafety.IsSafe(storage, tenant.TenantId,
                    resourceId, facts.Access.GovernancePolicy)
                || EventResourceFileSafety.Generation(storage) != facts.AttachmentGeneration)
                throw new InvalidOperationException("Resource file is unavailable.");

            var provider = await providers.ResolveAsync(storage.StorageProviderBindingId!.Value, ct);
            var opened = await provider.OpenReadAsync(new(storage.ObjectKey!, storage.ContentType, storage.ProviderVersionId), ct);
            try
            {
                if (!opened.Content.CanRead || opened.Length != storage.Size
                    || opened.ProviderVersionId != storage.ProviderVersionId)
                    throw new InvalidOperationException("Resource file is unavailable.");
                string name = storage.SafeDisplayName.Trim();
                if (name.Length is 0 or > 255 || name is "." or ".." || name.Any(char.IsControl)
                    || name.Contains('/') || name.Contains('\\'))
                    name = "download";
                return new EventResourcePreparedContent(facts.AttachmentGeneration,
                    new(opened.Content, storage.ContentType!, storage.Size, null, null, name));
            }
            catch
            {
                await opened.Content.DisposeAsync();
                throw;
            }
        }, cancellationToken);
}

/// <summary>Owns provider bytes privately until the single-use native header gate transfers them.</summary>
public sealed class EventResourcePreparedContent(
    string attachmentGeneration, StorageObjectContentResult content) : IEventResourcePrivatePreparation
{
    private StorageObjectContentResult? _content = content;
    public string AttachmentGeneration { get; } = attachmentGeneration;
    public StorageObjectContentResult TakeContent() =>
        Interlocked.Exchange(ref _content, null)
        ?? throw new InvalidOperationException("Resource content has already been transferred.");
    public async ValueTask DisposeAsync()
    {
        var remaining = Interlocked.Exchange(ref _content, null);
        if (remaining is not null) await remaining.Content.DisposeAsync();
    }
    public override string ToString() => nameof(EventResourcePreparedContent);
}
