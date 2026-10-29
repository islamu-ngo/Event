using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Services;

[NotInParallel]
public sealed class AuthenticationCredentialProfileReaderTests
{
    [Test]
    [Arguments(SecretProviderType.Environment, "Production", AuthenticationCredentialCutover.CredentialAuthority.Environment)]
    [Arguments(SecretProviderType.Infisical, "Production", AuthenticationCredentialCutover.CredentialAuthority.Infisical)]
    [Arguments(SecretProviderType.UserSecrets, "Development", AuthenticationCredentialCutover.CredentialAuthority.UserSecrets)]
    [Arguments(SecretProviderType.UserSecrets, "Testing", AuthenticationCredentialCutover.CredentialAuthority.UserSecrets)]
    public async Task SelectedProfileUsesIndependentDeploymentRevision(
        SecretProviderType provider, string environmentName, AuthenticationCredentialCutover.CredentialAuthority expected)
    {
        Guid revision = Guid.CreateVersion7();
        await WithRevisionAsync(revision.ToString("D"), async () =>
        {
            var profile = CreateReader(provider, environmentName).ReadCurrent();
            await Assert.That(profile.Authority).IsEqualTo(expected);
            await Assert.That(profile.Revision).IsEqualTo(revision);
        });
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not-a-uuid")]
    [Arguments("00000000-0000-0000-0000-000000000000")]
    public Task MissingOrInvalidRevisionCannotAuthorizeMaintenance(string? revision) =>
        WithRevisionAsync(revision, async () =>
        {
            var reader = CreateReader(SecretProviderType.Infisical, "Production");
            await Assert.That(() => reader.ReadCurrent()).Throws<InvalidOperationException>();
        });

    [Test]
    [Arguments(SecretProviderType.UserSecrets, "Production")]
    [Arguments(SecretProviderType.UserSecrets, "Staging")]
    [Arguments(SecretProviderType.Unspecified, "Testing")]
    public Task IneligibleAuthorityCannotAuthorizeMaintenance(SecretProviderType provider, string environmentName) =>
        WithRevisionAsync(Guid.CreateVersion7().ToString("D"), async () =>
        {
            var reader = CreateReader(provider, environmentName);
            await Assert.That(() => reader.ReadCurrent()).Throws<InvalidOperationException>();
            await Assert.That(() => reader.ObserveCurrent()).Throws<InvalidOperationException>();
        });

    [Test]
    public Task ChangedDeploymentRevisionIsNotHiddenByCachedProfile() =>
        WithRevisionAsync(Guid.CreateVersion7().ToString("D"), async () =>
        {
            var reader = CreateReader(SecretProviderType.Environment, "Production");
            var original = reader.ReadCurrent();
            Guid replacement = Guid.CreateVersion7();
            Environment.SetEnvironmentVariable(
                AuthenticationCredentialProfileReader.RevisionEnvironmentVariable, replacement.ToString("D"));
            await Assert.That(reader.ReadCurrent().Revision).IsEqualTo(replacement);
            await Assert.That(reader.ReadCurrent().Revision).IsNotEqualTo(original.Revision);
        });

    [Test]
    [Arguments(SecretProviderType.Environment, "Production", AuthenticationCredentialCutover.CredentialAuthority.Environment)]
    [Arguments(SecretProviderType.Infisical, "Production", AuthenticationCredentialCutover.CredentialAuthority.Infisical)]
    [Arguments(SecretProviderType.UserSecrets, "Testing", AuthenticationCredentialCutover.CredentialAuthority.UserSecrets)]
    public Task AbsentRevisionObservationRetainsSelectedAuthorityWithoutAuthorizingMaintenance(
        SecretProviderType provider, string environmentName, AuthenticationCredentialCutover.CredentialAuthority expected) =>
        WithRevisionAsync(null, async () =>
        {
            var reader = CreateReader(provider, environmentName);
            var observation = reader.ObserveCurrent();
            await Assert.That(observation.Authority).IsEqualTo(expected);
            await Assert.That(observation.Revision).IsNull();
            await Assert.That(() => reader.ReadCurrent()).Throws<InvalidOperationException>();
        });

    [Test]
    [Arguments("not-a-uuid")]
    [Arguments(" ")]
    [Arguments("00000000-0000-0000-0000-000000000000")]
    public Task MalformedConfiguredRevisionCannotBecomeAnAbsentObservation(string revision) =>
        WithRevisionAsync(revision, async () =>
        {
            var reader = CreateReader(SecretProviderType.Environment, "Production");
            await Assert.That(() => reader.ObserveCurrent()).Throws<InvalidOperationException>();
        });

    [Test]
    public Task InstallingAProfileChangesTheRealAbsentObservation() =>
        WithRevisionAsync(null, async () =>
        {
            var reader = CreateReader(SecretProviderType.Environment, "Production");
            var original = reader.ObserveCurrent();
            Guid revision = Guid.CreateVersion7();
            Environment.SetEnvironmentVariable(AuthenticationCredentialProfileReader.RevisionEnvironmentVariable, revision.ToString("D"));
            var current = reader.ObserveCurrent();
            await Assert.That(original.Revision).IsNull();
            await Assert.That(current.Authority).IsEqualTo(original.Authority);
            await Assert.That(current.Revision).IsEqualTo(revision);
            await Assert.That(current).IsNotEqualTo(original);
        });

    private static AuthenticationCredentialProfileReader CreateReader(
        SecretProviderType provider, string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new(Options.Create(new SecretProviderOptions { Provider = provider }), environment);
    }

    private static async Task WithRevisionAsync(string? revision, Func<Task> test)
    {
        string variable = AuthenticationCredentialProfileReader.RevisionEnvironmentVariable;
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, revision);
            await test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
