namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Moderation;

public sealed record ConfigurationImportSessionCreated(
    ConfigurationImportSession Session,
    string AccessToken)
{
    public override string ToString() => nameof(ConfigurationImportSessionCreated);
}

public sealed record ConfigurationImportAuthorizedArtifact(
    ReadOnlyMemory<byte> Bytes,
    string Digest,
    DateTime ExpiresAt,
    ModerationConfigurationImportReservationSet? ModerationReservation = null)
{
    public override string ToString() =>
        nameof(ConfigurationImportAuthorizedArtifact);
}

public sealed class ConfigurationImportSessionManager(
    IConfigurationImportSessionRepository repository,
    IConfigurationImportArtifactStore artifactStore,
    IUnitOfWork unitOfWork,
    ConfigurationImportPreviewComposer previewComposer)
{
    public async Task<ConfigurationImportSessionCreated> CreateAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> artifact,
        DateTime occurredAt,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ConfigurationImportContractGuard.RequireUtc(occurredAt, nameof(occurredAt));
        if (lifetime <= TimeSpan.Zero
            || lifetime > ConfigurationImportSessionLimits.MaximumSessionLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        Guid sessionId = Guid.CreateVersion7();
        var handle = new ConfigurationImportArtifactHandle(Guid.CreateVersion7());
        string accessToken = IssueAccessToken();
        string accessTokenDigest = DigestToken(accessToken);
        DateTime expiresAt = occurredAt.Add(lifetime);

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationImportSession? existing =
                    await repository.GetForUpdateAsync(
                        sessionId,
                        target,
                        token);
                if (existing is not null)
                {
                    return new ConfigurationImportSessionCreated(
                        existing,
                        accessToken);
                }

                ConfigurationImportArtifactReference stored =
                    await artifactStore.StoreAsync(
                        handle,
                        artifact,
                        occurredAt,
                        expiresAt,
                        token);
                ConfigurationImportSession session =
                    ConfigurationImportSession.Create(
                        sessionId,
                        target,
                        stored,
                        accessTokenDigest,
                        occurredAt,
                        lifetime);
                await repository.AddAsync(session, token);
                return new ConfigurationImportSessionCreated(
                    session,
                    accessToken);
            },
            cancellationToken);
    }

    public async Task<ConfigurationImportPreview> PreparePreviewAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewInput input,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(input);
        string tokenDigest = DigestToken(accessToken);
        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationImportSession session =
                    await repository.GetForUpdateAsync(
                        sessionId,
                        target,
                        token)
                    ?? throw new ConfigurationImportSessionException(
                        ConfigurationImportFailureCodes.ArtifactMissing);
                session.AuthorizePreview(target, tokenDigest, occurredAt);
                if (input.Target != target
                    || !string.Equals(
                        input.ArtifactDigest,
                        session.ArtifactDigest,
                        StringComparison.Ordinal)
                    || input.ExpiresAt > session.ExpiresAt)
                {
                    throw new ConfigurationImportSessionException(
                        ConfigurationImportFailureCodes.TargetMismatch);
                }

                ReadOnlyMemory<byte> artifact = await artifactStore.ReadAsync(
                    session.Artifact.Handle,
                    token);
                if (!string.Equals(
                        ConfigurationImportDigest.ComputeBytes(artifact.Span),
                        session.ArtifactDigest,
                        StringComparison.Ordinal))
                {
                    throw new ConfigurationImportSessionException(
                        ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
                }

                ConfigurationImportPreview preview =
                    previewComposer.Compose(input);
                session.MarkPreviewReady(preview.Binding, occurredAt, input.ModerationPreview?.Reservation);
                await repository.UpdateAsync(session, token);
                return preview;
            },
            cancellationToken);
    }

    public async Task<ConfigurationImportAuthorizedArtifact>
        ReadArtifactForPreviewAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(
            token => ReadArtifactForPreviewInCurrentTransactionAsync(
                sessionId, target, accessToken, occurredAt, token), cancellationToken);
    }

    internal async Task<ConfigurationImportAuthorizedArtifact> ReadArtifactForPreviewInCurrentTransactionAsync(
        Guid sessionId, ConfigurationImportTarget target, string accessToken,
        DateTime occurredAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        string tokenDigest = DigestToken(accessToken);
        var session = await repository.GetForUpdateAsync(sessionId, target, cancellationToken)
            ?? throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ArtifactMissing);
        session.AuthorizePreview(target, tokenDigest, occurredAt);
        var artifact = await artifactStore.ReadAsync(session.Artifact.Handle, cancellationToken);
        if (!string.Equals(ConfigurationImportDigest.ComputeBytes(artifact.Span),
                session.ArtifactDigest, StringComparison.Ordinal))
            throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
        return new(artifact, session.ArtifactDigest, session.ExpiresAt, session.ModerationReservation);
    }

    public async Task CancelAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        string tokenDigest = DigestToken(accessToken);
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationImportSession session =
                    await repository.GetForUpdateAsync(
                        sessionId,
                        target,
                        token)
                    ?? throw new ConfigurationImportSessionException(
                        ConfigurationImportFailureCodes.ArtifactMissing);
                if (session.State == ConfigurationImportSessionState.Cancelled)
                {
                    if (!session.HasAccess(target, tokenDigest))
                    {
                        throw new ConfigurationImportSessionException(
                            ConfigurationImportFailureCodes.TokenInvalid);
                    }
                    return;
                }

                session.AuthorizePreview(target, tokenDigest, occurredAt);
                session.Cancel(occurredAt);
                await artifactStore.DeleteAsync(session.Artifact.Handle, token);
                await repository.UpdateAsync(session, token);
            },
            cancellationToken);
    }

    public Task<int> ExpireAsync(
        DateTime occurredAt,
        int maximumCount,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                IReadOnlyList<ConfigurationImportSession> sessions =
                    await repository.ListExpiredForUpdateAsync(
                        occurredAt,
                        maximumCount,
                        token);
                foreach (ConfigurationImportSession session in sessions)
                {
                    session.Expire(occurredAt);
                    await artifactStore.DeleteAsync(
                        session.Artifact.Handle,
                        token);
                    await repository.UpdateAsync(session, token);
                }

                return sessions.Count;
            },
            cancellationToken);

    private static string IssueAccessToken()
    {
        string token = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return token;
    }

    internal static string DigestToken(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return ConfigurationImportDigest.ComputeBytes(
            Encoding.UTF8.GetBytes(accessToken));
    }
}
