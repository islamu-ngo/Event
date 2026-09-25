using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
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
