using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Footer;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeFooterHttpTests
{
    [Test]
    public async Task LinkLifecycle_PreservesPartialUpdatesOrderingPublicFallbackAndTransactionalGroupDeletion()
    {
        await using var factory = new FooterFactory();
        var seed = await SeedAsync(factory);
        using var client = Client(factory, seed.Admin.Id);
        using var anonymous = Client(factory);
        var initial = await ReadAsync<FooterConfigDto>(anonymous, "/api/footer/config");
        await Assert.That(initial.LinkGroups.Select(group => group.Id)).IsEquivalentTo(seed.InstanceGroupIds);
        await Assert.That(initial.LinkGroups.Select(group => group.Id)).Contains(seed.Instance.Id);

        var first = await CreateGroupAsync(client, "First");
        var second = await CreateGroupAsync(client, "Second");
        var link = await CreateLinkAsync(client, first, "  Events  ", "/events");
        var detail = await ReadAsync<FooterLinkGroupDetailsDto>(client, $"/api/footer/link-groups/{first}");
        await Assert.That(detail.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(detail.Links.Single().Label).IsEqualTo("Events");
        await PatchAsync(client, $"/api/footer/link-groups/{first}", new PatchFooterLinkGroupDto { Title = new() { Value = "  Renamed  " } });
        await PatchAsync(client, $"/api/footer/links/{link}", new PatchFooterLinkDto { OpenInNewTab = new() { Value = true } });
        var changed = await ReadAsync<FooterLinkGroupDetailsDto>(client, $"/api/footer/link-groups/{first}");
        await Assert.That(changed.Title).IsEqualTo("Renamed");
        await Assert.That(changed.IsActive).IsTrue();
        await Assert.That(changed.Links.Single().OpenInNewTab).IsTrue();
        await Assert.That(changed.Links.Single().Url).IsEqualTo("/events");
        await Assert.That(detail.Title).IsEqualTo("First");
        await Assert.That(detail.Links.Single().OpenInNewTab).IsFalse();
        using (var invalid = await client.PatchAsJsonAsync($"/api/footer/links/{link}", new PatchFooterLinkDto { Url = new() { Value = "javascript:alert(1)" } }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        using (var reorder = await client.PostAsJsonAsync("/api/footer/link-groups/reorder", new[] { second, seed.Foreign.Id, first }))
            await Assert.That(reorder.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var groups = await ReadAsync<List<FooterLinkGroupListDto>>(client, "/api/footer/link-groups");
        await Assert.That(groups.Select(group => group.Id).SequenceEqual(new[] { second, first })).IsTrue();
        await Assert.That(groups.Select(group => group.Order).SequenceEqual(new[] { 0, 2 })).IsTrue();
        var publicConfig = await ReadAsync<FooterConfigDto>(anonymous, "/api/footer/config");
        await Assert.That(publicConfig.LinkGroups.Select(group => group.Id).SequenceEqual(new[] { second, first })).IsTrue();
        await DeleteAsync(client, $"/api/footer/links/{link}");
        await Assert.That((await ReadAsync<FooterLinkGroupDetailsDto>(client, $"/api/footer/link-groups/{first}")).Links).IsEmpty();
        var cascadingLink = await CreateLinkAsync(client, first, "Nested", "/nested");
        await DeleteAsync(client, $"/api/footer/link-groups/{first}");
        using (var missingLink = await client.DeleteAsync($"/api/footer/links/{cascadingLink}"))
            await ProblemAsync(missingLink, HttpStatusCode.NotFound);
        using (var missingGroup = await client.GetAsync($"/api/footer/link-groups/{first}"))
            await ProblemAsync(missingGroup, HttpStatusCode.NotFound);
        await DeleteAsync(client, $"/api/footer/link-groups/{second}");
        await Assert.That((await ReadAsync<FooterConfigDto>(anonymous, "/api/footer/config")).LinkGroups.Select(group => group.Id))
            .IsEquivalentTo(seed.InstanceGroupIds);
    }

    [Test]
    public async Task ForeignAndInstanceRows_AreHiddenFromEveryTenantMutationAndScopedDetailQuery()
    {
        await using var factory = new FooterFactory();
        var seed = await SeedAsync(factory);
        using var client = Client(factory, seed.Admin.Id);
        foreach (var target in new[] { (GroupId: seed.Foreign.Id, LinkId: seed.ForeignLink.Id), (GroupId: seed.Instance.Id, LinkId: seed.InstanceLink.Id) })
        {
            using (var detail = await client.GetAsync($"/api/footer/link-groups/{target.GroupId}"))
                await ProblemAsync(detail, HttpStatusCode.NotFound);
            using (var update = await client.PatchAsJsonAsync($"/api/footer/link-groups/{target.GroupId}", new PatchFooterLinkGroupDto { Title = new() { Value = "Forbidden" } }))
                await ProblemAsync(update, HttpStatusCode.NotFound);
            using (var delete = await client.DeleteAsync($"/api/footer/link-groups/{target.GroupId}"))
                await ProblemAsync(delete, HttpStatusCode.NotFound);
            using (var createLink = await client.PostAsJsonAsync($"/api/footer/link-groups/{target.GroupId}/links", new { label = "Forbidden", url = "/bad", openInNewTab = false }))
                await ProblemAsync(createLink, HttpStatusCode.NotFound);
            using (var updateLink = await client.PatchAsJsonAsync($"/api/footer/links/{target.LinkId}", new PatchFooterLinkDto { Label = new() { Value = "Forbidden" } }))
                await ProblemAsync(updateLink, HttpStatusCode.NotFound);
            using (var deleteLink = await client.DeleteAsync($"/api/footer/links/{target.LinkId}"))
                await ProblemAsync(deleteLink, HttpStatusCode.NotFound);
        }
        await Assert.That(await ReadAsync<List<FooterLinkGroupListDto>>(client, "/api/footer/link-groups")).IsEmpty();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ITenantContextAccessor>();
        tenant.SetTenant(PlatformDefaults.DefaultTenantId);
        var query = services.GetRequiredService<IQueryHandler<GetFooterLinkGroupDetailsQuery, FooterLinkGroupDetailsDto>>();
        await Assert.That(async () => await query.QueryAsync(new(seed.Foreign.Id), default)).Throws<NotFoundException>();
        tenant.SetTenant(seed.Foreign.TenantId!.Value);
        var foreign = await query.QueryAsync(new(seed.Foreign.Id), default);
        await Assert.That(foreign.Title).IsEqualTo("Foreign");
        await Assert.That(foreign.Order).IsEqualTo(7);
        await Assert.That(foreign.Links.Single().Label).IsEqualTo("Foreign link");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GovernanceLocks_PreserveHalAndAllLinkMutationGuardsAcrossDeploymentModes(bool singleTenant)
    {
        await using var factory = new FooterFactory();
        var seed = await SeedAsync(factory, singleTenant);
        using var client = Client(factory, seed.Admin.Id);
        var id = await CreateGroupAsync(client, "Local");
        var linkId = await CreateLinkAsync(client, id, "Local link", "/local");
        await ReadAsync<FooterGovernanceSettingsDto>(client, "/api/instance/settings/footer-governance");
        await PatchAsync(client, "/api/instance/settings/footer-governance", new PatchFooterGovernanceSettingsDto
        {
            LockTenantLinkGroups = OptionalUpdate<bool>.Set(true),
            LockTenantDescription = OptionalUpdate<bool>.Set(true)
        });
        var governance = await ReadAsync<FooterGovernanceSettingsDto>(client, "/api/instance/settings/footer-governance");
        await Assert.That(governance.LockTenantLinkGroups).IsTrue();
        await Assert.That(governance.LockTenantDescription).IsTrue();
        using (var response = await client.GetAsync("/api/footer/settings"))
        {
            var settings = await JsonAsync(response);
            await Assert.That(settings.GetProperty("lockTenantLinkGroups").GetBoolean()).IsEqualTo(!singleTenant);
            await Assert.That(settings.GetProperty("_links").TryGetProperty("manage-link-groups", out _)).IsEqualTo(singleTenant);
        }
        if (singleTenant)
        {
            await CreateGroupAsync(client, "Allowed despite raw lock");
            await PatchAsync(client, $"/api/footer/link-groups/{id}", new PatchFooterLinkGroupDto { Title = new() { Value = "Allowed" } });
            await DeleteAsync(client, $"/api/footer/link-groups/{id}");
        }
        else
        {
            using (var create = await client.PostAsJsonAsync("/api/footer/link-groups", new { title = "Denied" }))
                await ProblemAsync(create, HttpStatusCode.Forbidden);
            using (var update = await client.PatchAsJsonAsync($"/api/footer/link-groups/{id}", new PatchFooterLinkGroupDto { Title = new() { Value = "Denied" } }))
                await ProblemAsync(update, HttpStatusCode.Forbidden);
            using (var delete = await client.DeleteAsync($"/api/footer/link-groups/{id}"))
                await ProblemAsync(delete, HttpStatusCode.Forbidden);
            using (var reorder = await client.PostAsJsonAsync("/api/footer/link-groups/reorder", new[] { id }))
                await ProblemAsync(reorder, HttpStatusCode.Forbidden);
            using (var createLink = await client.PostAsJsonAsync($"/api/footer/link-groups/{id}/links", new { label = "Denied", url = "/denied", openInNewTab = false }))
                await ProblemAsync(createLink, HttpStatusCode.Forbidden);
            using (var updateLink = await client.PatchAsJsonAsync($"/api/footer/links/{linkId}", new PatchFooterLinkDto { Label = new() { Value = "Denied" } }))
                await ProblemAsync(updateLink, HttpStatusCode.Forbidden);
            using (var deleteLink = await client.DeleteAsync($"/api/footer/links/{linkId}"))
                await ProblemAsync(deleteLink, HttpStatusCode.Forbidden);
            var unchanged = await ReadAsync<FooterLinkGroupDetailsDto>(client, $"/api/footer/link-groups/{id}");
            await Assert.That(unchanged.Title).IsEqualTo("Local");
            await Assert.That(unchanged.Order).IsEqualTo(1);
            await Assert.That(unchanged.Links.Single().Label).IsEqualTo("Local link");
            await Assert.That((await ReadAsync<List<FooterLinkGroupListDto>>(client, "/api/footer/link-groups")).Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ScalarPatch_PreservesOmittedAndLockedLeavesInvalidatesWarmCacheAndKeepsForeignTenantUnchanged()
    {
        await using var factory = new FooterFactory();
        var seed = await SeedAsync(factory, singleTenant: false);
        using var client = Client(factory, seed.Admin.Id);
        var before = await ReadAsync<TenantFooterSettingsDto>(client, "/api/footer/settings");
        await PatchAsync(client, "/api/footer/settings", new PatchTenantFooterSettingsDto
        {
            Description = new() { Text = OptionalUpdate<string>.Set("Tenant description") },
            SocialLinks = new() { Items = OptionalUpdate<IReadOnlyList<FooterSocialLinkDto>>.Set([new() { Platform = "github", Url = "https://example.test/community", Label = "Community" }]) }
        });
        var changed = await ReadAsync<TenantFooterSettingsDto>(client, "/api/footer/settings");
        await Assert.That(changed.DescriptionText).IsEqualTo("Tenant description");
        await Assert.That(changed.SocialLinks.Single().Label).IsEqualTo("Community");
        await Assert.That(changed.Template).IsEqualTo(before.Template);
        await PatchAsync(client, "/api/instance/settings/footer-governance", new PatchFooterGovernanceSettingsDto { LockTenantDescription = OptionalUpdate<bool>.Set(true) });
        await PatchAsync(client, "/api/footer/settings", new PatchTenantFooterSettingsDto
        {
            General = new() { Enabled = OptionalUpdate<bool>.Set(!before.Enabled) },
            Description = new() { Text = OptionalUpdate<string>.Set("Must not overwrite") }
        });
        var locked = await ReadAsync<TenantFooterSettingsDto>(client, "/api/footer/settings");
        await Assert.That(locked.DescriptionText).IsEqualTo(changed.DescriptionText);
        await Assert.That(locked.Enabled).IsEqualTo(!before.Enabled);
        await Assert.That(locked.SocialLinks).IsEquivalentTo(changed.SocialLinks);
        using (var invalid = await client.PatchAsJsonAsync("/api/footer/settings", new PatchTenantFooterSettingsDto
        {
            General = new() { Enabled = OptionalUpdate<bool>.Set(before.Enabled) },
            Template = new() { Value = OptionalUpdate<string>.Set(null) }
        }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, "application/json");
        var afterInvalid = await ReadAsync<TenantFooterSettingsDto>(client, "/api/footer/settings");
        await Assert.That(JsonSerializer.Serialize(afterInvalid)).IsEqualTo(JsonSerializer.Serialize(locked));
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(seed.Foreign.TenantId!.Value);
        var foreign = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTenantFooterSettingsQuery, TenantFooterSettingsDto>>()
            .QueryAsync(new(), default);
        await Assert.That(foreign.DescriptionText).IsEqualTo(before.DescriptionText);
        await Assert.That(foreign.Enabled).IsEqualTo(before.Enabled);
        await Assert.That(foreign.SocialLinks).IsEmpty();
    }

    [Test]
    public async Task EmptyPatchesAreRejectedAndEmptyReorderIsANoOpWithoutMutation()
    {
        await using var factory = new FooterFactory();
        var seed = await SeedAsync(factory);
        using var client = Client(factory, seed.Admin.Id);
        var groupId = await CreateGroupAsync(client, "Preserved");
        var linkId = await CreateLinkAsync(client, groupId, "Preserved link", "/preserved");
        var beforeGroup = await ReadAsync<FooterLinkGroupDetailsDto>(client, $"/api/footer/link-groups/{groupId}");
        var beforeSettings = await ReadAsync<TenantFooterSettingsDto>(client, "/api/footer/settings");
        var beforeGovernance = await ReadAsync<FooterGovernanceSettingsDto>(client, "/api/instance/settings/footer-governance");

        foreach (var path in new[] { $"/api/footer/link-groups/{groupId}", $"/api/footer/links/{linkId}", "/api/footer/settings", "/api/instance/settings/footer-governance" })
        {
            using var response = await client.PatchAsJsonAsync(path, new { });
            await ProblemAsync(response, HttpStatusCode.BadRequest,
                path == "/api/footer/settings" ? "application/json" : "application/problem+json");
        }
        using (var reorder = await client.PostAsJsonAsync("/api/footer/link-groups/reorder", Array.Empty<Guid>()))
            await Assert.That(reorder.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var afterGroup = await ReadAsync<FooterLinkGroupDetailsDto>(client, $"/api/footer/link-groups/{groupId}");
        var afterSettings = await ReadAsync<TenantFooterSettingsDto>(client, "/api/footer/settings");
        var afterGovernance = await ReadAsync<FooterGovernanceSettingsDto>(client, "/api/instance/settings/footer-governance");
        await Assert.That(JsonSerializer.Serialize(afterGroup)).IsEqualTo(JsonSerializer.Serialize(beforeGroup));
        await Assert.That(JsonSerializer.Serialize(afterSettings)).IsEqualTo(JsonSerializer.Serialize(beforeSettings));
        await Assert.That(afterGovernance).IsEqualTo(beforeGovernance);
    }

    [Test]
    public async Task DeniedProviderAndUnprivilegedGovernance_CannotMutateThroughHttpOrScopedPorts()
    {
        await using var factory = new FooterFactory { AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false } };
        var seed = await SeedAsync(factory);
        using var client = Client(factory, seed.User.Id);
        using var anonymous = Client(factory);
        using (var unauthenticated = await anonymous.GetAsync("/api/footer/settings"))
            await Assert.That(unauthenticated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using (var denied = await client.PostAsJsonAsync("/api/footer/link-groups", new { title = "Denied" }))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var denied = await client.PatchAsJsonAsync("/api/footer/settings", new PatchTenantFooterSettingsDto { General = new() { Enabled = OptionalUpdate<bool>.Set(false) } }))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var denied = await client.GetAsync("/api/instance/settings/footer-governance"))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var denied = await client.PatchAsJsonAsync("/api/instance/settings/footer-governance", new PatchFooterGovernanceSettingsDto { LockTenantLinkGroups = OptionalUpdate<bool>.Set(true) }))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var response = await client.GetAsync("/api/footer/settings"))
        {
            var body = await JsonAsync(response);
            await Assert.That(body.GetProperty("_links").TryGetProperty("edit", out _)).IsFalse();
            await Assert.That(body.GetProperty("_links").TryGetProperty("manage-link-groups", out _)).IsFalse();
        }
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        await Assert.That(async () => await services.GetRequiredService<ICommandHandler<CreateFooterLinkGroupCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, UserId = seed.User.Id, Title = "Denied" }, default)).Throws<AuthorizationException>();
        var governance = await services.GetRequiredService<ICommandHandler<UpdateFooterGovernanceSettingsCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { UserId = seed.User.Id, Patch = new() { LockTenantLinkGroups = OptionalUpdate<bool>.Set(true) } }, default);
        await Assert.That(governance.IsSuccess).IsFalse();
        await Assert.That(await services.GetRequiredService<IQueryHandler<GetFooterLinkGroupListQuery, List<FooterLinkGroupListDto>>>().QueryAsync(new(), default)).IsEmpty();
        await Assert.That((await services.GetRequiredService<IQueryHandler<GetFooterGovernanceSettingsQuery, FooterGovernanceSettingsDto>>().QueryAsync(new(), default)).LockTenantLinkGroups).IsFalse();
    }

    private static async Task<SeedData> SeedAsync(FooterFactory factory, bool singleTenant = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant { Id = PlatformDefaults.DefaultTenantId, Slug = PlatformDefaults.DefaultTenantSlug, FullName = "Footer", TenantStatusId = status.Id, TenantStatus = status };
        var foreignTenant = new Tenant { Id = Guid.CreateVersion7(), Slug = "foreign-footer", FullName = "Foreign", TenantStatusId = status.Id, TenantStatus = status };
        db.Tenants.AddRange(tenant, foreignTenant);
        var admin = new User { Id = Guid.CreateVersion7(), Pii = new() { Email = "admin@example.test", FirstName = "Footer", LastName = "Admin" } };
        var user = new User { Id = Guid.CreateVersion7(), Pii = new() { Email = "user@example.test", FirstName = "Footer", LastName = "User" } };
        db.Users.AddRange(admin, user);
        var role = await db.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
        db.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = admin.Id, User = admin, RoleId = role.Id, Role = role });
        var foreign = new TenantFooterLinkGroup { Id = Guid.CreateVersion7(), TenantId = foreignTenant.Id, Title = "Foreign", Order = 7, IsActive = true };
        var instance = new TenantFooterLinkGroup { Id = Guid.CreateVersion7(), Title = "Instance", Order = 1, IsActive = true };
        db.TenantFooterLinkGroups.AddRange(foreign, instance);
        var foreignLink = new TenantFooterLink { Id = Guid.CreateVersion7(), FooterLinkGroupId = foreign.Id, Label = "Foreign link", Url = "/foreign", Order = 1, IsActive = true };
        var instanceLink = new TenantFooterLink { Id = Guid.CreateVersion7(), FooterLinkGroupId = instance.Id, Label = "Instance link", Url = "/instance", Order = 1, IsActive = true };
        db.TenantFooterLinks.AddRange(foreignLink, instanceLink);
        if (!singleTenant)
        {
            var now = DateTime.UtcNow;
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.MultiTenant, now);
            bootstrap.CompleteInteractive(admin.Id, now);
            db.Set<InstanceBootstrapState>().Add(bootstrap);
        }
        await db.SaveChangesAsync();
        var instanceGroupIds = await db.TenantFooterLinkGroups
            .Where(group => group.TenantId == null && group.IsActive)
            .Select(group => group.Id).ToArrayAsync();
        return new(admin, user, foreign, instance, foreignLink, instanceLink, instanceGroupIds);
    }

    private static HttpClient Client(FooterFactory factory, Guid? userId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Slug", PlatformDefaults.DefaultTenantSlug);
        if (userId.HasValue)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId.Value));
        return client;
    }

    private static async Task<Guid> CreateGroupAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/footer/link-groups", new { title });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();
        await Assert.That(response.Headers.Location?.AbsolutePath).IsEqualTo($"/api/footer/link-groups/{id}");
        return id;
    }

    private static async Task<Guid> CreateLinkAsync(HttpClient client, Guid groupId, string label, string url)
    {
        using var response = await client.PostAsJsonAsync($"/api/footer/link-groups/{groupId}/links", new { label, url, openInNewTab = false });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task PatchAsync<T>(HttpClient client, string path, T patch)
    {
        using var response = await client.PatchAsJsonAsync(path, patch);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await JsonAsync(response)).GetProperty("success").GetBoolean()).IsTrue();
    }

    private static async Task DeleteAsync(HttpClient client, string path)
    {
        using var response = await client.DeleteAsync(path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadFromJsonAsync<bool>()).IsTrue();
    }

    private static async Task<T> ReadAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task ProblemAsync(
        HttpResponseMessage response, HttpStatusCode expected, string mediaType = "application/problem+json")
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(mediaType);
        var problem = await JsonAsync(response);
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo((int)expected);
        if (expected == HttpStatusCode.BadRequest)
            await Assert.That(problem.GetProperty("errors").EnumerateObject().Any()).IsTrue();
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private sealed record SeedData(User Admin, User User, TenantFooterLinkGroup Foreign, TenantFooterLinkGroup Instance, TenantFooterLink ForeignLink, TenantFooterLink InstanceLink, IReadOnlyList<Guid> InstanceGroupIds);

    private sealed class FooterFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"native-footer-{Guid.CreateVersion7():N}.db");

        public FooterFactory()
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true };
            AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                ConfigureDatabase(options);
                using (var db = new ExploreDbContext(options.Options))
                    db.Database.EnsureCreated();
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

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = _databasePath
            });
            options.UseSnakeCaseNamingConvention();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }
}
