
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Persistence.QueryFilters;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EmailDeliveryDisableHttpTests
{
    private const string InstancePath = "/api/instance/settings/smtp";
    private const string TenantPath = "/api/settings/email-delivery";
    private const string TenantSettingsPath = "/api/settings/tenant/Email";
    private const string Acknowledgement = "DISABLE EMAIL DELIVERY";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisableRequiresFreshConfirmationAndIsDiscoverableAndReversible(bool tenant)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = factory.CreateClient();
        Guid actor = await AuthenticateAsync(factory, client, tenant ? "tenant" : "instance");
        string path = tenant ? TenantPath : InstancePath;
        string settingsPath = tenant ? TenantSettingsPath : InstancePath;

        using var previewResponse = await client.PostAsync(path + "/disable-preview", null);
        await AssertStatusAsync(previewResponse, HttpStatusCode.OK);
        await AssertPrivateAsync(previewResponse);
        using var preview = await ReadAsync(previewResponse);
        await AssertPreviewAsync(preview.RootElement, tenant);

        using (var settingsResponse = await client.GetAsync(settingsPath))
        {
            await AssertStatusAsync(settingsResponse, HttpStatusCode.OK);
            using var settings = await ReadAsync(settingsResponse);
            await Assert.That(Link(settings.RootElement, "disable-preview")).EndsWith(path + "/disable-preview");
            await AssertEnabledAsync(settings.RootElement, tenant, true);
        }
        await Assert.That(Link(preview.RootElement, "disable")).EndsWith(path + "/disable");

        using (var missingAcknowledgement = await client.PostAsJsonAsync(path + "/disable", new
        {
            expectedRevision = preview.RootElement.GetProperty("expectedRevision").GetInt64(),
            confirmationToken = preview.RootElement.GetProperty("confirmationToken").GetString()
        }))
            await AssertStatusAsync(missingAcknowledgement, HttpStatusCode.BadRequest);

        foreach (string? acknowledgement in new string?[] { null, "disable email delivery", "DISABLE EMAIL DELIVERY " })
        {
            using var rejected = await client.PostAsJsonAsync(path + "/disable", Confirmation(preview.RootElement, acknowledgement));
            await AssertStatusAsync(rejected, HttpStatusCode.BadRequest);
            await AssertPrivateAsync(rejected);
        }
        using (var rejected = await client.PostAsJsonAsync(path + "/disable", new
        {
            expectedRevision = preview.RootElement.GetProperty("expectedRevision").GetInt64(),
            confirmationToken = "invalid-token",
            acknowledgement = Acknowledgement
        }))
        {
            await AssertStatusAsync(rejected, HttpStatusCode.Conflict);
            await AssertProblemAsync(rejected, "https://tools.ietf.org/html/rfc9110#section-15.5.10");
        }
        using (var rejected = await client.PostAsJsonAsync(path + "/disable", new
        {
            expectedRevision = preview.RootElement.GetProperty("expectedRevision").GetInt64() + 1,
            confirmationToken = preview.RootElement.GetProperty("confirmationToken").GetString(),
            acknowledgement = Acknowledgement
        }))
            await AssertStatusAsync(rejected, HttpStatusCode.Conflict);

        // A real owning settings write invalidates the previously issued token.
        if (tenant)
        {
            using var updated = await client.PutAsJsonAsync("/api/settings/tenant/keys/email.from_name", new { value = "\"Changed sender\"" });
            await AssertStatusAsync(updated, HttpStatusCode.OK);
        }
        else
        {
            using var updated = await client.PatchAsJsonAsync(InstancePath, new PatchInstanceSmtpSettingsDto
            {
                Configuration = OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Set(new()
                {
                    Host = "smtp.private.test", Port = 587, Security = "StartTls",
                    FromAddress = "events@private.test", FromName = "Changed sender", TimeoutSeconds = 30
                })
            });
            await AssertStatusAsync(updated, HttpStatusCode.OK);
        }
        using (var stale = await client.PostAsJsonAsync(path + "/disable", Confirmation(preview.RootElement, Acknowledgement)))
            await AssertStatusAsync(stale, HttpStatusCode.Conflict);

        using var freshResponse = await client.PostAsync(path + "/disable-preview", null);
        await AssertStatusAsync(freshResponse, HttpStatusCode.OK);
        using var fresh = await ReadAsync(freshResponse);
        string idempotencyKey = Guid.CreateVersion7().ToString("D");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Link(fresh.RootElement, "disable"))
            {
                Content = JsonContent.Create(Confirmation(fresh.RootElement, Acknowledgement))
            };
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            using var disabled = await client.SendAsync(request);
            await AssertStatusAsync(disabled, attempt == 0 ? HttpStatusCode.OK : HttpStatusCode.Conflict);
            await AssertPrivateAsync(disabled);
            await Assert.That(disabled.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        }
        string enableLink;
        using (var readback = await client.GetAsync(settingsPath))
        {
            await AssertStatusAsync(readback, HttpStatusCode.OK);
            using var state = await ReadAsync(readback);
            await AssertEnabledAsync(state.RootElement, tenant, false);
            enableLink = Link(state.RootElement, tenant ? "enable" : "edit");
            await Assert.That(state.RootElement.GetProperty("_links").TryGetProperty("disable-preview", out _)).IsFalse();
        }
        if (!tenant)
        {
            using var directFalse = await client.PatchAsJsonAsync(InstancePath,
                new PatchInstanceSmtpSettingsDto { DeliveryEnabled = OptionalUpdate<bool>.Set(false) });
            await AssertStatusAsync(directFalse, HttpStatusCode.BadRequest);
        }
        using (var enabled = tenant
            ? await client.PutAsJsonAsync(enableLink, new { value = "true" })
            : await client.PatchAsJsonAsync(enableLink, new PatchInstanceSmtpSettingsDto { DeliveryEnabled = OptionalUpdate<bool>.Set(true) }))
            await AssertStatusAsync(enabled, HttpStatusCode.OK);
        using (var readback = await client.GetAsync(settingsPath))
        {
            using var state = await ReadAsync(readback);
            await AssertEnabledAsync(state.RootElement, tenant, true);
        }
        using (var directFalse = tenant
            ? await client.PutAsJsonAsync("/api/settings/tenant/keys/email.delivery_enabled", new { value = "false" })
            : await client.PatchAsJsonAsync(InstancePath, new PatchInstanceSmtpSettingsDto { DeliveryEnabled = OptionalUpdate<bool>.Set(false) }))
            await AssertStatusAsync(directFalse, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ScopeAndPersistedAuthorityCannotComeFromTheBodyOrAnOldToken()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = factory.CreateClient();
        Guid actor = await AuthenticateAsync(factory, client, "tenant");
        using (var forbidden = await client.PostAsync(InstancePath + "/disable-preview", null))
            await AssertStatusAsync(forbidden, HttpStatusCode.Forbidden);
        using var previewResponse = await client.PostAsync(TenantPath + "/disable-preview", null);
        await AssertStatusAsync(previewResponse, HttpStatusCode.OK);
        using var preview = await ReadAsync(previewResponse);
        using (var scopeInjection = await client.PostAsJsonAsync(TenantPath + "/disable", new
        {
            tenantId = Guid.CreateVersion7(), expectedRevision = preview.RootElement.GetProperty("expectedRevision").GetInt64(),
            confirmationToken = preview.RootElement.GetProperty("confirmationToken").GetString(), acknowledgement = Acknowledgement
        }))
            await AssertStatusAsync(scopeInjection, HttpStatusCode.BadRequest);
        using (var crossScope = await client.PostAsJsonAsync(InstancePath + "/disable", Confirmation(preview.RootElement, Acknowledgement)))
            await AssertStatusAsync(crossScope, HttpStatusCode.Forbidden);

        await using (var database = factory.CreateDatabase())
        {
            var grant = await database.TenantUserRoleGrants
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.TenantUser.UserId == actor);
            grant.RevokedAt = DateTime.UtcNow;
            await database.SaveChangesAsync();
        }
        using (var revoked = await client.PostAsJsonAsync(TenantPath + "/disable", Confirmation(preview.RootElement, Acknowledgement)))
            await AssertStatusAsync(revoked, HttpStatusCode.Forbidden);
        using (var forbidden = await client.PostAsync(TenantPath + "/disable-preview", null))
            await AssertStatusAsync(forbidden, HttpStatusCode.Forbidden);
        using (var settingsResponse = await client.GetAsync(TenantSettingsPath))
        {
            using var settings = await ReadAsync(settingsResponse);
            await Assert.That(settings.RootElement.GetProperty("_links").TryGetProperty("disable-preview", out _)).IsFalse();
        }
    }

    [Test]
    public async Task InstanceLockWithdrawsTenantDisableAndInvalidatesConfirmation()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = factory.CreateClient();
        await AuthenticateAsync(factory, client, "instance");
        using var previewResponse = await client.PostAsync(TenantPath + "/disable-preview", null);
        await AssertStatusAsync(previewResponse, HttpStatusCode.OK);
        using var preview = await ReadAsync(previewResponse);
        using (var lockedSettings = await client.PatchAsJsonAsync("/api/instance/settings/tenant-delegation",
            new PatchTenantDelegationSettingsDto { LockTenantSmtp = OptionalUpdate<bool>.Set(true) }))
            await AssertStatusAsync(lockedSettings, HttpStatusCode.OK);
        using (var stale = await client.PostAsJsonAsync(TenantPath + "/disable", Confirmation(preview.RootElement, Acknowledgement)))
            await AssertStatusAsync(stale, HttpStatusCode.Conflict);
        using var lockedResponse = await client.PostAsync(TenantPath + "/disable-preview", null);
        await AssertStatusAsync(lockedResponse, HttpStatusCode.OK);
        using var locked = await ReadAsync(lockedResponse);
        await Assert.That(locked.RootElement.GetProperty("isLocked").GetBoolean()).IsTrue();
        await Assert.That(locked.RootElement.TryGetProperty("confirmationToken", out var token) && token.ValueKind != JsonValueKind.Null).IsFalse();
        await Assert.That(locked.RootElement.GetProperty("_links").TryGetProperty("disable", out _)).IsFalse();
    }

    [Test]
    [Arguments("none")]
    [Arguments("otherTenant")]
    public async Task NonAdministratorsAndAdministratorsOfAnotherTenantFailClosed(string authority)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = factory.CreateClient();
        await AuthenticateAsync(factory, client, authority);
        foreach (string path in new[] { InstancePath, TenantPath })
        {
            using var preview = await client.PostAsync(path + "/disable-preview", null);
            await AssertStatusAsync(preview, HttpStatusCode.Forbidden);
            await AssertPrivateAsync(preview);
            using var disable = await client.PostAsJsonAsync(path + "/disable", new
            {
                expectedRevision = 0, confirmationToken = "untrusted", acknowledgement = Acknowledgement
            });
            await AssertStatusAsync(disable, HttpStatusCode.Forbidden);
        }
    }

    private static async Task<Guid> AuthenticateAsync(LocalAdmissionWebApplicationFactory factory, HttpClient client, string authority)
    {
        var login = await factory.SeedLocalUserAsync(emailConfirmed: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IEmailDeliverySettingsWriter>().ApplyAsync(
                [new(null, GovernanceSettingKeys.TenantDelegation.LockSmtp, EmailDeliverySettingMutationKind.SetValue, "false")], null);
            await Assert.That(result.Status is EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange).IsTrue();
        }
        Guid actor;
        await using (ExploreDbContext database = factory.CreateDatabase())
        {
            actor = await database.Users.Where(user => user.Pii.Email == login.Identifier).Select(user => user.Id).SingleAsync();
            if (authority == "instance")
            {
                database.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(), UserId = actor, User = null!, RoleId = (int)RoleEnum.Admin,
                    Role = null!, GrantedAt = DateTime.UtcNow
                });
            }
            else if (authority != "none")
            {
                Guid tenantId = PlatformDefaults.DefaultTenantId;
                if (authority == "otherTenant")
                {
                    tenantId = Guid.CreateVersion7();
                    database.Tenants.Add(new Tenant
                    {
                        Id = tenantId, FullName = "Other tenant", Slug = "other-tenant",
                        TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = DateTime.UtcNow
                    });
                }
                var membership = new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
                    UserId = actor, User = null!, StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
                };
                database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(), TenantId = membership.TenantId, Tenant = null!, TenantUserId = membership.Id,
                    TenantUser = membership, RoleId = (int)RoleEnum.TenantAdmin, Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant, GrantedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
                });
            }
            await database.SaveChangesAsync();
        }
        using var response = await client.PostAsJsonAsync("/api/auth/local/login", login);
        await AssertStatusAsync(response, HttpStatusCode.OK);
        using var body = await ReadAsync(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        return actor;
    }

    private static object Confirmation(JsonElement preview, string? acknowledgement) => new
    {
        expectedRevision = preview.GetProperty("expectedRevision").GetInt64(),
        confirmationToken = preview.GetProperty("confirmationToken").GetString(), acknowledgement
    };

    private static string Link(JsonElement resource, string relation) => resource.GetProperty("_links").GetProperty(relation).GetProperty("href").GetString()!;
    private static Task<JsonDocument> ReadAsync(HttpResponseMessage response) => JsonDocument.ParseAsync(response.Content.ReadAsStream());
    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        await Assert.That(response.StatusCode).IsEqualTo(expected).Because(await response.Content.ReadAsStringAsync());
    private static async Task AssertPrivateAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }
    private static async Task AssertProblemAsync(HttpResponseMessage response, string type)
    {
        using var body = await ReadAsync(response);
        await Assert.That(body.RootElement.GetProperty("type").GetString()).IsEqualTo(type);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
    }
    private static async Task AssertEnabledAsync(JsonElement resource, bool tenant, bool enabled)
    {
        bool actual = tenant
            ? bool.Parse(resource.GetProperty("settings").EnumerateArray().Single(setting => setting.GetProperty("key").GetString() == GovernanceSettingKeys.Email.DeliveryEnabled).GetProperty("value").GetString()!)
            : resource.GetProperty("deliveryEnabled").GetBoolean();
        await Assert.That(actual).IsEqualTo(enabled);
    }
    private static async Task AssertPreviewAsync(JsonElement preview, bool tenant)
    {
        if (tenant)
            await Assert.That(preview.GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        else
            await Assert.That(!preview.TryGetProperty("tenantId", out var id) || id.ValueKind == JsonValueKind.Null).IsTrue();
        string[] safe = ["tenantId", "expectedRevision", "isLocked", "affectedScopes", "confirmationToken", "expiresAtUtc", "canDisable", "_links"];
        await Assert.That(preview.EnumerateObject().All(property => safe.Contains(property.Name))).IsTrue();
        await Assert.That(preview.GetProperty("canDisable").GetBoolean()).IsTrue();
    }
}
