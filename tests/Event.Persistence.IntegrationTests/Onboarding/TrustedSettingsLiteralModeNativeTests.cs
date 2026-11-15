using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Onboarding;

[ClassDataSource<AuthenticationUnusualLoginSourceTests.UnusualGlobalFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class TrustedSettingsLiteralModeNativeTests(
    AuthenticationUnusualLoginSourceTests.UnusualGlobalFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnusualGlobalLoginRetainsTrustedSettingsAdmissionAcrossLiteralModes(bool standardStrings)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        var enrollment = fixture.NativeQueueEnrollment;
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var installer = new ExploreDbContext(options.Options);
        installer.BindTenantLease();
        await installer.Database.OpenConnectionAsync(token);
        await PostgresSetupLiveStoragePermissions.ApplyAsync(installer, enrollment, token);
        await PostgresProvisioningPolicyPermissions.ApplyAsync(installer, enrollment, token);
        string mode = standardStrings ? "on" : "off";
        await installer.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_catalog.set_config('standard_conforming_strings', {mode}, false)", token);

        await PostgresSetupLiveStoragePermissions.ApplyTrustedSettingsAsync(installer, enrollment, token);
        await PostgresSetupLiveStoragePermissions.ApplyTrustedSettingsAsync(installer, enrollment, token);

        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var original = await factory.CreateDbContextAsync(token);
        await original.Database.OpenConnectionAsync(token);
        await original.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_catalog.set_config('standard_conforming_strings', {mode}, false)", token);
        var contextId = original.ContextId;
        var connection = RelationalConnectionIdentity.Capture(original);
        int? backend = connection.BackendProcessId;

        await PostgresSetupLiveStoragePermissions.RequireTrustedSettingsCurrentAsync(original, enrollment, token);

        string login = await original.Database.SqlQueryRaw<string>(
            "SELECT current_user::text AS \"Value\" WHERE current_user=session_user").SingleAsync(token);
        await Assert.That(login).IsEqualTo(enrollment.GlobalLogin);
        await Assert.That(original.ContextId).IsEqualTo(contextId);
        await Assert.That(connection.Matches(original)).IsTrue();
        await Assert.That(connection.BackendProcessId).IsEqualTo(backend);
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
    }
}
