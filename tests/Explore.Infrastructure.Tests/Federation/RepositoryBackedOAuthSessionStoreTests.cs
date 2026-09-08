using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services.Federation;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class RepositoryBackedOAuthSessionStoreTests
{
    private const string Did = "did:plc:alice";
    private const string AccessToken = "access-token-canary";
    private const string RefreshToken = "refresh-token-canary";
    private static readonly Guid TenantId = Guid.Parse("0198ab00-0000-7000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("0198ab00-0000-7000-8000-000000000002");

    [Test]
    public async Task StoreRoundTripsCompleteSessionWithoutPlaintextInPersistedBytes()
    {
        var ring = CreateRing(("active-key", "active", 7));
        var (store, _, getRow) = CreateStore(() => ring);
        var session = CreateSession();

        await store.StoreAsync(Did, session);

        var persisted = getRow()!;
        var persistedText = Encoding.UTF8.GetString(persisted.SessionCiphertext);
        await Assert.That(persisted.EncryptionKeyId).IsEqualTo("active-key");
        await Assert.That(persisted.OAuthClientKeyId).IsEqualTo("oauth-client-key");
        await Assert.That(persisted.EnvelopeVersion).IsEqualTo(1);
        await Assert.That(persisted.PdsHost).IsEqualTo("https://pds.example/");
        await Assert.That(persisted.SessionCiphertext.Length).IsGreaterThan(28);
        await Assert.That(persistedText).DoesNotContain(AccessToken);
        await Assert.That(persistedText).DoesNotContain(RefreshToken);
        await Assert.That(persistedText).DoesNotContain(session.DPoPKey.D!);

        var restored = await store.GetAsync(Did);
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.TokenSet.AccessToken).IsEqualTo(AccessToken);
        await Assert.That(restored.TokenSet.RefreshToken).IsEqualTo(RefreshToken);
        await Assert.That(restored.TokenSet.Audience).IsEqualTo("https://pds.example/");
        await Assert.That(restored.TokenSet.Sub).IsEqualTo(Did);
        await Assert.That(restored.AuthMethod).IsEqualTo("private_key_jwt");
        await Assert.That(restored.ClientId).IsEqualTo("https://events.example/oauth/client-metadata.json");
        await Assert.That(restored.RedirectUri).IsEqualTo("https://events.example/signin-atproto");
        await Assert.That(restored.Scope).IsEqualTo("atproto transition:generic");
        await Assert.That(restored.DPoPKey.D).IsEqualTo(session.DPoPKey.D);
    }

    [Test]
    public async Task GetRewritesRetiredEnvelopeUnderActiveKey()
    {
        var ring = CreateRing(("old-key", "active", 3));
        var (store, _, getRow) = CreateStore(() => ring);
        await store.StoreAsync(Did, CreateSession());
        var originalCiphertext = getRow()!.SessionCiphertext;
        ring = CreateRing(("new-key", "active", 5), ("old-key", "retired", 3));

        var restored = await store.GetAsync(Did);

        await Assert.That(restored).IsNotNull();
        await Assert.That(getRow()!.EncryptionKeyId).IsEqualTo("new-key");
        await Assert.That(getRow()!.SessionCiphertext).IsNotEquivalentTo(originalCiphertext);
    }

    [Test]
    public async Task UnknownKeyAndAuthenticatedEnvelopeTamperingFailClosed()
    {
        var ring = CreateRing(("active-key", "active", 7));
        var (store, _, getRow) = CreateStore(() => ring);
        await store.StoreAsync(Did, CreateSession());
        getRow()!.EncryptionKeyId = "missing-key";

        var unknownKey = await Assert.That(async () => await store.GetAsync(Did))
            .Throws<AtprotoOAuthSessionUnavailableException>();
        await Assert.That(unknownKey!.FailureCode).IsEqualTo("unknown_kid");
        await Assert.That(unknownKey.Message).DoesNotContain(AccessToken);

        getRow()!.EncryptionKeyId = "active-key";
        getRow()!.SessionCiphertext[^1] ^= 1;
        var tampered = await Assert.That(async () => await store.GetAsync(Did))
            .Throws<AtprotoOAuthSessionUnavailableException>();
        await Assert.That(tampered!.FailureCode).IsEqualTo("invalid_envelope");
        await Assert.That(tampered.Message).DoesNotContain(RefreshToken);
    }

    [Test]
    public async Task AuthenticatedEnvelopeWithNullOrMissingNestedMembersFailsAsInvalidSession()
    {
        var ring = CreateRing(("active-key", "active", 7));
        var (store, _, getRow) = CreateStore(() => ring);
        var mutations = new Action<JsonObject>[]
        {
            root => root[JsonName(nameof(OAuthSessionData.TokenSet))] = null,
            root => root[JsonName(nameof(OAuthSessionData.DPoPKey))] = null,
            root => root[JsonName(nameof(OAuthSessionData.TokenSet))]!.AsObject()
                .Remove(JsonName(nameof(TokenSet.Audience))),
            root => root[JsonName(nameof(OAuthSessionData.DPoPKey))]!.AsObject().Remove("x")
        };

        foreach (var mutate in mutations)
        {
            var session = CreateSession();
            await store.StoreAsync(Did, session);
            var serialized = JsonSerializer.SerializeToNode(
                session,
                AtprotoOAuthSessionJsonContext.Default.OAuthSessionData)!.AsObject();
            mutate(serialized);
            getRow()!.SessionCiphertext = CreateAuthenticatedEnvelope(serialized, 7);

            var failure = await Assert.That(async () => await store.GetAsync(Did))
                .Throws<AtprotoOAuthSessionUnavailableException>();
            await Assert.That(failure!.FailureCode).IsEqualTo("invalid_session");
        }
    }

    [Test]
    public async Task DeleteUsesTheEntireTenantUserProviderAndDidScopeAndIsIdempotent()
    {
        var ring = CreateRing(("active-key", "active", 7));
        var (store, repository, getRow) = CreateStore(() => ring);
        await store.StoreAsync(Did, CreateSession());

        await store.DeleteAsync(Did);
        await store.DeleteAsync(Did);

        await Assert.That(getRow()).IsNull();
        await repository.Received(2).DeleteAtprotoSessionAsync(
            TenantId,
            UserId,
            "atproto",
            Did,
            Arg.Any<CancellationToken>());
        await Assert.That(async () => await store.DeleteAsync("did:plc:other"))
            .Throws<AtprotoOAuthSessionUnavailableException>();
    }

    [Test]
    public async Task EncryptionKeyRingRejectsAmbiguousUnknownAndNonCanonicalMaterial()
    {
        var invalid = new[]
        {
            CreateRing(("one", "active", 1), ("two", "active", 2)),
            CreateRing(("one", "retired", 1)),
            CreateRing(("one", "unknown", 1)),
            "{\"keys\":[{\"kid\":\"one\",\"kid\":\"two\",\"k\":\"AA\",\"status\":\"active\"}]}",
            "{\"keys\":[{\"kid\":\"one\",\"k\":\"AA==\",\"status\":\"active\"}]}",
            "{\"keys\":[],\"extra\":true}",
            new string('x', 64 * 1024 + 1)
        };

        foreach (var serialized in invalid)
        {
            await Assert.That(AtprotoSessionEncryptionKeyRing.Parse(serialized)).IsNull();
        }
    }

    private static (RepositoryBackedOAuthSessionStore Store, IUserAuthenticationTokenRepository Repository, Func<UserAuthenticationToken?> GetRow)
        CreateStore(Func<string> getRing)
    {
        UserAuthenticationToken? row = null;
        var repository = Substitute.For<IUserAuthenticationTokenRepository>();
        repository.GetAtprotoSessionForUpdateAsync(
                TenantId,
                UserId,
                "atproto",
                Did,
                Arg.Any<CancellationToken>())
            .Returns(_ => row);
        repository.CreateAtprotoSessionAsync(
                Arg.Any<UserAuthenticationToken>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                row = call.Arg<UserAuthenticationToken>();
                return row;
            });
        repository.UpdateAtprotoSessionAsync(
                Arg.Any<UserAuthenticationToken>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => row = call.Arg<UserAuthenticationToken>());
        repository.DeleteAtprotoSessionAsync(
                TenantId,
                UserId,
                "atproto",
                Did,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => row = null);

        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Atproto.SessionEncryptionKeyRing,
                null,
                Arg.Any<CancellationToken>())
            .Returns(_ => SecretResolutionResult.Resolved(new ResolvedSecret(
                SecretDefinitionRegistry.Keys.Atproto.SessionEncryptionKeyRing,
                getRing(),
                SecretSourceType.Infisical,
                SecretScope.Instance,
                null,
                DateTimeOffset.UtcNow)));
        var context = new AtprotoOAuthSessionStoreContext(
            TenantId,
            UserId,
            Explore.Domain.ValueObjects.AtprotoDid.Parse(Did),
            new Uri("https://pds.example/"),
            "oauth-client-key");
        return (new(repository, new AtprotoSessionEnvelopeProtector(resolver), context), repository, () => row);
    }

    private static OAuthSessionData CreateSession()
    {
        using var dpopKey = DPoPKeyPair.Generate();
        return new()
        {
            DPoPKey = dpopKey.ExportKeyPair(),
            AuthMethod = "private_key_jwt",
            TokenSet = new TokenSet
            {
                Issuer = "https://issuer.example/",
                Sub = Did,
                Audience = "https://pds.example/",
                Scope = "atproto transition:generic",
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            },
            ClientId = "https://events.example/oauth/client-metadata.json",
            RedirectUri = "https://events.example/signin-atproto",
            Scope = "atproto transition:generic"
        };
    }

    private static byte[] CreateAuthenticatedEnvelope(JsonObject session, byte keyFill)
    {
        var plaintext = Encoding.UTF8.GetBytes(session.ToJsonString());
        var key = Enumerable.Repeat(keyFill, 32).ToArray();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        var associatedData = CreateAssociatedData();
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        var envelope = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo(envelope, 0);
        ciphertext.CopyTo(envelope, nonce.Length);
        tag.CopyTo(envelope, nonce.Length + ciphertext.Length);
        return envelope;
    }

    private static byte[] CreateAssociatedData()
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteInt32(writer, AtprotoSessionEnvelopeProtector.CurrentEnvelopeVersion);
        WriteString(writer, TenantId.ToString("D"));
        WriteString(writer, UserId.ToString("D"));
        WriteString(writer, RepositoryBackedAtprotoSession.Provider);
        WriteString(writer, Did);
        WriteString(writer, "https://pds.example/");
        WriteString(writer, "oauth-client-key");
        return writer.WrittenSpan.ToArray();
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(writer, byteCount);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(byteCount));
        writer.Advance(byteCount);
    }

    private static void WriteInt32(ArrayBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(sizeof(int)), value);
        writer.Advance(sizeof(int));
    }

    private static string JsonName(string propertyName) =>
        AtprotoOAuthSessionJsonContext.Default.Options.PropertyNamingPolicy?.ConvertName(propertyName)
        ?? propertyName;

    private static string CreateRing(params (string KeyId, string Status, byte Fill)[] keys) =>
        "{\"keys\":[" + string.Join(',', keys.Select(key =>
            $"{{\"kid\":\"{key.KeyId}\",\"k\":\"{Base64Url(Enumerable.Repeat(key.Fill, 32).ToArray())}\",\"status\":\"{key.Status}\"}}")) + "]}";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
