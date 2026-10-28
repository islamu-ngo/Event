using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Validation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

/// <summary>
/// Private preparation for the native inspection owner, not an authorization service. Call only
/// after native qualification while its fences are held. Never opens a public storage URL.
/// </summary>
public sealed class ModerationResourceInspectionMaterialReader(
    IStorageProviderBindingService providers,
    IEventResourceDestinationProtector destinations,
    IEventResourceGovernancePolicyReader governance)
{
    public Task<EventResourceGovernancePolicy?> ReadPolicyAsync(Guid tenantId, CancellationToken token) =>
        governance.ReadAsync(tenantId, token);

    public async Task<ModerationResourceInspectionMaterial?> PrepareAsync(
        EventResource resource, StorageObject? storage, EventResourceGovernancePolicy policy,
        Func<Task<bool>> current, CancellationToken token)
    {
        if (resource.IsDeleted || !resource.HasModerationQuarantine
            || !policy.EnabledDeliveryTypes.Contains((EventResourceDeliveryTypeEnum)resource.EventResourceDeliveryTypeId)
            || resource.AudienceRules.Count == 0
            || resource.AudienceRules.Any(rule => !policy.EnabledAudiences.Contains((EventResourceAudienceKindEnum)rule.AudienceKindId)))
            return null;
        if (resource.EventResourceDeliveryTypeId == (int)EventResourceDeliveryTypeEnum.ExternalLink)
        {
            if (resource.StorageObjectId is not null || resource.ExternalDestinationCiphertext is not { } ciphertext
                || resource.ExternalDestinationProtectionVersion is not { } version
                || resource.ExternalDestinationSafeOrigin is not { } origin)
                return null;
            if (!await current()) return null;
            var destination = destinations.Unprotect(ciphertext, resource.TenantId, resource.Id, version);
            if (!EventResourceDestinationValidator.TryValidate(destination, policy, out var validated, out var safeOrigin)
                || destination != validated || origin != safeOrigin)
                return null;
            // The native protector returns a string; retain no additional string copy or redirect.
            return new(Encoding.UTF8.GetBytes(destination), true);
        }
        if (resource.EventResourceDeliveryTypeId != (int)EventResourceDeliveryTypeEnum.StoredFile
            || storage is null || resource.StorageObjectId != storage.Id
            || !EventResourceFileSafety.IsSafe(storage, resource.TenantId, resource.Id, policy)
            || storage.Size > Array.MaxLength || storage.Sha256Checksum is not { Length: 64 } checksum
            || !checksum.All(char.IsAsciiHexDigit))
            return null;

        var bytes = new byte[(int)storage.Size];
        var extra = new byte[1];
        var transferred = false;
        try
        {
            var provider = await providers.ResolveTargetAsync(storage.StorageProviderBindingId, storage.Provider, token);
            if (!await current()) return null;
            var opened = await provider.OpenReadAsync(new(storage.ObjectKey!, storage.ContentType, storage.ProviderVersionId), token);
            await using (var stream = opened.Content)
            {
                if (!stream.CanRead || opened.Length != storage.Size || opened.ProviderVersionId != storage.ProviderVersionId)
                    return null;
                var offset = 0;
                while (offset < bytes.Length)
                {
                    if (!await current()) return null;
                    var read = await stream.ReadAsync(bytes.AsMemory(offset, Math.Min(65_536, bytes.Length - offset)), token);
                    if (read == 0) return null;
                    offset += read;
                }
                if (!await current() || await stream.ReadAsync(extra, token) != 0
                    || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), checksum, StringComparison.OrdinalIgnoreCase))
                    return null;
            }
            token.ThrowIfCancellationRequested();
            transferred = true;
            return new(bytes, false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(extra);
            if (!transferred) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

/// <summary>Owns the only prepared byte buffer; disposal also clears any borrowed memory slices.</summary>
public sealed class ModerationResourceInspectionMaterial(byte[] bytes, bool destination) : IDisposable
{
    private byte[]? material = bytes;
    public bool IsDestination { get; } = destination;
    public ReadOnlyMemory<byte> Bytes => material ?? throw new ObjectDisposedException(nameof(ModerationResourceInspectionMaterial));
    public void Dispose()
    {
        if (Interlocked.Exchange(ref material, null) is { } owned)
            CryptographicOperations.ZeroMemory(owned);
    }
    public override string ToString() => nameof(ModerationResourceInspectionMaterial);
}
