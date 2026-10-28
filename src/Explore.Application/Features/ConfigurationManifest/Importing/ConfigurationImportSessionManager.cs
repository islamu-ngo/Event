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
    ConfigurationImportPreviewComposer previewComposer,
    IHumanConfigurationImportAdmission admission)
{
    public Task<ConfigurationImportSessionCreated> CreateAsync(
        IPreparedHumanConfigurationImport preparedOwner,
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> artifact,
        DateTime occurredAt,
        TimeSpan lifetime,
        CancellationToken cancellationToken) =>
        admission.ExecuteSessionAsync(preparedOwner,
            CreateOperation(target, artifact, occurredAt, lifetime), cancellationToken);

    internal Task<ConfigurationImportSessionCreated> CreateSetupLiveAsync(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> artifact,
        DateTime occurredAt,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        RequireSetupLiveTarget(target);
        return unitOfWork.ExecuteInTransactionAsync(
            CreateOperation(target, artifact, occurredAt, lifetime).Execute, cancellationToken);
    }

    private HumanConfigurationImportSessionOperation<ConfigurationImportSessionCreated> CreateOperation(
        ConfigurationImportTarget target,
        ReadOnlyMemory<byte> artifact,
        DateTime occurredAt,
        TimeSpan lifetime)
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
        ReadOnlyMemory<byte> capturedArtifact = artifact.ToArray();

        return new(target, HumanConfigurationImportSessionOperationKind.Create,
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
                        capturedArtifact,
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
            });
    }

    public Task<ConfigurationImportPreview> PreparePreviewAsync(
        IPreparedHumanConfigurationImport preparedOwner,
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewInput input,
        DateTime occurredAt,
        CancellationToken cancellationToken) =>
        admission.ExecuteSessionAsync(preparedOwner,
            PreviewOperation(sessionId, target, accessToken, input, occurredAt), cancellationToken);

    internal Task<ConfigurationImportPreview> PrepareSetupLivePreviewAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewInput input,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        RequireSetupLiveTarget(target);
        return unitOfWork.ExecuteInTransactionAsync(
            PreviewOperation(sessionId, target, accessToken, input, occurredAt).Execute, cancellationToken);
    }

    private HumanConfigurationImportSessionOperation<ConfigurationImportPreview> PreviewOperation(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewInput input,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(input);
        string tokenDigest = DigestToken(accessToken);
        return new(target, HumanConfigurationImportSessionOperationKind.PreparePreview,
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
            });
    }

    public Task<ConfigurationImportAuthorizedArtifact>
        ReadArtifactForPreviewAsync(
        IPreparedHumanConfigurationImport preparedOwner,
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        DateTime occurredAt,
        CancellationToken cancellationToken) =>
        admission.ExecuteSessionAsync(preparedOwner,
            ReadArtifactOperation(sessionId, target, accessToken, occurredAt), cancellationToken);

    internal Task<ConfigurationImportAuthorizedArtifact> ReadSetupLiveArtifactAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        RequireSetupLiveTarget(target);
        return unitOfWork.ExecuteInTransactionAsync(
            ReadArtifactOperation(sessionId, target, accessToken, occurredAt).Execute, cancellationToken);
    }

    private HumanConfigurationImportSessionOperation<ConfigurationImportAuthorizedArtifact> ReadArtifactOperation(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        string tokenDigest = DigestToken(accessToken);
        return new(target, HumanConfigurationImportSessionOperationKind.ReadArtifact,
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

                return new ConfigurationImportAuthorizedArtifact(
                    artifact,
                    session.ArtifactDigest,
                    session.ExpiresAt,
                    session.ModerationReservation);
            });
    }

    internal Task<ConfigurationImportAuthorizedArtifact> ReadArtifactForPreviewInCurrentTransactionAsync(
        Guid sessionId, ConfigurationImportTarget target, string accessToken,
        DateTime occurredAt, CancellationToken cancellationToken) =>
        ReadArtifactOperation(sessionId, target, accessToken, occurredAt).Execute(cancellationToken);

    public Task CancelAsync(
        IPreparedHumanConfigurationImport preparedOwner,
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        string tokenDigest = DigestToken(accessToken);
        return admission.ExecuteSessionAsync(preparedOwner,
            new HumanConfigurationImportSessionOperation<bool>(target, HumanConfigurationImportSessionOperationKind.Cancel,
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
                    return true;
                }

                session.AuthorizePreview(target, tokenDigest, occurredAt);
                session.Cancel(occurredAt);
                await repository.UpdateAsync(session, token);
                await artifactStore.DeleteAsync(session.Artifact.Handle, token);
                return true;
            }),
            cancellationToken);
    }

    private static void RequireSetupLiveTarget(ConfigurationImportTarget target)
    {
        if (target.Scope != ConfigurationImportScope.Tenant
            || target.GetSessionStorageKey() == target.AuthorityKey)
            throw new InvalidOperationException("Setup import requires its enrollment-bound target.");
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
                    await repository.UpdateAsync(session, token);
                    await artifactStore.DeleteAsync(
                        session.Artifact.Handle,
                        token);
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
