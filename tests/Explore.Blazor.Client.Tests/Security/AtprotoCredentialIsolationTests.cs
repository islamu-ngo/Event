namespace Explore.Blazor.Client.Tests.Security;

public sealed class AtprotoCredentialIsolationTests
{
    private static readonly string[] ForbiddenBrowserSymbols =
    [
        "BffAtprotoSessionBridgeRequest",
        "BffAtprotoSessionBridgeResponse",
        "AtprotoCurrentSessionBridgeResponse",
        "AtprotoOAuthFlowSeed",
        "AtprotoOAuthSession",
        "AtprotoSessionBridge",
        "AtprotoSessionBootstrap",
        "BootstrapAtprotoSessionAsync",
        "GetCurrentAtprotoSessionAsync",
        "DeleteCurrentAtprotoSessionAsync",
        "/api/auth/atproto/session",
        "OAuthSession",
        "OAuthSessionData",
        "OAuthClientKeyId",
        "DPoP",
        "DpopPrivateKey",
        "PdsAccessToken",
        "PdsRefreshToken",
        "AccessJwt",
        "RefreshJwt",
        "PrivateJwk",
        "PrivateJwks",
        "ExpectedPdsUri",
        "SessionCiphertext",
        "CreateUserAuthenticationTokenDto",
        "UpdateUserAuthenticationTokenDto",
        "CreateUserAuthenticationTokenAsync",
        "UpdateUserAuthenticationTokenAsync",
        "CreateAtprotoRecordDto",
        "UpdateAtprotoRecordDto",
        "CreateAtprotoRecordEntryAsync",
        "UpdateAtprotoRecordEntryAsync",
        "DeleteAtprotoRecordEntryAsync"
    ];

    [Test]
    public async Task GeneratedClientPublicSurfaceExcludesAtprotoCredentialMembers()
    {
        var publicSurface = typeof(Explore.Blazor.Client.Clients.IPublicExperienceClient).Assembly
            .GetTypes()
            .Where(type => string.Equals(type.Namespace, "Explore.Blazor.Client.Clients", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}").Prepend(type.Name))
            .ToArray();

        foreach (var forbidden in ForbiddenBrowserSymbols)
        {
            await Assert.That(publicSurface.Any(member => member.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
                .IsFalse()
                .Because($"The generated public surface must not expose {forbidden}.");
        }
    }

}
