using Explore.ServiceDefaults.Secrets;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitAuthenticationPolicyTests
{
    [Test]
    [Arguments(61)]
    [Arguments(300)]
    public async Task UnsupportedSessionDeadlineIsRejectedBeforeAuthentication(int seconds)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        using var credential = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var result = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            [new("/api", "API_KEY", "api-key")],
            new(authority.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(seconds)),
            credential, new());
        if (result.IsSuccess)
            await result.Value.DisposeAsync();
        await Assert.That(result.Status).IsEqualTo(SecretStatus.Invalid);
        await Assert.That(authority.Logins).IsEqualTo(0);
    }
}
