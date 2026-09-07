using CarpaNet.OAuth.Storage;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Infrastructure.Services.Federation;

public sealed class RepositoryBackedOAuthSessionStore(
    IUserAuthenticationTokenRepository repository,
    AtprotoSessionEnvelopeProtector protector,
    AtprotoOAuthSessionStoreContext context) : IOAuthSessionStore
{
    public async Task StoreAsync(
        string sub,
        OAuthSessionData data,
        CancellationToken cancellationToken = default)
    {
        context.RequireExpectedSubject(sub);
        var protectedSession = await protector
            .ProtectAsync(data, context, cancellationToken)
            .ConfigureAwait(false);
        var existing = await repository.GetAtprotoSessionForUpdateAsync(
            context.TenantId,
            context.UserId,
            RepositoryBackedAtprotoSession.Provider,
            context.ExpectedSubjectDid,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            await repository.CreateAtprotoSessionAsync(new UserAuthenticationToken
            {
                Id = Guid.CreateVersion7(),
                TenantId = context.TenantId,
                Tenant = null!,
                UserId = context.UserId,
                User = null!,
                Provider = RepositoryBackedAtprotoSession.Provider,
                SubjectDid = context.ExpectedSubjectDid,
                SessionCiphertext = protectedSession.Ciphertext,
                EncryptionKeyId = protectedSession.EncryptionKeyId,
                OAuthClientKeyId = context.OAuthClientKeyId,
                EnvelopeVersion = AtprotoSessionEnvelopeProtector.CurrentEnvelopeVersion,
                PdsHost = context.ExpectedPdsUri,
                ExpiresAt = data.TokenSet.ExpiresAt?.UtcDateTime
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        existing.SessionCiphertext = protectedSession.Ciphertext;
        existing.EncryptionKeyId = protectedSession.EncryptionKeyId;
        existing.OAuthClientKeyId = context.OAuthClientKeyId;
        existing.EnvelopeVersion = AtprotoSessionEnvelopeProtector.CurrentEnvelopeVersion;
        existing.PdsHost = context.ExpectedPdsUri;
        existing.ExpiresAt = data.TokenSet.ExpiresAt?.UtcDateTime;
        await repository.UpdateAtprotoSessionAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OAuthSessionData?> GetAsync(
        string sub,
        CancellationToken cancellationToken = default)
    {
        context.RequireExpectedSubject(sub);
        var existing = await repository.GetAtprotoSessionForUpdateAsync(
            context.TenantId,
            context.UserId,
            RepositoryBackedAtprotoSession.Provider,
            context.ExpectedSubjectDid,
            cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        ValidateStoredBinding(existing);
        var unprotected = await protector.UnprotectAsync(
            existing.SessionCiphertext,
            existing.EncryptionKeyId,
            context,
            cancellationToken).ConfigureAwait(false);
        if (unprotected.NeedsRewrite)
        {
            await StoreAsync(sub, unprotected.Session, cancellationToken).ConfigureAwait(false);
        }

        return unprotected.Session;
    }

    public async Task DeleteAsync(
        string sub,
        CancellationToken cancellationToken = default)
    {
        context.RequireExpectedSubject(sub);
        await repository.DeleteAtprotoSessionAsync(
            context.TenantId,
            context.UserId,
            RepositoryBackedAtprotoSession.Provider,
            context.ExpectedSubjectDid,
            cancellationToken).ConfigureAwait(false);
    }

    private void ValidateStoredBinding(UserAuthenticationToken existing)
    {
        if (existing.EnvelopeVersion != AtprotoSessionEnvelopeProtector.CurrentEnvelopeVersion
            || !string.Equals(existing.OAuthClientKeyId, context.OAuthClientKeyId, StringComparison.Ordinal)
            || !string.Equals(existing.PdsHost, context.ExpectedPdsUri, StringComparison.Ordinal))
        {
            throw new AtprotoOAuthSessionUnavailableException("binding_mismatch");
        }
    }
}

public sealed class AtprotoOAuthSessionStoreContext
{
    public AtprotoOAuthSessionStoreContext(
        Guid tenantId,
        Guid userId,
        AtprotoDid expectedSubjectDid,
        Uri expectedPdsUri,
        string oauthClientKeyId)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("ATProto OAuth session context is invalid.");
        }

        if (expectedSubjectDid == default)
        {
            throw new ArgumentException("ATProto OAuth subject is invalid.", nameof(expectedSubjectDid));
        }

        ArgumentNullException.ThrowIfNull(expectedPdsUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(oauthClientKeyId);
        if (oauthClientKeyId.Length > 128
            || oauthClientKeyId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
        {
            throw new ArgumentException("ATProto OAuth client key id is invalid.", nameof(oauthClientKeyId));
        }

        TenantId = tenantId;
        UserId = userId;
        ExpectedDid = expectedSubjectDid;
        ExpectedPdsUri = NormalizePdsUri(expectedPdsUri.AbsoluteUri)
            ?? throw new ArgumentException("ATProto PDS URI is invalid.", nameof(expectedPdsUri));
        OAuthClientKeyId = oauthClientKeyId;
    }

    public Guid TenantId { get; }
    public Guid UserId { get; }
    internal AtprotoDid ExpectedDid { get; }
    public string ExpectedSubjectDid => ExpectedDid.Value;
    public string ExpectedPdsUri { get; }
    public string OAuthClientKeyId { get; }

    internal void RequireExpectedSubject(string sub)
    {
        if (!AtprotoDid.TryParse(sub, out AtprotoDid parsedDid) || parsedDid != ExpectedDid)
        {
            throw new AtprotoOAuthSessionUnavailableException("subject_mismatch");
        }
    }

    internal static string? NormalizePdsUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            return null;
        }

        return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped) + "/";
    }
}

public sealed class AtprotoOAuthSessionUnavailableException(string failureCode)
    : Exception("ATProto OAuth session is unavailable.")
{
    public string FailureCode { get; } = failureCode;
}
