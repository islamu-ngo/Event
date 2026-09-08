namespace Explore.Persistence.Repositories;

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Persistence.Entities;

public sealed class ConfigurationImportArtifactRepository(
    ExploreDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider)
    : IConfigurationImportArtifactStore
{
    private const string ProtectionPurpose =
        "ISLAMU.Event.ConfigurationImportArtifact.v1";

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(ProtectionPurpose);

    public async Task<ConfigurationImportArtifactReference> StoreAsync(
        ConfigurationImportArtifactHandle handle,
        ReadOnlyMemory<byte> artifact,
        DateTime createdAt,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (artifact.IsEmpty
            || artifact.Length >
                ConfigurationImportSessionLimits.MaximumArtifactBytes)
        {
            throw new ConfigurationImportSessionException(
                artifact.IsEmpty
                    ? ConfigurationImportFailureCodes.ContractInvalid
                    : ConfigurationImportFailureCodes.TooLarge);
        }

        if (createdAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", nameof(createdAt));
        if (expiresAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", nameof(expiresAt));
        if (expiresAt <= createdAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        string digest = ConfigurationImportDigest.ComputeBytes(artifact.Span);
        ConfigurationImportStoredArtifact? existing =
            await dbContext.Set<ConfigurationImportStoredArtifact>()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == handle.Id,
                    cancellationToken);
        if (existing is not null)
        {
            if (existing.ByteLength != artifact.Length
                || !string.Equals(
                    existing.Sha256Digest,
                    digest,
                    StringComparison.Ordinal)
                || existing.ExpiresAt != expiresAt)
            {
                throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
            }

            return Reference(existing);
        }

        byte[] plaintext = artifact.ToArray();
        byte[] protectedPayload;
        try
        {
            protectedPayload = _protector.Protect(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
        var stored = ConfigurationImportStoredArtifact.Create(
            handle.Id,
            protectedPayload,
            digest,
            artifact.Length,
            createdAt,
            expiresAt);
        await dbContext.Set<ConfigurationImportStoredArtifact>()
            .AddAsync(stored, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Reference(stored);
    }

    public async Task<ReadOnlyMemory<byte>> ReadAsync(
        ConfigurationImportArtifactHandle handle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ConfigurationImportStoredArtifact? stored =
            await dbContext.Set<ConfigurationImportStoredArtifact>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == handle.Id,
                    cancellationToken);
        if (stored is null)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactMissing);
        }

        try
        {
            byte[] plaintext = _protector.Unprotect(stored.ProtectedPayload);
            if (plaintext.Length != stored.ByteLength
                || !string.Equals(
                    ConfigurationImportDigest.ComputeBytes(plaintext),
                    stored.Sha256Digest,
                    StringComparison.Ordinal))
            {
                throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
            }

            return plaintext;
        }
        catch (CryptographicException)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
        }
    }

    public async Task DeleteAsync(
        ConfigurationImportArtifactHandle handle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ConfigurationImportStoredArtifact? stored =
            await dbContext.Set<ConfigurationImportStoredArtifact>()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == handle.Id,
                    cancellationToken);
        if (stored is null)
            return;
        dbContext.Remove(stored);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(
        DateTime occurredAt,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (occurredAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", nameof(occurredAt));
        if (maximumCount is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        ConfigurationImportStoredArtifact[] expired = await dbContext
            .Set<ConfigurationImportStoredArtifact>()
            .Where(artifact => artifact.ExpiresAt <= occurredAt)
            .OrderBy(artifact => artifact.ExpiresAt)
            .ThenBy(artifact => artifact.Id)
            .Take(maximumCount)
            .ToArrayAsync(cancellationToken);
        if (expired.Length == 0)
            return 0;
        dbContext.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Length;
    }

    private static ConfigurationImportArtifactReference Reference(
        ConfigurationImportStoredArtifact stored) =>
        new(
            new ConfigurationImportArtifactHandle(stored.Id),
            stored.Sha256Digest,
            stored.ByteLength,
            stored.ExpiresAt);
}
