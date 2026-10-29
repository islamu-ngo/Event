using Explore.Domain.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class AuthenticationCredentialProfileReaderTests
{
    [Test]
    [Arguments(SecretAuthorityKind.Environment, AuthenticationCredentialCutover.CredentialAuthority.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets, AuthenticationCredentialCutover.CredentialAuthority.UserSecrets)]
    [Arguments(SecretAuthorityKind.Infisical, AuthenticationCredentialCutover.CredentialAuthority.Infisical)]
    public async Task SelectedProfileUsesIndependentDeploymentRevision(
        SecretAuthorityKind kind, AuthenticationCredentialCutover.CredentialAuthority expected)
    {
        Guid revision = Guid.CreateVersion7();
        using var configuration = Configuration(kind.ToString(), revision.ToString());
        var selection = SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api);
        if (kind == SecretAuthorityKind.Infisical)
        {
            await using var server = await SecretKitHttpAuthority.StartAsync();
            using var credential = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
            var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(selection,
                [new("/api", "API_KEY", "api-key")],
                new(server.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                    InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)),
                credential, new());
            await Assert.That(opened.IsSuccess).IsTrue();
            await using var owner = opened.Value;
            var profile = new AuthenticationCredentialProfileReader(owner).ReadCurrent();
            await Assert.That(profile.Authority).IsEqualTo(expected);
            await Assert.That(profile.Revision).IsEqualTo(revision);
            var observation = new AuthenticationCredentialProfileReader(owner).ObserveCurrent();
            await Assert.That(observation.Authority).IsEqualTo(expected);
            await Assert.That(observation.Revision).IsEqualTo(revision);
            await Assert.That(server.Logins).IsEqualTo(1);
            return;
        }
        await using var local = SecretAuthorityAcquisition.OpenLocal(selection, configuration);
        var selected = new AuthenticationCredentialProfileReader(local).ReadCurrent();
        await Assert.That(selected.Authority).IsEqualTo(expected);
        await Assert.That(selected.Revision).IsEqualTo(revision);
        var selectedObservation = new AuthenticationCredentialProfileReader(local).ObserveCurrent();
        await Assert.That(selectedObservation.Authority).IsEqualTo(expected);
        await Assert.That(selectedObservation.Revision).IsEqualTo(revision);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("not-a-uuid")]
    [Arguments("00000000-0000-0000-0000-000000000000")]
    public async Task MissingOrInvalidRevisionCannotAuthorizeMaintenance(string? revision)
    {
        using var configuration = Configuration("Environment", revision);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api), configuration);
        await Assert.That(() => new AuthenticationCredentialProfileReader(owner).ReadCurrent())
            .Throws<InvalidOperationException>();
        var reader = new AuthenticationCredentialProfileReader(owner);
        if (revision is null)
            await Assert.That(reader.ObserveCurrent().Revision).IsNull();
        else
            await Assert.That(() => reader.ObserveCurrent()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment, AuthenticationCredentialCutover.CredentialAuthority.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets, AuthenticationCredentialCutover.CredentialAuthority.UserSecrets)]
    public async Task AbsentLocalRevisionRetainsSelectedAuthorityWithoutAuthorizingMaintenance(
        SecretAuthorityKind kind, AuthenticationCredentialCutover.CredentialAuthority expected)
    {
        using var configuration = Configuration(kind.ToString(), null);
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api), configuration);
        var reader = new AuthenticationCredentialProfileReader(owner);
        var observation = reader.ObserveCurrent();
        await Assert.That(observation.Authority).IsEqualTo(expected);
        await Assert.That(observation.Revision).IsNull();
        await Assert.That(() => reader.ReadCurrent()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AbsentNativeRevisionRetainsSelectedAuthorityWithoutAuthorizingMaintenance()
    {
        using var configuration = Configuration("Infisical", null);
        await using var server = await SecretKitHttpAuthority.StartAsync();
        using var credential = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api),
            [new("/api", "API_KEY", "api-key")],
            new(server.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)),
            credential, new());
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        var reader = new AuthenticationCredentialProfileReader(owner);
        var observation = reader.ObserveCurrent();
        await Assert.That(observation.Authority).IsEqualTo(AuthenticationCredentialCutover.CredentialAuthority.Infisical);
        await Assert.That(observation.Revision).IsNull();
        await Assert.That(() => reader.ReadCurrent()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReplacingTheSelectedOwnerChangesAGenuineAbsentObservation()
    {
        using var configuration = Configuration("Environment", null);
        await using var original = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api), configuration);
        Guid revision = Guid.CreateVersion7();
        configuration["AUTHENTICATION_CREDENTIAL_PROFILE_REVISION"] = revision.ToString("D");
        await using var replacement = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api), configuration);
        var before = new AuthenticationCredentialProfileReader(original).ObserveCurrent();
        var after = new AuthenticationCredentialProfileReader(replacement).ObserveCurrent();
        await Assert.That(before.Revision).IsNull();
        await Assert.That(after.Authority).IsEqualTo(before.Authority);
        await Assert.That(after.Revision).IsEqualTo(revision);
        await Assert.That(after).IsNotEqualTo(before);
    }

    [Test]
    [Arguments("UserSecrets", "Production")]
    [Arguments("UserSecrets", "Staging")]
    [Arguments("Unspecified", "Testing")]
    public async Task IneligibleAuthorityCannotAuthorizeMaintenance(string provider, string environment)
    {
        using var configuration = Configuration(provider, Guid.CreateVersion7().ToString());
        await Assert.That(() => SecretAuthoritySelection.Select(configuration, environment, SecretConsumerRole.Api))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ProfileIsBoundAtSelectionEvenIfReaderIsConstructedAfterConfigurationChanges()
    {
        Guid original = Guid.CreateVersion7();
        Guid replacement = Guid.CreateVersion7();
        using var configuration = Configuration("Environment", original.ToString());
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api), configuration);
        configuration["AUTHENTICATION_CREDENTIAL_PROFILE_REVISION"] = replacement.ToString();
        var reader = new AuthenticationCredentialProfileReader(owner);
        await Assert.That(reader.ReadCurrent().Revision).IsEqualTo(original);
        await using var restarted = SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api), configuration);
        await Assert.That(new AuthenticationCredentialProfileReader(restarted).ReadCurrent().Revision)
            .IsEqualTo(replacement);
        await owner.DisposeAsync();
        await Assert.That(() => reader.ReadCurrent()).Throws<ObjectDisposedException>();
        await Assert.That(() => reader.ObserveCurrent()).Throws<ObjectDisposedException>();
    }

    private static ConfigurationRoot Configuration(string provider, string? revision) =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = provider,
            ["AUTHENTICATION_CREDENTIAL_PROFILE_REVISION"] = revision
        }).Build();
}
