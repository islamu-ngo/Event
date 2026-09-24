using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceGovernanceHttpTests
{
    private static CancellationToken Token => TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;

    [Test]
    public async Task InstanceCategoryRequiresAdministratorAndTenantHalTracksCurrentLocks()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var administrator = await factory.SeedLocalUserAsync(emailConfirmed: true);
        var nonAdministrator = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid administratorId;
        await using (var database = factory.CreateDatabase())
        {
            var user = await database.Users.SingleAsync(row => row.Pii!.Email == administrator.Identifier, Token);
            administratorId = user.Id;
            database.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = user.Id, User = user,
                RoleId = (int)RoleEnum.Admin, Role = null!, GrantedAt = DateTime.UtcNow, GrantedBy = user.Id
            });
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = user.Id, User = user,
                ActorId = await database.Actors.Where(actor => actor.UserId == user.Id).Select(actor => actor.Id).SingleAsync(Token),
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
            };
            database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(), TenantId = membership.TenantId, Tenant = null!,
                TenantUserId = membership.Id, TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow, GrantedBy = user.Id
            });
            await database.SaveChangesAsync(Token);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Accept.ParseAdd("application/hal+json");
        const string instancePath = "/api/settings/instance/event-resources";
        const string tenantPath = "/api/settings/tenant/EventResources";
        using (var anonymousGet = await client.GetAsync(instancePath, Token))
            await Assert.That(anonymousGet.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using (var anonymousPut = await client.PutAsJsonAsync(instancePath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { [GovernanceSettingKeys.EventResources.MaxActiveResources] = "80" } }, Token))
            await Assert.That(anonymousPut.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        await AuthenticateAsync(client, nonAdministrator);
        using (var deniedGet = await client.GetAsync(instancePath, Token))
            await Assert.That(deniedGet.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var deniedPut = await client.PutAsJsonAsync(instancePath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { [GovernanceSettingKeys.EventResources.MaxActiveResources] = "80" } }, Token))
            await Assert.That(deniedPut.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        await AuthenticateAsync(client, administrator);
        using (var initial = await client.GetAsync(instancePath, Token))
        {
            await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await initial.Content.ReadAsStringAsync(Token));
            using var document = await JsonDocument.ParseAsync(await initial.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            var root = document.RootElement;
            await Assert.That(root.GetProperty("category").GetString()).IsEqualTo("EventResources");
            await Assert.That(root.TryGetProperty("tenantId", out var tenantId) && tenantId.ValueKind != JsonValueKind.Null).IsFalse();
            var edit = root.GetProperty("_links").GetProperty("edit");
            await Assert.That(edit.GetProperty("href").GetString()).IsEqualTo(instancePath);
            await Assert.That(edit.GetProperty("method").GetString()).IsEqualTo("PUT");
            await Assert.That(root.GetProperty("settings").EnumerateArray().Count()).IsEqualTo(8);
        }
        using (var update = await client.PutAsJsonAsync(instancePath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string>
                   {
                       [GovernanceSettingKeys.EventResources.MaxActiveResources] = "80",
                       [GovernanceSettingKeys.EventResources.AllowUnscannedDocuments] = "true"
                   } }, Token))
            await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await update.Content.ReadAsStringAsync(Token));
        using (var current = await client.GetAsync(instancePath, Token))
        {
            using var document = await JsonDocument.ParseAsync(await current.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            var settings = document.RootElement.GetProperty("settings").EnumerateArray().ToArray();
            await Assert.That(settings.Single(row => row.GetProperty("key").GetString() == GovernanceSettingKeys.EventResources.MaxActiveResources)
                .GetProperty("value").GetString()).IsEqualTo("80");
            await Assert.That(settings.Single(row => row.GetProperty("key").GetString() == GovernanceSettingKeys.EventResources.AllowUnscannedDocuments)
                .GetProperty("value").GetString()).IsEqualTo("true");
        }
        using (var tenant = await client.GetAsync(tenantPath, Token))
        {
            using var document = await JsonDocument.ParseAsync(await tenant.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            var root = document.RootElement;
            await Assert.That(root.GetProperty("_links").GetProperty("edit").GetProperty("href").GetString()).IsEqualTo("/api/settings/tenant/eventresources");
            await Assert.That(root.GetProperty("settings").EnumerateArray().Single(row =>
                row.GetProperty("key").GetString() == GovernanceSettingKeys.EventResources.AllowUnscannedDocuments)
                .GetProperty("canEdit").GetBoolean()).IsFalse();
        }
        using (var tenantOptIn = await client.PutAsJsonAsync(tenantPath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { [GovernanceSettingKeys.EventResources.AllowUnscannedDocuments] = "true" } }, Token))
            await Assert.That(tenantOptIn.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var tenantCap = await client.PutAsJsonAsync(tenantPath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { [GovernanceSettingKeys.EventResources.MaxActiveResources] = "81" } }, Token))
            await Assert.That(tenantCap.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var tenantCap = await client.PutAsJsonAsync(tenantPath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { [GovernanceSettingKeys.EventResources.MaxActiveResources] = "40" } }, Token))
            await Assert.That(tenantCap.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var instanceOptions = await client.PutAsJsonAsync(instancePath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string>
                   {
                       [GovernanceSettingKeys.EventResources.EnabledDeliveryTypes] = "[\"StoredFile\",\"ExternalLink\"]",
                       [GovernanceSettingKeys.EventResources.EnabledAudiences] = "[\"Public\",\"Organizer\"]",
                       [GovernanceSettingKeys.EventResources.PermittedFileTypes] = "[\"application/pdf\",\"application/vnd.openxmlformats-officedocument.wordprocessingml.document\"]",
                       [GovernanceSettingKeys.EventResources.ExternalOrigins] = "[\"https://a.example.org\",\"https://b.example.org\"]",
                       [GovernanceSettingKeys.EventResources.MaxUploadBytes] = "20000000",
                       [GovernanceSettingKeys.EventResources.AuditRetentionDays] = "60"
                   } }, Token))
            await Assert.That(instanceOptions.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await instanceOptions.Content.ReadAsStringAsync(Token));
        using (var tenantOptions = await client.PutAsJsonAsync(tenantPath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string>
                   {
                       [GovernanceSettingKeys.EventResources.EnabledDeliveryTypes] = "[\"StoredFile\",\"ExternalLink\"]",
                       [GovernanceSettingKeys.EventResources.EnabledAudiences] = "[\"Public\",\"Organizer\"]",
                       [GovernanceSettingKeys.EventResources.PermittedFileTypes] = "[\"application/pdf\",\"application/vnd.openxmlformats-officedocument.wordprocessingml.document\"]",
                       [GovernanceSettingKeys.EventResources.ExternalOrigins] = "[\"https://a.example.org\",\"https://b.example.org\"]",
                       [GovernanceSettingKeys.EventResources.MaxUploadBytes] = "15000000",
                       [GovernanceSettingKeys.EventResources.AuditRetentionDays] = "40"
                   } }, Token))
            await Assert.That(tenantOptions.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await tenantOptions.Content.ReadAsStringAsync(Token));
        using (var tighten = await client.PutAsJsonAsync(instancePath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string>
                   {
                       [GovernanceSettingKeys.EventResources.MaxActiveResources] = "20",
                       [GovernanceSettingKeys.EventResources.EnabledDeliveryTypes] = "[\"ExternalLink\"]",
                       [GovernanceSettingKeys.EventResources.EnabledAudiences] = "[\"Organizer\"]",
                       [GovernanceSettingKeys.EventResources.PermittedFileTypes] = "[\"application/pdf\"]",
                       [GovernanceSettingKeys.EventResources.ExternalOrigins] = "[\"https://a.example.org\"]",
                       [GovernanceSettingKeys.EventResources.AuditRetentionDays] = "20"
                   } }, Token))
            await Assert.That(tighten.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await tighten.Content.ReadAsStringAsync(Token));
        using (var effective = await client.GetAsync(tenantPath, Token))
        {
            await Assert.That(effective.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var document = await JsonDocument.ParseAsync(await effective.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            var rows = document.RootElement.GetProperty("settings").EnumerateArray()
                .ToDictionary(row => row.GetProperty("key").GetString()!, row => row);
            await Assert.That(rows[GovernanceSettingKeys.EventResources.MaxActiveResources].GetProperty("value").GetString()).IsEqualTo("20");
            await Assert.That(rows[GovernanceSettingKeys.EventResources.AuditRetentionDays].GetProperty("value").GetString()).IsEqualTo("20");
            await Assert.That(rows[GovernanceSettingKeys.EventResources.MaxUploadBytes].GetProperty("value").GetString()).IsEqualTo("10485760");
            await Assert.That(rows[GovernanceSettingKeys.EventResources.EnabledDeliveryTypes].GetProperty("value").GetString()).IsEqualTo("[\"ExternalLink\"]");
            await Assert.That(rows[GovernanceSettingKeys.EventResources.EnabledAudiences].GetProperty("value").GetString()).IsEqualTo("[\"Organizer\"]");
            await Assert.That(rows[GovernanceSettingKeys.EventResources.PermittedFileTypes].GetProperty("value").GetString()).IsEqualTo("[\"application/pdf\"]");
            await Assert.That(rows[GovernanceSettingKeys.EventResources.ExternalOrigins].GetProperty("value").GetString()).IsEqualTo("[\"https://a.example.org\"]");
            await Assert.That(rows[GovernanceSettingKeys.EventResources.MaxActiveResources].GetProperty("source").GetString()).IsEqualTo("TenantOverride");
            await Assert.That(rows[GovernanceSettingKeys.EventResources.MaxActiveResources].GetProperty("isLocked").GetBoolean()).IsFalse();
            await Assert.That(rows[GovernanceSettingKeys.EventResources.MaxActiveResources].GetProperty("canEdit").GetBoolean()).IsTrue();
        }
        await using (var policyScope = factory.Services.CreateAsyncScope())
        {
            var policy = await policyScope.ServiceProvider.GetRequiredService<IEventResourceGovernancePolicyReader>()
                .ReadAsync(PlatformDefaults.DefaultTenantId, Token);
            await Assert.That(policy!.MaxActiveResources).IsEqualTo(20);
        }
        using (var widenedAfterTightening = await client.PutAsJsonAsync(tenantPath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { [GovernanceSettingKeys.EventResources.MaxActiveResources] = "30" } }, Token))
            await Assert.That(widenedAfterTightening.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        await using (var database = factory.CreateDatabase())
        {
            var unit = new EfCoreUnitOfWork(database);
            (await new EventResourceSettingsWriter(database, new RelationalSettingMutationLock(database, unit), unit)
                .ApplyAsync([new(null, GovernanceSettingKeys.EventResources.MaxActiveResources,
                    EventResourceSettingMutationKind.SetLock, IsLocked: true)], administratorId, Token)).EnsureAccepted();
        }
        await using (var cacheScope = factory.Services.CreateAsyncScope())
            cacheScope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>()
                .InvalidateCache(Explore.Domain.Settings.SettingScope.Instance, Guid.Empty);
        using (var locked = await client.GetAsync(tenantPath, Token))
        {
            using var document = await JsonDocument.ParseAsync(await locked.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            var root = document.RootElement;
            await Assert.That(root.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
            var lockedCapacity = root.GetProperty("settings").EnumerateArray().Single(row =>
                row.GetProperty("key").GetString() == GovernanceSettingKeys.EventResources.MaxActiveResources);
            await Assert.That(lockedCapacity.GetProperty("value").GetString()).IsEqualTo("20");
            await Assert.That(lockedCapacity.GetProperty("source").GetString()).IsEqualTo("SystemLocked");
            await Assert.That(lockedCapacity.GetProperty("isLocked").GetBoolean()).IsTrue();
            await Assert.That(lockedCapacity.GetProperty("canEdit").GetBoolean()).IsFalse();
        }
        using (var stale = await client.PutAsJsonAsync(tenantPath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { [GovernanceSettingKeys.EventResources.MaxActiveResources] = "20" } }, Token))
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var locked = await client.GetAsync(instancePath, Token))
        {
            using var document = await JsonDocument.ParseAsync(await locked.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(document.RootElement.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
        }
        await using (var database = factory.CreateDatabase())
        {
            var unit = new EfCoreUnitOfWork(database);
            var writer = new EventResourceSettingsWriter(database, new RelationalSettingMutationLock(database, unit), unit);
            var otherSettings = EventResourceSettingDefinitions.All.Where(definition =>
                definition.Key != GovernanceSettingKeys.EventResources.MaxActiveResources).ToArray();
            (await writer.ApplyAsync([.. otherSettings.Select(definition => new EventResourceSettingMutation(
                null, definition.Key, EventResourceSettingMutationKind.SetValue, definition.DefaultValue))],
                administratorId, Token)).EnsureAccepted();
            (await writer.ApplyAsync([.. otherSettings.Select(definition => new EventResourceSettingMutation(
                null, definition.Key, EventResourceSettingMutationKind.SetLock, IsLocked: true))],
                administratorId, Token)).EnsureAccepted();
        }
        await using (var cacheScope = factory.Services.CreateAsyncScope())
            cacheScope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>()
                .InvalidateCache(Explore.Domain.Settings.SettingScope.Instance, Guid.Empty);
        using (var allLocked = await client.GetAsync(tenantPath, Token))
        {
            using var document = await JsonDocument.ParseAsync(await allLocked.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            await Assert.That(document.RootElement.GetProperty("_links").TryGetProperty("edit", out _)).IsFalse();
        }
        using (var foreignKey = await client.PutAsJsonAsync(instancePath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { ["private.invalid.setting"] = "private-invalid-value" } }, Token))
        {
            await Assert.That(foreignKey.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That((await foreignKey.Content.ReadAsStringAsync(Token)).Contains("private-invalid-value", StringComparison.Ordinal)).IsFalse();
        }
        using (var invalid = await client.PutAsJsonAsync(instancePath, new UpdateSettingBatchDto
               { Values = new Dictionary<string, string> { [GovernanceSettingKeys.EventResources.ExternalOrigins] = "private-invalid-value" } }, Token))
        {
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That((await invalid.Content.ReadAsStringAsync(Token)).Contains("private-invalid-value", StringComparison.Ordinal)).IsFalse();
        }
    }

    private static async Task AuthenticateAsync(HttpClient client, Explore.Application.Features.Authentication.Local.Models.LocalAuthRequestDto credentials)
    {
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        login.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
    }

    [Test]
    public async Task NativeTenantSettingsRejectWideningBatchesInstanceOnlyOptInAndFreshInstanceLocks()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid userId;
        await using (var database = factory.CreateDatabase())
        {
            var user = await database.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier, Token);
            userId = user.Id;
            database.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = userId, User = user,
                RoleId = (int)RoleEnum.Admin, Role = null!, GrantedAt = DateTime.UtcNow, GrantedBy = userId
            });
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = userId, User = user,
                ActorId = await database.Actors.Where(actor => actor.UserId == userId).Select(actor => actor.Id).SingleAsync(Token),
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
            };
            database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(), TenantId = membership.TenantId, Tenant = null!,
                TenantUserId = membership.Id, TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow, GrantedBy = userId
            });
            await database.SaveChangesAsync(Token);
            var unit = new EfCoreUnitOfWork(database);
            (await new EventResourceSettingsWriter(database, new RelationalSettingMutationLock(database, unit), unit)
                .ApplyAsync([
                    new(null, GovernanceSettingKeys.EventResources.MaxActiveResources, EventResourceSettingMutationKind.SetValue, "100"),
                    new(null, GovernanceSettingKeys.EventResources.AllowUnscannedDocuments, EventResourceSettingMutationKind.SetValue, "true")
                ], userId, Token)).EnsureAccepted();
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token))
        {
            login.EnsureSuccessStatusCode();
            using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
        string capacityPath = "/api/settings/tenant/keys/" + GovernanceSettingKeys.EventResources.MaxActiveResources;
        using (var accepted = await client.PutAsJsonAsync(capacityPath, new UpdateSettingValueDto { Value = "50" }, Token))
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await accepted.Content.ReadAsStringAsync(Token));
        using (var widening = await client.PutAsJsonAsync(capacityPath, new UpdateSettingValueDto { Value = "101" }, Token))
            await Assert.That(widening.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(await widening.Content.ReadAsStringAsync(Token));
        using (var batch = await client.PutAsJsonAsync("/api/settings/tenant/EventResources", new UpdateSettingBatchDto
               {
                   Values = new Dictionary<string, string>
                   {
                       [GovernanceSettingKeys.EventResources.AuditRetentionDays] = "5",
                       [GovernanceSettingKeys.EventResources.MaxActiveResources] = "150"
                   }
               }, Token))
            await Assert.That(batch.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(await batch.Content.ReadAsStringAsync(Token));
        using (var optIn = await client.PutAsJsonAsync(
                   "/api/settings/tenant/keys/" + GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                   new UpdateSettingValueDto { Value = "true" }, Token))
            await Assert.That(optIn.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(await optIn.Content.ReadAsStringAsync(Token));
        await using (var database = factory.CreateDatabase())
        {
            var unit = new EfCoreUnitOfWork(database);
            (await new EventResourceSettingsWriter(database, new RelationalSettingMutationLock(database, unit), unit)
                .ApplyAsync([new(null, GovernanceSettingKeys.EventResources.MaxActiveResources,
                    EventResourceSettingMutationKind.SetLock, IsLocked: true)], userId, Token)).EnsureAccepted();
        }
        using (var locked = await client.PutAsJsonAsync(capacityPath, new UpdateSettingValueDto { Value = "25" }, Token))
            await Assert.That(locked.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(await locked.Content.ReadAsStringAsync(Token));
        await using (var verification = factory.CreateDatabase())
        {
            verification.EnableTenantFilterBypass("Governance HTTP test verifies exact scoped setting outcomes.");
            var saved = await verification.TenantSettingOverrides.AsNoTracking()
                .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId).ToArrayAsync(Token);
            await Assert.That(saved.Single(row => row.SettingKey == GovernanceSettingKeys.EventResources.MaxActiveResources).Value).IsEqualTo("50");
            await Assert.That(saved.Any(row => row.SettingKey == GovernanceSettingKeys.EventResources.AuditRetentionDays
                || row.SettingKey == GovernanceSettingKeys.EventResources.AllowUnscannedDocuments)).IsFalse();
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var current = await scope.ServiceProvider.GetRequiredService<IEventResourceGovernancePolicyReader>()
            .ReadAsync(PlatformDefaults.DefaultTenantId, Token);
        await Assert.That(current!.MaxActiveResources).IsEqualTo(100);
    }
}
