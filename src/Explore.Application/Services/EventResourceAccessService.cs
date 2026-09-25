using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Validation;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class EventResourceAccessService(
    EventResourceAuthorityOrchestrator authority,
    IEventResourceRepository resources,
    IEventResourceDestinationProtector protector,
    ITenantContext tenant,
    ICurrentUserService user,
    IMachinePrincipalAccessor machine)
{
    public Task<EventResourceAuthorityResult> PrepareAsync(Guid resourceId, DateTimeOffset deadlineUtc,
        CancellationToken cancellationToken) =>
        authority.AuthorizeAsync(new(tenant.TenantId, resourceId,
            user.IsAuthenticated ? user.UserId : null, machine.IsMachineCaller, "access")
        {
            DeadlineUtc = deadlineUtc
        }, async (facts, ct) =>
        {
            var resource = await resources.GetAuthorityResourceAsync(tenant.TenantId, resourceId, ct);
            if (resource is null || resource.IsDeleted
                || resource.EventResourceDeliveryTypeId != (int)EventResourceDeliveryTypeEnum.ExternalLink
                || !resource.HasPublishablePayload()
                || resource.ExternalDestinationProtectionVersion is not { } version
                || resource.ExternalDestinationCiphertext is not { } ciphertext
                || resource.ExternalDestinationSafeOrigin is not { } origin)
                throw new InvalidOperationException("Resource destination is unavailable.");

            string generation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
                resource.ConcurrencyStamp, version, origin, ciphertext))));
            if (generation != facts.AttachmentGeneration || facts.Access.GovernancePolicy is not { } policy)
                throw new InvalidOperationException("Resource destination is unavailable.");

            // Ciphertext is never unprotected before the exact current access decision.
            string destination = protector.Unprotect(ciphertext, tenant.TenantId, resourceId, version);
            if (!EventResourceDestinationValidator.TryValidate(destination, policy, out var validated, out var safeOrigin)
                || safeOrigin != origin || validated != destination)
                throw new InvalidOperationException("Resource destination is unavailable.");
            return new EventResourcePreparedDestination(generation, destination);
        }, cancellationToken);
}

/// <summary>Private single-use destination; never serialize or log this preparation.</summary>
public sealed class EventResourcePreparedDestination(string attachmentGeneration, string destination)
    : IEventResourcePrivatePreparation
{
    private string? _destination = destination;
    public string AttachmentGeneration { get; } = attachmentGeneration;
    public string TakeDestination() => Interlocked.Exchange(ref _destination, null)
        ?? throw new InvalidOperationException("Resource destination was already consumed.");
    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _destination, null);
        return ValueTask.CompletedTask;
    }
    public override string ToString() => nameof(EventResourcePreparedDestination);
}
