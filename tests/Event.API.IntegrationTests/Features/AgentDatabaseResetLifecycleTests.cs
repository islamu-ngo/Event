using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.API.Hosting;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class AgentDatabaseResetLifecycleTests
{
    [After(Assembly)]
    public static async Task DisposeResetDatabase() => await AgentBrowserPersonaFixture.DisposeDatabaseAsync();

    [Test]
    public async Task NonlocalStoragePolicyRejectsBeforeAnyPersonaWrite()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await using (var database = fixture.CreateDatabase())
        {
            var storage = await database.SystemSettings.SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Storage.Provider);
            storage.Value = $"\"{StorageProviders.S3Compatible}\"";
            await database.SaveChangesAsync();
        }

        try
        {
            await fixture.RunAsync();
            throw new InvalidOperationException("agent_browser_storage_test_did_not_reject");
        }
        catch (InvalidOperationException exception)
        {
            await Assert.That(exception.Message).IsEqualTo("agent_browser_storage_provider_unsupported");
        }

        await using var unchanged = fixture.CreateDatabase();
        await Assert.That(await unchanged.Users.CountAsync()).IsEqualTo(0);
        using var client = fixture.CreateHostClient("default");
        using var response = await client.GetAsync("/api/user");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task NonlocalStoragePolicyBlocksResetWithoutPurgingExistingPersonas()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            var storage = await database.SystemSettings.SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Storage.Provider);
            storage.Value = $"\"{StorageProviders.S3Compatible}\"";
            await database.SaveChangesAsync();
        }

        await Assert.That(await ResetThroughPipeAsync()).IsEqualTo("failed");
        await using var preserved = fixture.CreateDatabase();
        await Assert.That(await preserved.Users.CountAsync()).IsEqualTo(6);
        using var client = fixture.CreateHostClient("default");
        using var response = await client.GetAsync("/api/user");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task UnlockedTenantStorageOverrideBlocksResetWithoutPurgingExistingPersonas()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await fixture.RunAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
        var policy = scope.ServiceProvider.GetRequiredService<IStoragePolicyResolver>();
        await using var database = fixture.CreateDatabase();
        Guid actorId = await database.Users
            .Where(user => user.Pii.Email == AgentBrowserPersonaCatalog.Administrator.Email)
            .Select(user => user.Id).SingleAsync();
        await settings.SetValueAsync(GovernanceSettingKeys.TenantDelegation.LockStorage, "false",
            SettingScope.Instance, Guid.Empty, actorId);
        await settings.SetValueAsync(GovernanceSettingKeys.Storage.Provider,
            $"\"{StorageProviders.S3Compatible}\"", SettingScope.Tenant,
            AgentBrowserPersonaCatalog.NegativeTenantId, actorId);

        await Assert.That((await policy.ResolveAsync(null)).Provider).IsEqualTo(StorageProviders.Local);
        await Assert.That((await policy.ResolveAsync(AgentBrowserPersonaCatalog.NegativeTenantId)).Provider)
            .IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(await ResetThroughPipeAsync()).IsEqualTo("failed");
        await Assert.That(await database.Users.CountAsync()).IsEqualTo(6);
        using var client = fixture.CreateHostClient("default");
        using var response = await client.GetAsync("/api/user");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task OwnerControlPathRecreatesNativeCredentialsAndInvalidatesPriorSessions()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await fixture.RunAsync();
        string previousToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Attendee);
        await fixture.ChangePasswordProfileAndRevokeGrantAsync();
        await fixture.CreateSameTenantUnrelatedEventAsync();
        string before = await fixture.DatabasePreservationFingerprintAsync();

        // The native tool is the actual owner-scoped external surface, not a direct coordinator call.
        await ResetThroughToolAsync();
        await fixture.AssertReadyAsync();
        await Assert.That(await fixture.DatabasePreservationFingerprintAsync()).IsEqualTo(before);
        await using (var database = fixture.CreateDatabase())
        {
            await Assert.That(await database.Events.CountAsync()).IsEqualTo(2);
            await Assert.That(await database.Users.CountAsync()).IsEqualTo(6);
            await Assert.That(await database.TenantUserRoleGrants.AnyAsync(row => row.RevokedAt != null)).IsFalse();
            await database.Database.ExecuteSqlRawAsync("""
                DO $$ BEGIN
                  IF NOT ST_DWithin(ST_SetSRID(ST_Point(0, 0), 4326)::geography,
                      ST_SetSRID(ST_Point(0, 0.001), 4326)::geography, 200)
                  THEN RAISE EXCEPTION 'agent_postgis_expression_failed'; END IF;
                END $$;
                """);
        }
        using var oldSession = fixture.CreateTenantClient("default", previousToken);
        using var stale = await oldSession.GetAsync("/api/user");
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        foreach (var persona in AgentBrowserPersonaCatalog.All)
            _ = await fixture.LoginAsync(persona);
        string organizerToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Organizer);
        using var negative = fixture.CreateTenantClient("agent-negative", organizerToken);
        using var denied = await negative.DeleteAsync($"/api/event/{AgentBrowserPersonaCatalog.NegativeEventId:D}");
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AspireCommandPipeAndProcessOwnerPipeReachTheSameMaintenanceOwner()
    {
        string commandPipe = $"islamu-agent-database-reset-{Guid.NewGuid():N}";
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(
            enableReset: true, resetPipeName: commandPipe);
        await fixture.RunAsync();

        await Assert.That((await ResetThroughPipeAsync(commandPipe)).StartsWith("ready ", StringComparison.Ordinal))
            .IsTrue();
        await fixture.AssertReadyAsync();
        await Assert.That((await ResetThroughPipeAsync()).StartsWith("ready ", StringComparison.Ordinal))
            .IsTrue();
        await fixture.AssertReadyAsync();
    }

    [Test]
    public async Task CompetingPostgresOwnerLockDoesNotPurgeAndKeepsReadinessClosedUntilRetry()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await fixture.RunAsync();
        Guid marker;
        await using (var database = fixture.CreateDatabase())
            marker = (await database.InstanceBootstrapStates.SingleAsync()).Id;
        await using (var competitor = await fixture.AcquireProvisioningLockAsync())
        {
            await Assert.That(await ResetThroughPipeAsync()).IsEqualTo("failed");
            await using var database = fixture.CreateDatabase();
            await Assert.That((await database.InstanceBootstrapStates.SingleAsync()).Id).IsEqualTo(marker);
            await Assert.That(await database.Users.CountAsync()).IsEqualTo(6);
            using var client = fixture.CreateHostClient("default");
            using var readiness = await client.GetAsync("/health/ready");
            await Assert.That(readiness.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
        await Assert.That((await ResetThroughPipeAsync()).StartsWith("ready ", StringComparison.Ordinal)).IsTrue();
        await fixture.AssertReadyAsync();
    }

    [Test]
    [Arguments("receipt")]
    [Arguments("graph")]
    [Arguments("activation")]
    public async Task PartialNativeLifecycleIsResumedWithoutPurgingCommittedReceiptsAgain(string boundary)
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await fixture.RunAsync();
        fixture.InterruptAt(boundary);
        await Assert.That(await ResetThroughPipeAsync()).IsEqualTo("failed");
        await Assert.That(fixture.InterruptionObserved).IsTrue();
        Guid marker;
        await using (var database = fixture.CreateDatabase())
            marker = (await database.InstanceBootstrapStates.SingleAsync()).Id;
        using var client = fixture.CreateHostClient("default");
        using (var closed = await client.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}"))
            await Assert.That(closed.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That((await ResetThroughPipeAsync()).StartsWith("ready ", StringComparison.Ordinal)).IsTrue();
        await fixture.AssertReadyAsync();
        await using var recovered = fixture.CreateDatabase();
        await Assert.That((await recovered.InstanceBootstrapStates.SingleAsync()).Id).IsEqualTo(marker);
    }

    [Test]
    public async Task UnclassifiedForeignKeyPreventsPurgeWithoutCascadeOrPartialDeletion()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync(enableReset: true);
        await fixture.RunAsync();
        await using var database = fixture.CreateDatabase();
        Guid marker = (await database.InstanceBootstrapStates.SingleAsync()).Id;
        await database.Database.ExecuteSqlRawAsync("CREATE TABLE public.agent_reset_guard (user_id uuid REFERENCES islamu_event.users(id))");
        try
        {
            await Assert.That(await ResetThroughPipeAsync()).IsEqualTo("failed");
            database.ChangeTracker.Clear();
            await Assert.That((await database.InstanceBootstrapStates.SingleAsync()).Id).IsEqualTo(marker);
            await Assert.That(await database.Users.CountAsync()).IsEqualTo(6);
            using var client = fixture.CreateHostClient("default");
            using var closed = await client.GetAsync("/health/ready");
            await Assert.That(closed.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
        finally { await database.Database.ExecuteSqlRawAsync("DROP TABLE public.agent_reset_guard"); }
        await Assert.That((await ResetThroughPipeAsync()).StartsWith("ready ", StringComparison.Ordinal)).IsTrue();
        await fixture.AssertReadyAsync();
    }

    private static async Task ResetThroughToolAsync()
    {
        string root = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "run", "eng/tools/AgentDatabaseReset.cs", "--", "--owner-pid",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "--apply" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("agent_reset_tool_not_started");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(150));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            string result = await stdout;
            Console.WriteLine(result); // Only the tool's value-free outcome and measured duration.
            await Assert.That(process.ExitCode).IsEqualTo(0).Because(await stderr);
            await Assert.That(result.Contains("request-to-ready=", StringComparison.Ordinal)).IsTrue();
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
    }

    internal static async Task<string> ResetThroughPipeAsync(string? controlPipe = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(125));
        await using var pipe = new NamedPipeClientStream(".", controlPipe ?? AgentBrowserResetCoordinator.PipeName(Environment.ProcessId),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token);
        await pipe.WriteAsync("reset\n"u8.ToArray(), deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        using var reader = new StreamReader(pipe);
        return await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("reset_control_closed");
    }
}
