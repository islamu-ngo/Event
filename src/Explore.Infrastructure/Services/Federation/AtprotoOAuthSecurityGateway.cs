using System.Text.Json;
using CarpaNet.OAuth.Storage;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoOAuthSecurityGateway(
    AtprotoCoreClientFactory coreClientFactory,
    AtprotoOAuthClientFactory oauthClientFactory,
    IUserAuthenticationTokenRepository tokenRepository,
    AtprotoSessionEnvelopeProtector protector,
    IAtprotoSessionRefreshLock refreshLock,
    ILogger<AtprotoOAuthSecurityGateway> logger) : IAtprotoOAuthSecurityGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16
    };

    public async Task<AtprotoOAuthVerificationResult> VerifyAsync(
        AtprotoOAuthVerificationInput request,
        CancellationToken cancellationToken)
    {
        AtprotoDid expectedDid = request.ExpectedDid;
        string exactDid = expectedDid.Value;
        OAuthSessionData session;
        try
        {
            session = JsonSerializer.Deserialize<OAuthSessionData>(request.OAuthSessionPayload.Span, JsonOptions)
                ?? throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return AtprotoOAuthVerificationResult.Failed("invalid_session");
        }

        var expectedPds = AtprotoOAuthSessionStoreContext.NormalizePdsUri(request.ExpectedPdsUri.AbsoluteUri);
        if (expectedPds is null
            || !MatchesProviderSubject(session.TokenSet?.Sub, expectedDid)
            || !string.Equals(
                AtprotoOAuthSessionStoreContext.NormalizePdsUri(session.TokenSet?.Audience),
                expectedPds,
                StringComparison.Ordinal))
        {
            return AtprotoOAuthVerificationResult.Failed("session_binding_mismatch");
        }

        var transientStore = new SingleSessionStore(expectedDid, session);
        try
        {
            using var lease = await coreClientFactory.CreateAsync(
                exactDid,
                request.OAuthClientKeyId,
                transientStore,
                cancellationToken).ConfigureAwait(false);
            var pdsSession = await lease.Client.GetAsync<InfrastructureAtprotoGetSessionResponse>(
                "com.atproto.server.getSession",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var actualPds = AtprotoOAuthSessionStoreContext.NormalizePdsUri(lease.Client.BaseUrl.AbsoluteUri);
            if (!MatchesProviderSubject(lease.Client.AuthenticatedDid, expectedDid)
                || !MatchesProviderSubject(pdsSession.Did, expectedDid)
                || !string.Equals(actualPds, expectedPds, StringComparison.Ordinal)
                || pdsSession.Active == false
                || string.IsNullOrWhiteSpace(pdsSession.Handle))
            {
                return AtprotoOAuthVerificationResult.Failed("pds_identity_mismatch");
            }

            var persistedPayload = JsonSerializer.SerializeToUtf8Bytes(transientStore.Session, JsonOptions);
            return AtprotoOAuthVerificationResult.Verified(new AtprotoVerifiedOAuthSession(
                expectedDid,
                pdsSession.Handle,
                new Uri(expectedPds, UriKind.Absolute),
                request.OAuthClientKeyId,
                persistedPayload));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AtprotoOAuthVerificationResult.Failed("pds_verification_failed");
        }
    }

    public async Task<AtprotoPreparedOAuthSession> PreparePersistenceAsync(
        AtprotoVerifiedOAuthSession verifiedSession,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var session = JsonSerializer.Deserialize<OAuthSessionData>(verifiedSession.OAuthSessionPayload.Span, JsonOptions)
    ?? throw new AtprotoOAuthSessionUnavailableException("invalid_session");
        var context = new AtprotoOAuthSessionStoreContext(
            tenantId,
            userId,
            verifiedSession.Did,
            verifiedSession.PdsUri,
            verifiedSession.OAuthClientKeyId);
        var protectedSession = await protector
            .ProtectAsync(session, context, cancellationToken)
            .ConfigureAwait(false);
        return new AtprotoPreparedOAuthSession(
            protectedSession.Ciphertext,
            protectedSession.EncryptionKeyId,
            AtprotoSessionEnvelopeProtector.CurrentEnvelopeVersion,
            context.TenantId,
            context.UserId,
            context.ExpectedDid,
            context.ExpectedPdsUri,
            context.OAuthClientKeyId,
            session.TokenSet.ExpiresAt?.UtcDateTime);
    }

    public async Task PersistPreparedAsync(
        AtprotoPreparedOAuthSession preparedSession,
        CancellationToken cancellationToken)
    {
        var existing = await tokenRepository.GetAtprotoSessionForUpdateAsync(
    preparedSession.TenantId,
    preparedSession.UserId,
    RepositoryBackedAtprotoSession.Provider,
    preparedSession.SubjectDid.Value,
    cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await tokenRepository.CreateAtprotoSessionAsync(new UserAuthenticationToken
            {
                Id = Guid.CreateVersion7(),
                TenantId = preparedSession.TenantId,
                Tenant = null!,
                UserId = preparedSession.UserId,
                User = null!,
                Provider = RepositoryBackedAtprotoSession.Provider,
                SubjectDid = preparedSession.SubjectDid.Value,
                SessionCiphertext = preparedSession.SessionCiphertext.ToArray(),
                EncryptionKeyId = preparedSession.EncryptionKeyId,
                OAuthClientKeyId = preparedSession.OAuthClientKeyId,
                EnvelopeVersion = preparedSession.EnvelopeVersion,
                PdsHost = preparedSession.PdsHost,
                ExpiresAt = preparedSession.ExpiresAt
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        existing.SessionCiphertext = preparedSession.SessionCiphertext.ToArray();
        existing.EncryptionKeyId = preparedSession.EncryptionKeyId;
        existing.OAuthClientKeyId = preparedSession.OAuthClientKeyId;
        existing.EnvelopeVersion = preparedSession.EnvelopeVersion;
        existing.PdsHost = preparedSession.PdsHost;
        existing.ExpiresAt = preparedSession.ExpiresAt;
        await tokenRepository.UpdateAtprotoSessionAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AtprotoCurrentOAuthSession?> GetCurrentAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        var persisted = await tokenRepository.GetAtprotoSessionForReadAsync(
            identity.TenantId,
            identity.UserId,
            RepositoryBackedAtprotoSession.Provider,
            identity.Did.Value,
            cancellationToken).ConfigureAwait(false);
        if (persisted is null
            || !Uri.TryCreate(persisted.PdsHost, UriKind.Absolute, out var pdsUri)
            || string.IsNullOrWhiteSpace(persisted.OAuthClientKeyId))
        {
            return null;
        }

        try
        {
            var context = new AtprotoOAuthSessionStoreContext(
                identity.TenantId,
                identity.UserId,
                identity.Did,
                pdsUri,
                persisted.OAuthClientKeyId);
            var store = new RepositoryBackedOAuthSessionStore(tokenRepository, protector, context);
            var session = await store.GetAsync(identity.Did.Value, cancellationToken).ConfigureAwait(false);
            return session is null
                ? null
                : new AtprotoCurrentOAuthSession(
                    identity.Did,
                    pdsUri,
                    persisted.OAuthClientKeyId,
                    JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions));
        }
        catch (AtprotoOAuthSessionUnavailableException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public async Task<AtprotoOAuthRefreshResult> RefreshAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var coordination = await refreshLock.AcquireAsync(
            identity.TenantId,
            identity.UserId,
            RepositoryBackedAtprotoSession.Provider,
            identity.Did.Value,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var persisted = await tokenRepository.GetAtprotoSessionForReadAsync(
                identity.TenantId,
                identity.UserId,
                RepositoryBackedAtprotoSession.Provider,
                identity.Did.Value,
                cancellationToken).ConfigureAwait(false);
            if (persisted is null
                || !Uri.TryCreate(persisted.PdsHost, UriKind.Absolute, out var pdsUri)
                || string.IsNullOrWhiteSpace(persisted.OAuthClientKeyId))
            {
                return AtprotoOAuthRefreshResult.ReauthenticationRequired();
            }

            var context = new AtprotoOAuthSessionStoreContext(
                identity.TenantId,
                identity.UserId,
                identity.Did,
                pdsUri,
                persisted.OAuthClientKeyId);
            var store = new RepositoryBackedOAuthSessionStore(tokenRepository, protector, context);
            using var coreLease = await coreClientFactory.CreateAsync(
                identity.Did.Value,
                persisted.OAuthClientKeyId,
                store,
                cancellationToken).ConfigureAwait(false);
            await coreLease.RefreshAsync(cancellationToken).ConfigureAwait(false);
            var pdsSession = await coreLease.Client.GetAsync<InfrastructureAtprotoGetSessionResponse>(
                "com.atproto.server.getSession",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var actualPds = AtprotoOAuthSessionStoreContext.NormalizePdsUri(
                coreLease.Client.BaseUrl.AbsoluteUri);
            return MatchesProviderSubject(coreLease.Client.AuthenticatedDid, identity.Did)
                   && MatchesProviderSubject(pdsSession.Did, identity.Did)
                   && string.Equals(actualPds, context.ExpectedPdsUri, StringComparison.Ordinal)
                   && pdsSession.Active != false
                ? AtprotoOAuthRefreshResult.Refreshed()
                : AtprotoOAuthRefreshResult.ReauthenticationRequired();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AtprotoOAuthRefreshResult.ReauthenticationRequired();
        }
    }

    public async Task<AtprotoSessionRevocationResult> RevokeCurrentAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        using var localCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IAsyncDisposable? coordination = null;
        var outcome = AtprotoSessionRevocationOutcome.RemoteFailedLocalCleared;
        try
        {
            coordination = await refreshLock.AcquireAsync(
                identity.TenantId,
                identity.UserId,
                RepositoryBackedAtprotoSession.Provider,
                identity.Did.Value,
                localCleanup.Token).ConfigureAwait(false);
            var persisted = await tokenRepository.GetAtprotoSessionForReadAsync(
                identity.TenantId,
                identity.UserId,
                RepositoryBackedAtprotoSession.Provider,
                identity.Did.Value,
                localCleanup.Token).ConfigureAwait(false);
            if (persisted is null)
            {
                outcome = AtprotoSessionRevocationOutcome.AlreadyAbsent;
                return new(outcome);
            }

            if (Uri.TryCreate(persisted.PdsHost, UriKind.Absolute, out var pdsUri)
                && !string.IsNullOrWhiteSpace(persisted.OAuthClientKeyId))
            {
                var context = new AtprotoOAuthSessionStoreContext(
                    identity.TenantId,
                    identity.UserId,
                    identity.Did,
                    pdsUri,
                    persisted.OAuthClientKeyId);
                var store = new RepositoryBackedOAuthSessionStore(tokenRepository, protector, context);
                var observer = new AtprotoRevocationObserver();
                using var oauthLease = await oauthClientFactory.CreateAsync(
                    persisted.OAuthClientKeyId,
                    new MemoryOAuthStateStore(),
                    store,
                    observer,
                    cancellationToken).ConfigureAwait(false);
                using var client = await oauthLease.Session
                    .RestoreSessionAsync(identity.Did.Value, cancellationToken).ConfigureAwait(false);
                if (client is not null)
                {
                    await client.SignOutAsync(cancellationToken).ConfigureAwait(false);
                    if (observer.Attempted && observer.Succeeded)
                    {
                        outcome = AtprotoSessionRevocationOutcome.Revoked;
                    }
                }
            }
        }
        catch (Exception)
        {
            outcome = AtprotoSessionRevocationOutcome.RemoteFailedLocalCleared;
        }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await tokenRepository.DeleteAtprotoSessionAsync(
                    identity.TenantId,
                    identity.UserId,
                    RepositoryBackedAtprotoSession.Provider,
                    identity.Did.Value,
                    cleanup.Token).ConfigureAwait(false);
            }
            finally
            {
                if (coordination is not null)
                {
                    await coordination.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        if (outcome == AtprotoSessionRevocationOutcome.RemoteFailedLocalCleared)
        {
            logger.LogWarning(
                "ATProto remote session revocation did not complete; the local session was cleared. Outcome={Outcome}",
                outcome);
        }

        return new(outcome);
    }

    private static bool MatchesProviderSubject(string? subject, AtprotoDid expectedDid) =>
        AtprotoDid.TryParse(subject, out AtprotoDid parsedDid) && parsedDid == expectedDid;

    private sealed class SingleSessionStore(AtprotoDid expectedDid, OAuthSessionData session) : IOAuthSessionStore
    {
        public OAuthSessionData Session { get; private set; } = session;

        public Task StoreAsync(string sub, OAuthSessionData data, CancellationToken cancellationToken = default)
        {
            RequireSubject(sub);
            Session = data;
            return Task.CompletedTask;
        }

        public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
        {
            RequireSubject(sub);
            return Task.FromResult<OAuthSessionData?>(Session);
        }

        public Task DeleteAsync(string sub, CancellationToken cancellationToken = default)
        {
            RequireSubject(sub);
            return Task.CompletedTask;
        }

        private void RequireSubject(string sub)
        {
            if (!AtprotoDid.TryParse(sub, out AtprotoDid parsedDid) || parsedDid != expectedDid)
            {
                throw new AtprotoOAuthSessionUnavailableException("subject_mismatch");
            }
        }
    }
}
