using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>Independently signed conditions, each sent through real production JwtBearer HTTP.</summary>
[Category("Security")]
public sealed class KeycloakTokenContextHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ValidAccessTokenMutatesProtectedStateInSupportedRepresentation(bool legacy)
    {
        await using var fixture = new KeycloakTokenContextFixture();
        await fixture.InitializeAsync(legacy);
        using var response = await fixture.PostAsync(fixture.Sign(fixture.ValidPayload()));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fixture.Revision).IsEqualTo(1);
        await Assert.That(fixture.Representation).IsEqualTo(legacy ? "JwtSecurityToken" : "JsonWebToken");
    }

    [Test]
    [Arguments("expired")]
    [Arguments("not-before")]
    [Arguments("wrong-key")]
    [Arguments("issuer")]
    [Arguments("audience")]
    [Arguments("missing-audience")]
    [Arguments("bff-audience")]
    [Arguments("audience-number")]
    [Arguments("audience-null")]
    [Arguments("audience-blank")]
    [Arguments("audience-mixed-array")]
    [Arguments("empty-audience")]
    [Arguments("duplicate-audience")]
    [Arguments("conflicting-audience")]
    [Arguments("duplicate-audience-value")]
    [Arguments("client")]
    [Arguments("missing-client")]
    [Arguments("duplicate-client")]
    [Arguments("conflicting-client")]
    [Arguments("client-number")]
    [Arguments("client-null")]
    [Arguments("client-array")]
    [Arguments("empty-client")]
    [Arguments("id-token")]
    [Arguments("missing-purpose")]
    [Arguments("duplicate-purpose")]
    [Arguments("conflicting-purpose")]
    [Arguments("purpose-number")]
    [Arguments("purpose-null")]
    [Arguments("purpose-array")]
    [Arguments("header-type")]
    public async Task IsolatedSignedContextFailureCannotMutateProtectedState(string condition)
    {
        // Repeat the same invariant for both handler representations, not conditional assertions.
        foreach (bool legacy in new[] { false, true })
        {
            await using var fixture = new KeycloakTokenContextFixture();
            await fixture.InitializeAsync(legacy, ["islamu-event-blazor", "approved-service"]);
            var payload = fixture.ValidPayload();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            switch (condition)
            {
                case "expired":
                    payload["iat"] = now - 7200;
                    payload["nbf"] = now - 7200;
                    payload["exp"] = now - 3600;
                    break;
                case "not-before": payload["nbf"] = now + 1800; break;
                case "issuer": payload["iss"] = "https://different-issuer.invalid"; break;
                case "audience": payload["aud"] = "unrelated-api"; break;
                case "missing-audience": payload.Remove("aud"); break;
                case "bff-audience": payload["aud"] = "islamu-event-blazor"; break;
                case "audience-number": payload["aud"] = 42; break;
                case "audience-null": payload["aud"] = null; break;
                case "audience-blank": payload["aud"] = " "; break;
                case "audience-mixed-array": payload["aud"] = new object[] { "islamu-event-api", 42 }; break;
                case "empty-audience": payload["aud"] = Array.Empty<string>(); break;
                case "duplicate-audience-value": payload["aud"] = new[] { "islamu-event-api", "islamu-event-api" }; break;
                case "client": payload["azp"] = "unlisted-service"; break;
                case "missing-client": payload.Remove("azp"); break;
                case "client-number": payload["azp"] = 42; break;
                case "client-null": payload["azp"] = null; break;
                case "client-array": payload["azp"] = new[] { "islamu-event-blazor" }; break;
                case "empty-client": payload["azp"] = ""; break;
                case "id-token": payload["typ"] = "ID"; break;
                case "missing-purpose": payload.Remove("typ"); break;
                case "purpose-number": payload["typ"] = 42; break;
                case "purpose-null": payload["typ"] = null; break;
                case "purpose-array": payload["typ"] = new[] { "Bearer" }; break;
            }
            string json = JsonSerializer.Serialize(payload);
            string? duplicate = condition switch
            {
                "duplicate-audience" => "\"aud\":\"islamu-event-api\"",
                "conflicting-audience" => "\"aud\":\"another-api\"",
                "duplicate-client" => "\"azp\":\"islamu-event-blazor\"",
                "conflicting-client" => "\"azp\":\"unlisted-service\"",
                "duplicate-purpose" => "\"typ\":\"Bearer\"",
                "conflicting-purpose" => "\"typ\":\"ID\"",
                _ => null
            };
            if (duplicate is not null)
                json = json[..^1] + "," + duplicate + "}";
            string token = fixture.SignJson(json, condition == "wrong-key", condition == "header-type" ? "ID" : "JWT");
            using var response = await fixture.PostAsync(token);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(fixture.Revision).IsEqualTo(0);
            // The unchanged host accepts the control token; a broken fixture cannot pass every negative.
            using var control = await fixture.PostAsync(fixture.Sign(fixture.ValidPayload()));
            await Assert.That(control.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(fixture.Revision).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitServiceClientDoesNotBroadenResourceAudience(bool legacy)
    {
        await using var fixture = new KeycloakTokenContextFixture();
        await fixture.InitializeAsync(legacy, ["approved-service"]);
        var payload = fixture.ValidPayload();
        payload["azp"] = "approved-service";
        payload["aud"] = new[] { "another-resource", "islamu-event-api" };
        using var valid = await fixture.PostAsync(fixture.Sign(payload));
        await Assert.That(valid.StatusCode).IsEqualTo(HttpStatusCode.OK);
        payload["aud"] = "approved-service";
        using var denied = await fixture.PostAsync(fixture.Sign(payload));
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(fixture.Revision).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnrestrictedClientPolicyAllowsAbsentClientButRejectsMalformedClient(bool legacy)
    {
        await using var fixture = new KeycloakTokenContextFixture();
        await fixture.InitializeAsync(legacy);
        var payload = fixture.ValidPayload();
        payload.Remove("azp");
        using var absent = await fixture.PostAsync(fixture.Sign(payload));
        await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.OK);
        foreach (object? invalid in new object?[] { 42, null, "", new[] { "islamu-event-blazor" } })
        {
            payload["azp"] = invalid;
            using var malformed = await fixture.PostAsync(fixture.Sign(payload));
            await Assert.That(malformed.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(fixture.Revision).IsEqualTo(1);
        }
        payload["azp"] = "unlisted-but-unrestricted-client";
        using var unrestricted = await fixture.PostAsync(fixture.Sign(payload));
        await Assert.That(unrestricted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string duplicate = JsonSerializer.Serialize(payload)[..^1] + ",\"azp\":\"unlisted-but-unrestricted-client\"}";
        using var duplicated = await fixture.PostAsync(fixture.SignJson(duplicate));
        await Assert.That(duplicated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(fixture.Revision).IsEqualTo(2);
    }

    [Test]
    public async Task ConfiguredApiAudienceReplacesRatherThanAddsDefaultAudience()
    {
        await using var fixture = new KeycloakTokenContextFixture();
        await fixture.InitializeAsync(audience: "deployment-api");
        var payload = fixture.ValidPayload();
        using var oldAudience = await fixture.PostAsync(fixture.Sign(payload));
        await Assert.That(oldAudience.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(fixture.Revision).IsEqualTo(0);
        payload["aud"] = "deployment-api";
        using var valid = await fixture.PostAsync(fixture.Sign(payload));
        await Assert.That(valid.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fixture.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task NormalProviderLifetimeRetainsFiveMinuteSkew()
    {
        await using var fixture = new KeycloakTokenContextFixture();
        await fixture.InitializeAsync();
        var payload = fixture.ValidPayload();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        payload["iat"] = now - 3600;
        payload["nbf"] = now - 3600;
        payload["exp"] = now - 60;
        using var response = await fixture.PostAsync(fixture.Sign(payload));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fixture.Revision).IsEqualTo(1);
    }
}
