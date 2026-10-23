using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Microsoft.IdentityModel.Tokens;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Selected runtime lane: actual Keycloak 26.1.2 metadata, issuance and realm
/// mapper effects. This is separate from generated-authority evidence.
/// </summary>
[Category("Security")]
[Category("Runtime")]
[ClassDataSource<KeycloakContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("SecurityInfra")]
public sealed class KeycloakNativeTokenContextHttpTests(KeycloakContainerFixture keycloak)
{
    [Test]
    public async Task NativeMetadataAndMapperProduceAccessContextButNotIdTokenAuthority()
    {
        using var metadataClient = new HttpClient();
        using var metadataResponse = await metadataClient.GetAsync(keycloak.MetadataAddress);
        await Assert.That(metadataResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync());
        await Assert.That(metadata.RootElement.GetProperty("issuer").GetString()).IsEqualTo(keycloak.Authority);
        await Assert.That(metadata.RootElement.GetProperty("token_endpoint").GetString())
            .IsEqualTo(keycloak.Authority + "/protocol/openid-connect/token");
        string jwksUri = metadata.RootElement.GetProperty("jwks_uri").GetString()!;
        await Assert.That(jwksUri.StartsWith(keycloak.Authority + "/", StringComparison.Ordinal)).IsTrue();
        using var keysResponse = await metadataClient.GetAsync(jwksUri);
        await Assert.That(keysResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var keys = JsonDocument.Parse(await keysResponse.Content.ReadAsStringAsync());

        KeycloakTokenPair tokens = await keycloak.TokenClient.GetUserTokenPairAsync();
        await Assert.That(!string.IsNullOrEmpty(tokens.IdToken)).IsTrue();
        using var accessPayload = Payload(tokens.AccessToken);
        using var accessHeader = Header(tokens.AccessToken);
        using var idPayload = Payload(tokens.IdToken!);
        using var idHeader = Header(tokens.IdToken!);
        await Assert.That(HasValidNativeSignature(tokens.AccessToken, keys.RootElement)).IsTrue();
        await Assert.That(HasValidNativeSignature(tokens.IdToken!, keys.RootElement)).IsTrue();
        await Assert.That(accessHeader.RootElement.GetProperty("typ").GetString()).IsEqualTo("JWT");
        await Assert.That(idHeader.RootElement.GetProperty("typ").GetString()).IsEqualTo("JWT");
        await Assert.That(accessHeader.RootElement.GetProperty("alg").GetString()).IsEqualTo("RS256");
        await Assert.That(idHeader.RootElement.GetProperty("alg").GetString()).IsEqualTo("RS256");
        string keyId = accessHeader.RootElement.GetProperty("kid").GetString()!;
        await Assert.That(keys.RootElement.GetProperty("keys").EnumerateArray().Any(key =>
            key.GetProperty("kid").GetString() == keyId && key.GetProperty("kty").GetString() == "RSA")).IsTrue();
        await Assert.That(accessPayload.RootElement.GetProperty("iss").GetString()).IsEqualTo(keycloak.Authority);
        await Assert.That(idPayload.RootElement.GetProperty("iss").GetString()).IsEqualTo(keycloak.Authority);
        await Assert.That(accessPayload.RootElement.GetProperty("typ").GetString()).IsEqualTo("Bearer");
        await Assert.That(idPayload.RootElement.GetProperty("typ").GetString()).IsEqualTo("ID");
        await Assert.That(accessPayload.RootElement.GetProperty("azp").GetString()).IsEqualTo(KeycloakContainerFixture.TestClientId);
        await Assert.That(idPayload.RootElement.GetProperty("azp").GetString()).IsEqualTo(KeycloakContainerFixture.TestClientId);
        await Assert.That(idPayload.RootElement.GetProperty("exp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds()).IsTrue();
        await Assert.That(HasAudience(accessPayload.RootElement, "islamu-event-api")).IsTrue();
        await Assert.That(HasAudience(idPayload.RootElement, "islamu-event-api")).IsFalse();
        await Assert.That(HasAudience(idPayload.RootElement, KeycloakContainerFixture.TestClientId)).IsTrue();

        foreach (bool legacy in new[] { false, true })
        {
            await using var api = new KeycloakTokenContextFixture();
            await api.InitializeAsync(legacy, [KeycloakContainerFixture.TestClientId],
                keycloak.Authority, keycloak.MetadataAddress);
            using var access = await api.PostAsync(tokens.AccessToken);
            await Assert.That(access.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(api.Revision).IsEqualTo(1);
            await Assert.That(api.Representation).IsEqualTo(legacy ? "JwtSecurityToken" : "JsonWebToken");
            using var id = await api.PostAsync(tokens.IdToken!);
            await Assert.That(id.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(api.Revision).IsEqualTo(1);

            // Match the real ID token's audience to isolate signed purpose rejection.
            await using var idAudience = new KeycloakTokenContextFixture();
            await idAudience.InitializeAsync(legacy, [KeycloakContainerFixture.TestClientId],
                keycloak.Authority, keycloak.MetadataAddress, KeycloakContainerFixture.TestClientId);
            using var wrongPurpose = await idAudience.PostAsync(tokens.IdToken!);
            await Assert.That(wrongPurpose.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(idAudience.Revision).IsEqualTo(0);
        }
    }

    [Test]
    public async Task NativeClientWithoutAudienceMapperCannotUseClientIdentityAsResource()
    {
        using var unmappedClient = keycloak.CreateContextTokenClient(KeycloakContainerFixture.NoAudienceClientId);
        KeycloakTokenPair tokens = await unmappedClient.GetUserTokenPairAsync();
        using var payload = Payload(tokens.AccessToken);
        await Assert.That(payload.RootElement.GetProperty("typ").GetString()).IsEqualTo("Bearer");
        await Assert.That(payload.RootElement.GetProperty("azp").GetString()).IsEqualTo(KeycloakContainerFixture.NoAudienceClientId);
        await Assert.That(HasAudience(payload.RootElement, "islamu-event-api")).IsFalse();
        await using var api = new KeycloakTokenContextFixture();
        await api.InitializeAsync(permittedClients: [KeycloakContainerFixture.NoAudienceClientId, KeycloakContainerFixture.TestClientId],
            nativeAuthority: keycloak.Authority, nativeMetadataAddress: keycloak.MetadataAddress);
        using var denied = await api.PostAsync(tokens.AccessToken);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(api.Revision).IsEqualTo(0);
        using var control = await api.PostAsync(await keycloak.TokenClient.GetUserTokenAsync());
        await Assert.That(control.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task NativeServiceClientRequiresExplicitClientAdmissionIndependentlyOfMappedAudience()
    {
        using var serviceClient = keycloak.CreateContextTokenClient(KeycloakContainerFixture.ServiceClientId);
        string token = await serviceClient.GetServiceTokenAsync();
        using var payload = Payload(token);
        await Assert.That(payload.RootElement.GetProperty("typ").GetString()).IsEqualTo("Bearer");
        await Assert.That(payload.RootElement.GetProperty("azp").GetString()).IsEqualTo(KeycloakContainerFixture.ServiceClientId);
        await Assert.That(HasAudience(payload.RootElement, "islamu-event-api")).IsTrue();
        await using var deniedApi = new KeycloakTokenContextFixture();
        await deniedApi.InitializeAsync(permittedClients: [KeycloakContainerFixture.TestClientId],
            nativeAuthority: keycloak.Authority, nativeMetadataAddress: keycloak.MetadataAddress);
        using var denied = await deniedApi.PostAsync(token);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(deniedApi.Revision).IsEqualTo(0);
        using var control = await deniedApi.PostAsync(await keycloak.TokenClient.GetUserTokenAsync());
        await Assert.That(control.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var admittedApi = new KeycloakTokenContextFixture();
        await admittedApi.InitializeAsync(permittedClients: [KeycloakContainerFixture.ServiceClientId],
            nativeAuthority: keycloak.Authority, nativeMetadataAddress: keycloak.MetadataAddress);
        using var admitted = await admittedApi.PostAsync(token);
        await Assert.That(admitted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(admittedApi.Revision).IsEqualTo(1);
    }

    private static JsonDocument Payload(string token) => Part(token, 1);
    private static JsonDocument Header(string token) => Part(token, 0);

    private static JsonDocument Part(string token, int index)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3)
            throw new InvalidOperationException("Native Keycloak token is not a compact signed JWT.");
        return JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[index]));
    }

    private static bool HasAudience(JsonElement payload, string expected)
    {
        if (!payload.TryGetProperty("aud", out JsonElement audience))
            return false;
        if (audience.ValueKind == JsonValueKind.String)
            return audience.GetString() == expected;
        if (audience.ValueKind != JsonValueKind.Array
            || audience.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
            throw new InvalidOperationException("Native Keycloak audience has an unsupported JSON shape.");
        return audience.EnumerateArray().Any(value => value.GetString() == expected);
    }

    private static bool HasValidNativeSignature(string token, JsonElement keySet)
    {
        using var header = Header(token);
        string keyId = header.RootElement.GetProperty("kid").GetString()!;
        JsonElement key = keySet.GetProperty("keys").EnumerateArray()
            .Single(candidate => candidate.GetProperty("kid").GetString() == keyId);
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(key.GetProperty("n").GetString()!),
            Exponent = Base64UrlEncoder.DecodeBytes(key.GetProperty("e").GetString()!)
        });
        string[] parts = token.Split('.');
        return rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64UrlEncoder.DecodeBytes(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
