using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeControlPlaneHttpTests
{
    private const string Root = "/api/admin/control-plane";

    [Test]
    public async Task Controllers_CloseEveryControlPlanePortWithoutChangingTheTenantCreationOwner()
    {
        Type[] controllers = [typeof(ControlPlaneController), typeof(ControlPlaneTenantPlanController),
            typeof(ControlPlaneTenantConfigurationController), typeof(ControlPlaneTenantLifecycleController),
            typeof(ControlPlaneDeploymentModeController)];
        Type[] parameters = controllers.SelectMany(type => type.GetConstructors().Single().GetParameters())
            .Select(parameter => parameter.ParameterType).ToArray();
        Type[] requests = parameters.Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                 type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .Select(type => type.GetGenericArguments()[0])
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features.ControlPlane.", StringComparison.Ordinal) == true)
            .ToArray();
        await Assert.That(requests.Length).IsEqualTo(26);
        await Assert.That(requests.Distinct().Count()).IsEqualTo(26);
        await Assert.That(parameters.Count(type => type == typeof(MediatR.IMediator))).IsEqualTo(1);
        foreach (Type request in requests)
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
    }

    [Test]
    public async Task InstanceAuthority_GatesReadsAndPreviewWhileScopedQueriesFailClosed()
    {
        await using var factory = await ControlPlaneFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        using var member = Client(factory, seed.MemberId, forgedAdmin: true);
        using var anonymous = Client(factory);
        string[] paths = ["/overview", "/domains", "/operations", "/tenants", "/plans", "/deployment-mode",
            $"/tenants/{seed.TenantId}", $"/tenants/{seed.TenantId}/effective-configuration"];
        foreach (string path in paths)
        {
            using var deniedAnonymous = await anonymous.GetAsync(Root + path);
            await Assert.That(deniedAnonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            using var deniedMember = await member.GetAsync(Root + path);
            await ProblemAsync(deniedMember, HttpStatusCode.Forbidden);
            using var allowed = await admin.GetAsync(Root + path);
            await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(path);
            await Assert.That((await JsonAsync(allowed)).TryGetProperty("_links", out _)).IsTrue();
        }
        using (var missing = await admin.GetAsync(Root + "/plans/missing"))
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var missing = await admin.GetAsync(Root + $"/tenants/{seed.TenantId}/plan-assignment"))
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var missing = await admin.GetAsync(Root + $"/tenants/{Guid.CreateVersion7()}"))
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var denied = await member.PostAsJsonAsync(Root + "/plans/validate", Draft()))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var validation = await admin.PostAsJsonAsync(Root + "/plans/validate",
            Draft() with { Pricing = new(-1, "EUR", "monthly") }))
        {
            await Assert.That(validation.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var result = (await validation.Content.ReadFromJsonAsync<TenantPlanValidationResult>())!;
            await Assert.That(result.Errors.Select(error => error.Code)).Contains(TenantPlanValidationErrorCodes.NegativePrice);
        }
        using (var preview = await admin.PostAsJsonAsync(Root + "/plans/preview-diff",
            new PreviewTenantPlanDiffRequest(new([]), Draft())))
        {
            await Assert.That(preview.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var changes = (await JsonAsync(preview)).GetProperty("settingChanges");
            await Assert.That(changes.GetArrayLength()).IsEqualTo(1);
            await Assert.That(changes[0].GetProperty("changeType").GetString()).IsEqualTo("Added");
        }
        using var scope = factory.Services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetControlPlaneTenantPlanListQuery, IReadOnlyList<ControlPlaneTenantPlanListItemDto>>>();
        await Assert.That(query.GetType().Namespace).IsEqualTo("Explore.Application.Operations.Decorators");
        await Assert.That(async () => await query.QueryAsync(new(), default))
            .Throws<Explore.Application.Exceptions.AuthorizationException>();
    }

    [Test]
    public async Task PlanAuthoring_PreservesPublishedVersionImmutabilityAndHalState()
    {
        await using var factory = await ControlPlaneFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        await PostAsync(admin, "/plans", Draft());
        var draft = await PlanAsync(admin, "native-plan");
        Guid versionId = draft.GetProperty("versions")[0].GetProperty("id").GetGuid();
        await Assert.That(draft.GetProperty("versions")[0].GetProperty("_links").TryGetProperty("publish", out _)).IsTrue();
        var patch = new PatchControlPlaneTenantPlanVersionDraftDto { Pricing = new() { Amount = 12, CurrencyCode = "EUR", BillingPeriod = "monthly" } };
        using (var updated = await admin.PatchAsJsonAsync(Root + $"/plans/versions/{versionId}", patch))
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await PostAsync(admin, $"/plans/versions/{versionId}/publish", new PublishTenantPlanVersionRequest(TenantPlanExistingAssignmentPolicy.LeaveExistingTenantsPinned));
        var published = (await PlanAsync(admin, "native-plan")).GetProperty("versions")[0];
        await Assert.That(published.GetProperty("priceAmount").GetDecimal()).IsEqualTo(12m);
        await Assert.That(published.GetProperty("_links").TryGetProperty("update-version-draft", out _)).IsFalse();
        await Assert.That(published.GetProperty("_links").TryGetProperty("archive", out _)).IsTrue();
        using (var rejected = await admin.PatchAsJsonAsync(Root + $"/plans/versions/{versionId}", patch with { Pricing = new() { Amount = 99, CurrencyCode = "EUR", BillingPeriod = "monthly" } }))
            await ProblemAsync(rejected, HttpStatusCode.BadRequest);
        await PostAsync(admin, $"/plans/versions/{versionId}/clone", new CloneTenantPlanRequest("native-clone", "Cloned plan"));
        await Assert.That((await PlanAsync(admin, "native-clone")).GetProperty("versions")[0].GetProperty("statusId").GetInt32())
            .IsEqualTo((int)TenantPlanStatusEnum.Draft);
        using (var mismatch = await admin.PostAsJsonAsync(Root + "/plans/native-plan/versions", Draft() with { Key = "wrong-key" }))
            await ProblemAsync(mismatch, HttpStatusCode.BadRequest);
        await PostAsync(admin, $"/plans/versions/{versionId}/archive", new { });
        var archived = (await PlanAsync(admin, "native-plan")).GetProperty("versions")[0];
        await Assert.That(archived.GetProperty("statusId").GetInt32()).IsEqualTo((int)TenantPlanStatusEnum.Archived);
        await Assert.That(archived.GetProperty("priceAmount").GetDecimal()).IsEqualTo(12m);
        await Assert.That(archived.GetProperty("isActiveForProvisioning").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task AssignmentAndSettingTransitions_KeepPinnedVersionsAndExplicitApplicationSeparate()
    {
        await using var factory = await ControlPlaneFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        await PostAsync(admin, "/plans", Draft());
        Guid firstVersion = (await PlanAsync(admin, "native-plan")).GetProperty("versions")[0].GetProperty("id").GetGuid();
        await PostAsync(admin, $"/plans/versions/{firstVersion}/publish", new PublishTenantPlanVersionRequest(TenantPlanExistingAssignmentPolicy.LeaveExistingTenantsPinned));
        var originalPlan = await PlanAsync(admin, "native-plan");
        var originalVersion = originalPlan.GetProperty("versions")[0];
        using (var deniedDelete = await admin.DeleteAsync(Root + "/plans/native-plan"))
            await Assert.That(deniedDelete.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
        string tenant = $"/tenants/{seed.TenantId}";
        Guid firstAssignment = await PostAsync(admin, tenant + "/plan-assignment", new SwitchTenantPlanAssignmentRequest(firstVersion));
        await Assert.That(firstAssignment).IsNotEqualTo(Guid.Empty);
        await Assert.That(JsonElement.DeepEquals(originalPlan, await PlanAsync(admin, "native-plan"))).IsTrue();
        Guid repeatedAssignment = await PostAsync(admin, tenant + "/plan-assignment", new SwitchTenantPlanAssignmentRequest(firstVersion));
        await Assert.That(repeatedAssignment).IsEqualTo(firstAssignment);
        await Assert.That(JsonElement.DeepEquals(originalPlan, await PlanAsync(admin, "native-plan"))).IsTrue();
        await PostAsync(admin, tenant + $"/plan-assignments/{firstAssignment}/apply", new { });
        string key = GovernanceSettingKeys.PublicExperience.EventCatalogLabel;
        using (var applied = await admin.GetAsync(Root + tenant + "/effective-configuration"))
            await Assert.That((await JsonAsync(applied)).GetProperty("settings").EnumerateArray().Single(setting => setting.GetProperty("key").GetString() == key).GetProperty("value").GetString())
                .IsEqualTo("Plan events");
        using (var changed = await admin.PutAsJsonAsync(Root + tenant + "/settings/" + key, new SetControlPlaneTenantSettingRequest("Operator events")))
            await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await PostAsync(admin, tenant + "/settings/" + key + "/lock", new { });
        using (var unlocked = await admin.DeleteAsync(Root + tenant + "/settings/" + key + "/lock"))
            await Assert.That(unlocked.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var secondDraft = Draft() with
        {
            Pricing = new(20, "EUR", "monthly"),
            SettingOverrides = [new(key, "\"Second version events\"", false)],
            QuotaLimits = [new(TenantPlanQuotaKeys.AiDailyTenantMessages, 29)]
        };
        Guid secondVersion = await PostAsync(admin, "/plans/native-plan/versions", secondDraft);
        var versions = (await PlanAsync(admin, "native-plan")).GetProperty("versions");
        await Assert.That(versions.GetArrayLength()).IsEqualTo(2);
        await Assert.That(JsonElement.DeepEquals(originalVersion,
            versions.EnumerateArray().Single(version => version.GetProperty("id").GetGuid() == firstVersion))).IsTrue();
        var createdVersion = versions.EnumerateArray().Single(version => version.GetProperty("id").GetGuid() == secondVersion);
        await Assert.That(createdVersion.GetProperty("settings")[0].GetProperty("jsonValue").GetString()).IsEqualTo("\"Second version events\"");
        await Assert.That(createdVersion.GetProperty("quotas")[0].GetProperty("limit").GetInt64()).IsEqualTo(29L);
        await PostAsync(admin, $"/plans/versions/{secondVersion}/publish", new PublishTenantPlanVersionRequest(TenantPlanExistingAssignmentPolicy.LeaveExistingTenantsPinned));
        using (var pinned = await admin.GetAsync(Root + tenant + "/plan-assignment"))
            await Assert.That((await JsonAsync(pinned)).GetProperty("planVersionId").GetGuid()).IsEqualTo(firstVersion);
        Guid secondAssignment = await PostAsync(admin, tenant + "/plan-assignment", new SwitchTenantPlanAssignmentRequest(secondVersion));
        await Assert.That(secondAssignment).IsNotEqualTo(firstAssignment);
        await PostAsync(admin, tenant + $"/plan-assignments/{firstAssignment}/rollback", new { });
        using (var rolledBack = await admin.GetAsync(Root + tenant + "/plan-assignment"))
            await Assert.That((await JsonAsync(rolledBack)).GetProperty("id").GetGuid()).IsEqualTo(firstAssignment);
        using (var unchanged = await admin.GetAsync(Root + tenant + "/effective-configuration"))
            await Assert.That((await JsonAsync(unchanged)).GetProperty("settings").EnumerateArray().Single(setting => setting.GetProperty("key").GetString() == key).GetProperty("value").GetString())
                .IsEqualTo("Operator events");
        using var foreign = await admin.PostAsJsonAsync(Root + $"/tenants/{seed.OtherTenantId}/plan-assignments/{firstAssignment}/apply", new { });
        await ProblemAsync(foreign, HttpStatusCode.BadRequest);
        var finalPlan = await PlanAsync(admin, "native-plan");
        await Assert.That(finalPlan.GetProperty("id").GetGuid()).IsEqualTo(originalPlan.GetProperty("id").GetGuid());
        await Assert.That(JsonElement.DeepEquals(originalVersion,
            finalPlan.GetProperty("versions").EnumerateArray().Single(version => version.GetProperty("id").GetGuid() == firstVersion))).IsTrue();
    }

    [Test]
    public async Task PublishingWithMovePolicy_UpdatesBothTenantAssignmentsWithoutRewritingPublishedContent()
    {
        await using var factory = await ControlPlaneFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        await PostAsync(admin, "/plans", Draft());
        Guid originalVersionId = (await PlanAsync(admin, "native-plan")).GetProperty("versions")[0].GetProperty("id").GetGuid();
        await PostAsync(admin, $"/plans/versions/{originalVersionId}/publish",
            new PublishTenantPlanVersionRequest(TenantPlanExistingAssignmentPolicy.LeaveExistingTenantsPinned));
        var originalVersion = (await PlanAsync(admin, "native-plan")).GetProperty("versions")[0];
        var assignmentIds = new Dictionary<Guid, Guid>();
        foreach (Guid tenantId in new[] { seed.TenantId, seed.OtherTenantId })
            assignmentIds[tenantId] = await PostAsync(admin, $"/tenants/{tenantId}/plan-assignment",
                new SwitchTenantPlanAssignmentRequest(originalVersionId));
        Guid newVersionId = await PostAsync(admin, "/plans/native-plan/versions", Draft() with { Pricing = new(30, "EUR", "monthly") });
        await PostAsync(admin, $"/plans/versions/{newVersionId}/publish",
            new PublishTenantPlanVersionRequest(TenantPlanExistingAssignmentPolicy.MoveExistingTenantsToPublishedVersion));
        foreach (var (tenantId, assignmentId) in assignmentIds)
        {
            using var response = await admin.GetAsync(Root + $"/tenants/{tenantId}/plan-assignment");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var assignment = await JsonAsync(response);
            await Assert.That(assignment.GetProperty("id").GetGuid()).IsEqualTo(assignmentId);
            await Assert.That(assignment.GetProperty("planVersionId").GetGuid()).IsEqualTo(newVersionId);
        }
        var versions = (await PlanAsync(admin, "native-plan")).GetProperty("versions");
        await Assert.That(versions.GetArrayLength()).IsEqualTo(2);
        await Assert.That(JsonElement.DeepEquals(originalVersion,
            versions.EnumerateArray().Single(version => version.GetProperty("id").GetGuid() == originalVersionId))).IsTrue();
    }

    [Test]
    public async Task LifecycleAndDeployment_RequireReasonsConfirmationAndCapacityWithoutCrossTenantEffects()
    {
        await using var factory = await ControlPlaneFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        using (var blocked = await admin.PostAsJsonAsync(Root + "/deployment-mode/transition",
            new { targetMode = "SingleTenant", reason = "Reduce fleet", confirmationText = "SingleTenant" }))
            await ProblemAsync(blocked, HttpStatusCode.BadRequest);
        string tenant = $"/tenants/{seed.OtherTenantId}";
        using (var reasonMissing = await admin.PostAsJsonAsync(Root + tenant + "/suspend", new { }))
            await ProblemAsync(reasonMissing, HttpStatusCode.BadRequest);
        await PostAsync(admin, tenant + "/suspend", new { reason = "Review" });
        await PostAsync(admin, tenant + "/archive", new { reason = "Retire" });
        using (var confirmationMissing = await admin.PostAsJsonAsync(Root + tenant + "/schedule-purge", new { reason = "Purge" }))
            await ProblemAsync(confirmationMissing, HttpStatusCode.BadRequest);
        await PostAsync(admin, tenant + "/schedule-purge", new { reason = "Purge", confirmationText = seed.OtherSlug });
        using (var terminal = await admin.PostAsJsonAsync(Root + tenant + "/reactivate", new { reason = "Invalid revival" }))
            await ProblemAsync(terminal, HttpStatusCode.BadRequest);
        using (var current = await admin.GetAsync(Root + $"/tenants/{seed.TenantId}"))
            await Assert.That((await JsonAsync(current)).GetProperty("statusId").GetInt32()).IsEqualTo((int)TenantStatusEnum.Active);
        await PostAsync(admin, "/deployment-mode/transition", new { targetMode = "SingleTenant", reason = "Reduce fleet", confirmationText = "SingleTenant" });
        using var runbook = await admin.GetAsync(Root + "/deployment-mode");
        await Assert.That((await JsonAsync(runbook)).GetProperty("currentMode").GetString()).IsEqualTo("SingleTenant");
    }

    private static TenantPlanDraft Draft() => new("native-plan", "Native plan", new(5, "EUR", "monthly"), true,
        [new(GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"Plan events\"", false)],
        [new(TenantPlanQuotaKeys.AiDailyTenantMessages, 17)]);

    private static async Task<JsonElement> PlanAsync(HttpClient client, string key)
    {
        using var response = await client.GetAsync(Root + "/plans/" + key);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await JsonAsync(response);
    }

    private static async Task<Guid> PostAsync<T>(HttpClient client, string path, T request)
    {
        using var response = await client.PostAsJsonAsync(Root + path, request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(path + ": " + await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        return body.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var value) ? value : Guid.Empty;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected).Because(await response.Content.ReadAsStringAsync());
        // The existing controller Produces metadata selects application/json for mapped command failures.
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo(expected == HttpStatusCode.BadRequest ? "application/json" : "application/problem+json");
        await Assert.That((await JsonAsync(response)).GetProperty("status").GetInt32()).IsEqualTo((int)expected);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static HttpClient Client(ControlPlaneFactory factory, Guid? userId = null, bool forgedAdmin = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", "default-test");
        if (userId.HasValue)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, forgedAdmin
                ? TestAuthHandler.CreateInstanceAdminHeaderValue(userId.Value)
                : TestAuthHandler.CreateAuthHeaderValue(userId.Value));
        return client;
    }

    private static async Task<SeedData> SeedAsync(ControlPlaneFactory factory)
    {
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var other = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        var role = await db.Roles.SingleAsync(item => item.MasterCode == "platform.admin");
        db.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = admin.UserId, User = null!, RoleId = role.Id, Role = role });
        var now = DateTime.UtcNow;
        var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.MultiTenant, now);
        bootstrap.CompleteInteractive(admin.UserId, now);
        db.Set<InstanceBootstrapState>().Add(bootstrap);
        await db.SaveChangesAsync();
        return new(admin.UserId, member.UserId, admin.TenantId, other.TenantId,
            (await db.Tenants.SingleAsync(item => item.Id == other.TenantId)).Slug);
    }

    private sealed record SeedData(Guid AdminId, Guid MemberId, Guid TenantId, Guid OtherTenantId, string OtherSlug);

    private sealed class ControlPlaneFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"native-control-plane-{Guid.CreateVersion7():N}.db");

        public ControlPlaneFactory() => AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";

        public static async Task<ControlPlaneFactory> CreateAsync()
        {
            var factory = new ControlPlaneFactory();
            try
            {
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                factory.ConfigureDatabase(options);
                await using var db = new ExploreDbContext(options.Options);
                await db.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(db, CancellationToken.None);
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            { Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _path });
            options.UseSnakeCaseNamingConvention();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return db;
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            using var connection = new SqliteConnection($"Data Source={_path}");
            SqliteConnection.ClearPool(connection);
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
        }
    }
}
