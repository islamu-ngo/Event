namespace Explore.Application.Features.ConfigurationManifest.Managed;

using System.Net;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;

public interface IConfigurationDirectTransferRepository
{
    Task AddAsync(
        ConfigurationDirectTransferSession session,
        CancellationToken cancellationToken);

    Task<ConfigurationDirectTransferSession?> GetForUpdateAsync(
        Guid sessionId,
        string targetAuthorityKey,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        ConfigurationDirectTransferSession session,
        CancellationToken cancellationToken);

    Task<bool> TryClaimPromotionAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string nonceDigest,
        string destinationProofDigest,
        DateTime occurredAt,
        CancellationToken cancellationToken);

    Task ReleasePromotionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task CompletePromotionAsync(
        Guid sessionId,
        DateTime occurredAt,
        CancellationToken cancellationToken);
}

public interface IConfigurationDirectTransferChunkStore
{
    Task<bool> AddAsync(
        Guid sessionId,
        int offset,
        ReadOnlyMemory<byte> bytes,
        string digest,
        DateTime expiresAt,
        CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> AssembleAsync(
        Guid sessionId,
        int expectedByteLength,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<int> DeleteExpiredAsync(
        DateTime occurredAt,
        int maximumCount,
        CancellationToken cancellationToken);
}

public interface IConfigurationTransferDestinationResolver
{
    Task<IReadOnlyCollection<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken);
}

public sealed record ConfigurationDirectTransferCreated(
    Guid SessionId,
    string Nonce,
    string DestinationProof,
    DateTime ExpiresAt,
    int NextOffset)
{
    public override string ToString() => nameof(ConfigurationDirectTransferCreated);
}

public sealed record ConfigurationDirectTransferProgress(
    Guid SessionId,
    ConfigurationDirectTransferStatus Status,
    int NextOffset,
    int ArtifactByteLength,
    DateTime ExpiresAt)
{
    public override string ToString() => nameof(ConfigurationDirectTransferProgress);
}

public sealed class ConfigurationDirectTransferService(
    IConfigurationDirectTransferRepository sessions,
    IConfigurationDirectTransferChunkStore chunks,
    IConfigurationTransferDestinationResolver destinations,
    ConfigurationImportSessionApplicationService imports,
    ConfigurationImportArtifactParser parser,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
{
    public const int MaximumChunkBytes = 256 * 1024;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async Task<ConfigurationDirectTransferCreated> CreateAsync(
        ConfigurationImportTarget target,
        string sourceAuthority,
        Uri destinationOrigin,
        string artifactDigest,
        int artifactByteLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyCollection<IPAddress> addresses = await destinations.ResolveAsync(
            destinationOrigin.Host,
            cancellationToken);
        _ = ConfigurationDirectTransferPolicy.ValidateDestinationOrigin(
            destinationOrigin,
            addresses);

        Guid actorUserId = Actor();
        DateTime now = UtcNow();
        DateTime expiresAt = now.Add(Lifetime);
        string nonce = Token();
        string proof = Token();
        var session = ConfigurationDirectTransferSession.Create(
            Guid.CreateVersion7(),
            sourceAuthority,
            target.AuthorityKey,
            target.TenantId,
            ConfigurationImportDigest.Compute([destinationOrigin.GetLeftPart(UriPartial.Authority)]),
            Digest(proof),
            Digest(nonce),
            artifactDigest,
            artifactByteLength,
            now,
            expiresAt);
        session.ApproveDestination(actorUserId, Digest(proof), now);
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationDirectTransferSession? existing =
                    await sessions.GetForUpdateAsync(
                        session.Id,
                        target.AuthorityKey,
                        token);
                if (existing is null)
                    await sessions.AddAsync(session, token);
            },
            cancellationToken);
        return new ConfigurationDirectTransferCreated(
            session.Id,
            nonce,
            proof,
            expiresAt,
            session.NextOffset);
    }

    public async Task<ConfigurationDirectTransferProgress> ApproveSourceAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string nonce,
        string destinationProof,
        CancellationToken cancellationToken) =>
        await MutateAsync(
            sessionId,
            target,
            nonce,
            destinationProof,
            session => session.ApproveSource(Actor(), UtcNow()),
            cancellationToken);

    public async Task<ConfigurationDirectTransferProgress> AppendAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string nonce,
        string destinationProof,
        int offset,
        ReadOnlyMemory<byte> bytes,
        string chunkDigest,
        CancellationToken cancellationToken)
    {
        if (bytes.IsEmpty
            || bytes.Length > MaximumChunkBytes
            || !string.Equals(
                ConfigurationImportDigest.ComputeBytes(bytes.Span),
                chunkDigest,
                StringComparison.Ordinal))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationDirectTransferSession session =
                    await AuthorizedAsync(
                        sessionId,
                        target,
                        nonce,
                        destinationProof,
                        token);
                bool accepted = session.AcceptChunk(
                    offset,
                    bytes.Length,
                    chunkDigest,
                    Digest(nonce),
                    UtcNow());
                if (accepted)
                {
                    bool stored = await chunks.AddAsync(
                        session.Id,
                        offset,
                        bytes,
                        chunkDigest,
                        session.ExpiresAt,
                        token);
                    if (!stored)
                    {
                        throw new ConfigurationImportSessionException(
                            ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
                    }
                    await sessions.UpdateAsync(session, token);
                }
                return Progress(session);
            },
            cancellationToken);
    }

    public async Task<ConfigurationDirectTransferProgress> CompleteAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string nonce,
        string destinationProof,
        CancellationToken cancellationToken)
    {
        (ConfigurationDirectTransferProgress Progress, string? FailureCode) result =
            await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationDirectTransferSession session =
                    await AuthorizedAsync(
                        sessionId,
                        target,
                        nonce,
                        destinationProof,
                        token);
                ReadOnlyMemory<byte> artifact = await chunks.AssembleAsync(
                    session.Id,
                    session.ArtifactByteLength,
                    token);
                string digest = ConfigurationImportDigest.ComputeBytes(artifact.Span);
                try
                {
                    if (target.Scope == ConfigurationImportScope.Instance)
                        _ = parser.Parse(artifact);
                    else
                        _ = parser.ParseTenantPackage(artifact);
                }
                catch (ConfigurationImportSessionException exception)
                {
                    session.Cancel(UtcNow());
                    await chunks.DeleteAsync(session.Id, token);
                    await sessions.UpdateAsync(session, token);
                    return (Progress(session), exception.FailureCode);
                }
                session.Complete(digest, Digest(nonce), UtcNow());
                await sessions.UpdateAsync(session, token);
                return (Progress(session), (string?)null);
            },
            cancellationToken);
        if (result.FailureCode is { } failureCode)
            throw new ConfigurationImportSessionException(failureCode);
        return result.Progress;
    }

    public async Task<ConfigurationImportSessionCreatedResult> PromoteAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string nonce,
        string destinationProof,
        CancellationToken cancellationToken)
    {
        bool claimed = await sessions.TryClaimPromotionAsync(
            sessionId,
            target,
            Digest(nonce),
            Digest(destinationProof),
            UtcNow(),
            cancellationToken);
        if (!claimed)
        {
            _ = await AuthorizedAsync(
                sessionId,
                target,
                nonce,
                destinationProof,
                cancellationToken);
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.Replayed);
        }

        ConfigurationDirectTransferSession session =
            await sessions.GetForUpdateAsync(
                sessionId,
                target.AuthorityKey,
                cancellationToken)
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactMissing);
        ConfigurationImportSessionCreatedResult created;
        try
        {
            ReadOnlyMemory<byte> artifact = await chunks.AssembleAsync(
                session.Id,
                session.ArtifactByteLength,
                cancellationToken);
            created =
                target.Scope == ConfigurationImportScope.Instance
                    ? await imports.CreateInstanceAsync(
                        artifact,
                        cancellationToken)
                    : await imports.CreateTenantAsync(
                        target.TenantId!.Value,
                        artifact,
                        cancellationToken);
        }
        catch
        {
            await sessions.ReleasePromotionAsync(
                session.Id,
                CancellationToken.None);
            throw;
        }
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await sessions.CompletePromotionAsync(
                    session.Id,
                    UtcNow(),
                    token);
                await chunks.DeleteAsync(session.Id, token);
            },
            CancellationToken.None);
        return created;
    }

    public async Task<ConfigurationDirectTransferProgress> CancelAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string nonce,
        string destinationProof,
        CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationDirectTransferSession session =
                    await AuthorizedAsync(
                        sessionId,
                        target,
                        nonce,
                        destinationProof,
                        token);
                session.Cancel(UtcNow());
                await chunks.DeleteAsync(session.Id, token);
                await sessions.UpdateAsync(session, token);
                return Progress(session);
            },
            cancellationToken);

    private async Task<ConfigurationDirectTransferProgress> MutateAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string nonce,
        string destinationProof,
        Action<ConfigurationDirectTransferSession> mutate,
        CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationDirectTransferSession session =
                    await AuthorizedAsync(
                        sessionId,
                        target,
                        nonce,
                        destinationProof,
                        token);
                mutate(session);
                await sessions.UpdateAsync(session, token);
                return Progress(session);
            },
            cancellationToken);

    private async Task<ConfigurationDirectTransferSession> AuthorizedAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string nonce,
        string destinationProof,
        CancellationToken cancellationToken)
    {
        ConfigurationDirectTransferSession session =
            await sessions.GetForUpdateAsync(
                sessionId,
                target.AuthorityKey,
                cancellationToken)
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactMissing);
        if (session.TargetTenantId != target.TenantId
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(session.NonceDigest),
                Convert.FromHexString(Digest(nonce)))
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(session.DestinationProofDigest),
                Convert.FromHexString(Digest(destinationProof))))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TokenInvalid);
        }
        if (UtcNow() >= session.ExpiresAt)
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.Expired);
        return session;
    }

    private Guid Actor() =>
        currentUser.IsAuthenticated && currentUser.UserId is { } id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException();

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static ConfigurationDirectTransferProgress Progress(
        ConfigurationDirectTransferSession session) =>
        new(
            session.Id,
            session.Status,
            session.NextOffset,
            session.ArtifactByteLength,
            session.ExpiresAt);

    private static string Token() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Digest(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
