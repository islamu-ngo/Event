using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class KeycloakRequestCredentialContractTests
{
    [Test]
    public async Task FreshAdministratorPairIsPreservedExactlyForProviderAuthentication()
    {
        string username = $" operator-{Guid.CreateVersion7():N} ";
        string password = $" {Guid.CreateVersion7():N} ";
        var context = Context(username, password);
        await Assert.That(context.AdministratorUsername).IsEqualTo(username);
        await Assert.That(context.AdministratorPassword).IsEqualTo(password);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task IncompleteAdministratorPairCannotBecomeAnApplyContext(bool hasUsername, bool hasPassword)
    {
        string username = hasUsername ? Guid.CreateVersion7().ToString("N") : string.Empty;
        string password = hasPassword ? Guid.CreateVersion7().ToString("N") : string.Empty;
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            _ = Context(username, password);
            return Task.CompletedTask;
        });
    }

    private static KeycloakOperationApplyContext Context(string username, string password) =>
        new(Guid.CreateVersion7(), "operator", 1,
            new KeycloakTarget(Guid.CreateVersion7(), "https://identity.example.test/realms/operators",
                "operators", "event-bff"),
            "approved-digest", null, null, null, null, username, password,
            new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero));
}
