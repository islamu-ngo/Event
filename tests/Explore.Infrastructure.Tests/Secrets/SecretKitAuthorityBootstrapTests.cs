using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitAuthorityBootstrapTests
{
    [Test]
    public async Task NativeAvailableGroupRetainsOneReadPerSelection()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string material = Guid.NewGuid().ToString("N");
        authority.Values["DATABASE_PASSWORD"] = material;
        using var bootstrap = NativeConfiguration(authority);
        SecretAcquisitionEntry[] entries =
        [
            new("/database", "DATABASE_PASSWORD", "Database:Password"),
            new("/database", "DATABASE_HOST", "Database:Host")
        ];
        await using var owner = await SecretAuthorityBootstrap.OpenAsync(
            bootstrap, "Testing", SecretConsumerRole.Migration, entries, new());
        var result = await owner.PrepareAvailableGroupAsync(entries);
        await Assert.That(result.IsSuccess).IsTrue();
        using var lease = result.Value;
        await Assert.That(lease.Configuration["Database:Password"]).IsEqualTo(material);
        await Assert.That(lease.Configuration["Database:Host"]).IsNull();
        await Assert.That(authority.Reads).IsEqualTo(2);
    }

    [Test]
    [Arguments(401)]
    [Arguments(403)]
    [Arguments(503)]
    public async Task NativeAuthorityFailuresCannotBecomeOptionalAbsence(int statusCode)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.ReadStatusCode = statusCode;
        using var bootstrap = NativeConfiguration(authority);
        SecretAcquisitionEntry[] entries = [new("/database", "DATABASE_PASSWORD", "Database:Password")];
        await using var owner = await SecretAuthorityBootstrap.OpenAsync(
            bootstrap, "Testing", SecretConsumerRole.Migration, entries, new());
        var result = await owner.PrepareAvailableGroupAsync(entries);
        if (result.IsSuccess)
            result.Value.Dispose();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Status == SecretKit.Core.SecretStatus.Absent).IsFalse();
    }

    [Test]
    public async Task AvailableGroupExportsPresentSelectionsWithoutRescuingAbsentOnes()
    {
        string value = Guid.NewGuid().ToString("N");
        using var authority = Configuration(new() { ["DATABASE_PROVIDER"] = value });
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Migration), authority);
        var result = await owner.PrepareAvailableGroupAsync(
            [new("/database", "DATABASE_PROVIDER", "Database:Provider"),
             new("/database", "DATABASE_PASSWORD", "Database:Password")]);
        await Assert.That(result.IsSuccess).IsTrue();
        using var lease = result.Value;
        await Assert.That(lease.Configuration["Database:Provider"]).IsEqualTo(value);
        await Assert.That(lease.Configuration["Database:Password"]).IsNull();
    }

    [Test]
    public async Task ExplicitEmptyAvailableMemberRejectsTheWholeGroup()
    {
        using var authority = Configuration(new()
        {
            ["DATABASE_PROVIDER"] = Guid.NewGuid().ToString("N"),
            ["DATABASE_PASSWORD"] = string.Empty
        });
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Migration), authority);
        var result = await owner.PrepareAvailableGroupAsync(
            [new("/database", "DATABASE_PROVIDER", "Database:Provider"),
             new("/database", "DATABASE_PASSWORD", "Database:Password")]);
        if (result.IsSuccess)
            result.Value.Dispose();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Status).IsEqualTo(SecretKit.Core.SecretStatus.Invalid);
    }

    [Test]
    public async Task NoAvailableMembersCannotPublishAnEmptyGeneration()
    {
        using var authority = Configuration(new());
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Migration), authority);
        var result = await owner.PrepareAvailableGroupAsync(
            [new("/database", "DATABASE_PROVIDER", "Database:Provider")]);
        if (result.IsSuccess)
            result.Value.Dispose();
        await Assert.That(result.Status).IsEqualTo(SecretKit.Core.SecretStatus.Absent);
    }

    [Test]
    public async Task ConfiguredNativeOwnerSharesOneLoginAcrossPreparedGroups()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string material = Guid.NewGuid().ToString("N");
        authority.Values["DATABASE_PASSWORD"] = material;
        using var bootstrap = NativeConfiguration(authority);
        SecretAcquisitionEntry[] entries = [new("/database", "DATABASE_PASSWORD", "Database:Password")];
        var attempt = new SecretAuthorityLoginAttempt();
        await using var owner = await SecretAuthorityBootstrap.OpenAsync(
            bootstrap, "Testing", SecretConsumerRole.Migration, entries, attempt);
        var first = await owner.PrepareGroupAsync(entries);
        await Assert.That(first.IsSuccess).IsTrue();
        using var firstLease = first.Value;
        var second = await owner.PrepareGroupAsync(entries);
        await Assert.That(second.IsSuccess).IsTrue();
        using var secondLease = second.Value;
        await Assert.That(secondLease.Configuration["Database:Password"]).IsEqualTo(material);
        await Assert.That(authority.Logins).IsEqualTo(1);
        await Assert.That(attempt.Receipt).IsNotNull();
    }

    [Test]
    [Arguments(null, null)]
    [Arguments("Finite", "600")]
    [Arguments("Periodic", null)]
    [Arguments("0", null)]
    public async Task MissingOrContradictoryTokenPolicyRejectsBeforeLogin(string? lifetime, string? period)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        using var bootstrap = NativeConfiguration(authority);
        bootstrap["SecretProvider:Infisical:TokenLifetime"] = lifetime;
        bootstrap["SecretProvider:Infisical:TokenPeriodSeconds"] = period;
        Func<Task> open = async () =>
        {
            await using var owner = await SecretAuthorityBootstrap.OpenAsync(
                bootstrap, "Testing", SecretConsumerRole.Migration,
                [new("/database", "DATABASE_PASSWORD", "Database:Password")], new());
        };
        await Assert.That(open).Throws<InvalidOperationException>();
        await Assert.That(authority.Logins).IsEqualTo(0);
    }

    [Test]
    public async Task SelectedTestingAuthorityRemainsIsolatedFromBootstrapValues()
    {
        string selectedValue = Guid.NewGuid().ToString("N");
        using var bootstrap = Configuration(new()
        {
            ["SECRET_PROVIDER"] = "Environment",
            ["DATABASE_PASSWORD"] = Guid.NewGuid().ToString("N")
        });
        using var selected = Configuration(new() { ["DATABASE_PASSWORD"] = selectedValue });
        SecretAcquisitionEntry[] entries = [new("/database", "DATABASE_PASSWORD", "Database:Password")];
        await using var owner = await SecretAuthorityBootstrap.OpenAsync(
            bootstrap, "Testing", SecretConsumerRole.Migration, entries, new(), selected);
        var prepared = await owner.PrepareGroupAsync(entries);
        await Assert.That(prepared.IsSuccess).IsTrue();
        using var lease = prepared.Value;
        await Assert.That(lease.Configuration["Database:Password"]).IsEqualTo(selectedValue);
    }

    [Test]
    public async Task PreCanceledBootstrapCannotOpenAnAuthority()
    {
        using var bootstrap = Configuration(new() { ["SECRET_PROVIDER"] = "Environment" });
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Func<Task> open = async () =>
        {
            await using var owner = await SecretAuthorityBootstrap.OpenAsync(
                bootstrap, "Testing", SecretConsumerRole.Migration,
                [new("/database", "DATABASE_PASSWORD", "Database:Password")], new(),
                cancellationToken: canceled.Token);
        };
        await Assert.That(open).Throws<OperationCanceledException>();
    }

    [Test]
    [Arguments("Production", "Environment", "/runtime/bff/oidc/google")]
    [Arguments("Production", "UserSecrets", "/database")]
    [Arguments("Testing", "Environment", "/runtime/bff/oidc/google")]
    public async Task InvalidPurposeOrAuthorityCannotBeOpened(
        string environment, string provider, string folder)
    {
        using var bootstrap = Configuration(new() { ["SECRET_PROVIDER"] = provider });
        Func<Task> open = async () =>
        {
            await using var owner = await SecretAuthorityBootstrap.OpenAsync(
                bootstrap, environment, SecretConsumerRole.Migration,
                [new(folder, "PASSWORD", "Database:Password")], new());
        };
        await Assert.That(open).Throws<InvalidOperationException>();
    }

    private static ConfigurationRoot Configuration(Dictionary<string, string?> values) =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ConfigurationRoot NativeConfiguration(SecretKitHttpAuthority authority) => Configuration(new()
    {
        ["SECRET_PROVIDER"] = "Infisical",
        ["INFISICAL_URL"] = authority.Origin.AbsoluteUri,
        ["INFISICAL_PROJECT_ID"] = "project",
        ["INFISICAL_ENV"] = "testing",
        ["INFISICAL_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
        ["INFISICAL_CLIENT_SECRET"] = Guid.NewGuid().ToString("N"),
        ["SecretProvider:Infisical:TokenLifetime"] = "Finite"
    });
}
