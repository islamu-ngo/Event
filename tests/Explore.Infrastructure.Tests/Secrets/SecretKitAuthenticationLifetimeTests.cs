using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitAuthenticationLifetimeTests
{
    [Test]
    [Arguments(200)]
    [Arguments(503)]
    public async Task ScheduledRenewalUsesOnlyTheSelectedSessionAndNeverReplaysFailure(int statusCode)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.RenewalStatusCode = statusCode;
        authority.BlockRenewal = true;
        var clock = new RenewalClock();
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Combined),
            [new("/database", "DATABASE_PROVIDER", "Database:Provider")],
            new(authority.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)),
            bootstrap, new(), timeProvider: clock);
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        clock.Advance(TimeSpan.FromSeconds(570));
        await owner.StartAuthenticationMaintenanceAsync();
        SecretStatus renewal;
        try
        {
            await authority.RenewalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var joined = owner.AuthenticationSession!.RenewAsync().AsTask();
            authority.RenewalReleased.TrySetResult();
            renewal = await joined.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            authority.RenewalReleased.TrySetResult();
        }
        var token = await owner.AuthenticationSession!.GetTokenAsync(CancellationToken.None);
        if (token.IsSuccess)
            token.Value.Dispose();
        await Assert.That(renewal == SecretStatus.Success).IsEqualTo(statusCode == 200);
        await Assert.That(token.IsSuccess).IsEqualTo(statusCode == 200);
        if (statusCode != 200)
        {
            await Assert.That(token.Status).IsEqualTo(SecretStatus.RecoveryRequired);
            await Assert.That(await owner.AuthenticationSession.RenewAsync())
                .IsEqualTo(SecretStatus.RecoveryRequired);
        }
        await Assert.That(authority.Logins).IsEqualTo(1);
        await Assert.That(authority.Renewals).IsEqualTo(1);
        await Assert.That(authority.Reads).IsEqualTo(0);
    }

    [Test]
    public async Task LocalAuthorityStartsNoAuthenticationWorker()
    {
        using var authority = Configuration(new());
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), authority);
        await owner.StartAuthenticationMaintenanceAsync();
        await Assert.That(owner.AuthenticationMaintenance).IsNull();
    }

    [Test]
    public async Task PreCanceledStartupCannotCreateMaintenance()
    {
        using var authority = Configuration(new());
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), authority);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Func<Task> start = () => owner.StartAuthenticationMaintenanceAsync(canceled.Token);
        await Assert.That(start).Throws<OperationCanceledException>();
        await Assert.That(owner.AuthenticationMaintenance).IsNull();
    }

    [Test]
    public async Task OneNativeSessionHasOneMaintenanceOwnerAndRetiresWithIt()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        using var bootstrap = Configuration(new()
        {
            ["SECRET_PROVIDER"] = "Infisical",
            ["INFISICAL_URL"] = authority.Origin.AbsoluteUri,
            ["INFISICAL_PROJECT_ID"] = "project",
            ["INFISICAL_ENV"] = "testing",
            ["INFISICAL_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
            ["INFISICAL_CLIENT_SECRET"] = Guid.NewGuid().ToString("N"),
            ["SecretProvider:Infisical:TokenLifetime"] = "Finite"
        });
        await using var owner = await SecretAuthorityBootstrap.OpenAsync(
            bootstrap, "Testing", SecretConsumerRole.Combined,
            [new("/database", "DATABASE_PROVIDER", "Database:Provider")], new());
        await owner.StartAuthenticationMaintenanceAsync();
        var helper = owner.AuthenticationMaintenance!;
        await Assert.That(helper).IsNotNull();
        await Assert.That((await helper.CheckHealthAsync(new HealthCheckContext())).Status)
            .IsEqualTo(HealthStatus.Healthy);
        Func<Task> duplicate = () => owner.StartAuthenticationMaintenanceAsync();
        await Assert.That(duplicate).Throws<InvalidOperationException>();
        await Assert.That(owner.AuthenticationMaintenance).IsSameReferenceAs(helper);
        await owner.DisposeAsync();
        await Assert.That((await helper.CheckHealthAsync(new HealthCheckContext())).Status)
            .IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(authority.Logins).IsEqualTo(1);
        await Assert.That(authority.Renewals).IsEqualTo(0);
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    public async Task FinitePolicyAcceptsAnUnconfiguredComposePeriod(string period)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        using var bootstrap = Configuration(new()
        {
            ["SECRET_PROVIDER"] = "Infisical",
            ["INFISICAL_URL"] = authority.Origin.AbsoluteUri,
            ["INFISICAL_PROJECT_ID"] = "project",
            ["INFISICAL_ENV"] = "testing",
            ["INFISICAL_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
            ["INFISICAL_CLIENT_SECRET"] = Guid.NewGuid().ToString("N"),
            ["SecretProvider:Infisical:TokenLifetime"] = "Finite",
            ["SecretProvider:Infisical:TokenPeriodSeconds"] = period
        });
        await using var owner = await SecretAuthorityBootstrap.OpenAsync(
            bootstrap, "Testing", SecretConsumerRole.Api,
            [new("/database", "DATABASE_PROVIDER", "Database:Provider")], new());
        await Assert.That(owner.InfisicalOptions!.Lifetime).IsEqualTo(InfisicalTokenLifetime.Finite);
        await Assert.That(owner.InfisicalOptions.Period).IsNull();
        await Assert.That(authority.Logins).IsEqualTo(1);
    }

    private static ConfigurationRoot Configuration(Dictionary<string, string?> values) =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class RenewalClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }
}
