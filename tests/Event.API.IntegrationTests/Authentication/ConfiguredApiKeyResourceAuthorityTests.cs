using System.Security.Claims;
using Explore.API.Authentication;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Services;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.API.IntegrationTests.Authentication;

public sealed class ConfiguredApiKeyResourceAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("disabled")]
    [Arguments("expired")]
    [Arguments("duplicate")]
    [Arguments("owner-kind")]
    [Arguments("owner-id")]
    public async Task UnqualifiedCurrentConfiguredSourcesAreExcluded(string change)
    {
        ApiKeyClientDescriptor client = Client();
        var current = new ApiKeyAuthenticationOptions { Clients = [client] };
        switch (change)
        {
            case "disabled":
                client.IsActive = false;
                break;
            case "expired":
                client.ExpiresAtUtc = Now;
                break;
            case "duplicate":
                ApiKeyClientDescriptor duplicate = Client();
                duplicate.KeyId = client.KeyId;
                current.Clients.Add(duplicate);
                break;
            case "owner-kind":
                client.OwnerType = "999";
                break;
            case "owner-id":
                client.OwnerId = Guid.Empty.ToString();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
        var reader = Reader(() => current);
        await Assert.That(await reader.ReadAsync(client.KeyId, Now, default)).IsNull();
    }

    [Test]
    public async Task NamedAuthenticationSourceIsReadAgainAfterReplacement()
    {
        ApiKeyClientDescriptor client = Client();
        var current = new ApiKeyAuthenticationOptions { Clients = [client] };
        var reader = Reader(() => current);
        ApiKeyPrincipalContext? original = await reader.ReadAsync(client.KeyId, Now, default);
        await Assert.That(original).IsNotNull();
        await Assert.That(original!.CredentialSource).IsEqualTo(ApiKeyCredentialSource.Configured);
        await Assert.That(original.OwnerId).IsEqualTo(Guid.Parse(client.OwnerId));
        current = new ApiKeyAuthenticationOptions();
        await Assert.That(await reader.ReadAsync(client.KeyId, Now, default)).IsNull();
    }

    [Test]
    public async Task CapturedMetadataDoesNotAliasMutableSourceScopes()
    {
        ApiKeyClientDescriptor client = Client();
        var reader = Reader(() => new() { Clients = [client] });
        ApiKeyPrincipalContext? original = await reader.ReadAsync(client.KeyId, Now, default);
        await Assert.That(original).IsNotNull();
        client.Scopes.Clear();
        await Assert.That(original!.Scopes).Contains("events:read");
        ApiKeyPrincipalContext? refreshed = await reader.ReadAsync(client.KeyId, Now, default);
        await Assert.That(refreshed).IsNotNull();
        await Assert.That(refreshed!.Scopes).IsEmpty();
    }

    [Test]
    [Arguments("persisted", ApiKeyCredentialSource.Persisted)]
    [Arguments("configured", ApiKeyCredentialSource.Configured)]
    public async Task VerifiedPrincipalRetainsCredentialAuthority(string source, ApiKeyCredentialSource expected)
    {
        ClaimsPrincipal principal = Principal([source]);
        await Assert.That(principal.TryGetApiKeyPrincipalContext()!.CredentialSource).IsEqualTo(expected);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("unknown")]
    [Arguments("duplicate")]
    public async Task MissingOrAmbiguousCredentialAuthorityRemainsUnqualified(string change)
    {
        string[] sources = change switch
        {
            "missing" => [],
            "unknown" => ["unknown"],
            "duplicate" => ["persisted", "configured"],
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        ApiKeyPrincipalContext? context = Principal(sources).TryGetApiKeyPrincipalContext();
        await Assert.That(context).IsNotNull();
        await Assert.That(context!.CredentialSource).IsEqualTo(ApiKeyCredentialSource.Unqualified);
    }

    private static ConfiguredApiKeyResourceAuthorityReader Reader(Func<ApiKeyAuthenticationOptions> current)
    {
        var options = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        options.CurrentValue.Returns(new ApiKeyAuthenticationOptions());
        options.Get(ApiAuthenticationSchemeNames.ApiKey).Returns(_ => current());
        return new(options);
    }

    private static ApiKeyClientDescriptor Client() => new()
    {
        KeyId = ApiKeyHashing.CreateKeyId(),
        TenantId = Guid.CreateVersion7(),
        OwnerType = nameof(ExternalApiKeyOwnerType.User),
        OwnerId = Guid.CreateVersion7().ToString(),
        Scopes = ["events:read"],
        ExpiresAtUtc = Now.AddDays(1)
    };

    private static ClaimsPrincipal Principal(string[] sources)
    {
        Claim[] claims =
        [
            new(ApiAuthenticationClaimTypes.AuthMethod, "api_key"),
            new(ApiAuthenticationClaimTypes.ApiKeyId, ApiKeyHashing.CreateKeyId()),
            new(ApiAuthenticationClaimTypes.OwnerType, nameof(ExternalApiKeyOwnerType.User)),
            new(ApiAuthenticationClaimTypes.OwnerId, Guid.CreateVersion7().ToString()),
            .. sources.Select(source => new Claim(ApiAuthenticationClaimTypes.CredentialSource, source))
        ];
        return new(new ClaimsIdentity(claims, ApiAuthenticationSchemeNames.ApiKey));
    }
}
