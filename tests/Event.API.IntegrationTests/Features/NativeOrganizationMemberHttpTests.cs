using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeOrganizationMemberHttpTests
{
    [Test]
    public async Task MembershipWrites_PreserveInvitationUniquenessAndLastAdministrator()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.Admin);
        using var outsider = Client(factory, data.Outsider);
        using (var denied = await outsider.PostAsJsonAsync("/api/organizationmember",
            new { organizationId = data.OrganizationId, email = data.Invitee.Email }))
        {
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(denied)).GetProperty("success").GetBoolean()).IsFalse();
        }
        using var created = await admin.PostAsJsonAsync("/api/organizationmember",
            new { organizationId = data.OrganizationId, email = data.Invitee.Email, role = RoleEnum.OrgMember });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await JsonAsync(created);
        await Assert.That(result.GetProperty("success").GetBoolean()).IsTrue();
        var id = result.GetProperty("id").GetGuid();
        var member = await ReadMemberAsync(factory, id);
        await Assert.That(member.UserId).IsEqualTo(data.Invitee.Id);
        await Assert.That(member.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(member.OrganizationTenantId).IsEqualTo(data.ParticipationId);
        using (var duplicate = await admin.PostAsJsonAsync("/api/organizationmember",
            new { organizationId = data.OrganizationId, email = data.Invitee.Email }))
        {
            await Assert.That(duplicate.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(duplicate)).GetProperty("success").GetBoolean()).IsFalse();
        }
        using (var lastAdmin = await admin.PutAsJsonAsync("/api/organizationmember/role",
            new { id = data.AdminMemberId, role = RoleEnum.OrgMember }))
        {
            await Assert.That(lastAdmin.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(lastAdmin)).GetProperty("success").GetBoolean()).IsFalse();
        }
        using (var removeLastAdmin = await admin.DeleteAsync($"/api/organizationmember/{data.AdminMemberId}"))
        {
            await Assert.That(removeLastAdmin.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(removeLastAdmin)).GetProperty("success").GetBoolean()).IsFalse();
        }
        using (var promoted = await admin.PutAsJsonAsync("/api/organizationmember/role",
            new { id, role = RoleEnum.OrgAdmin }))
        {
            await Assert.That(promoted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(promoted)).GetProperty("success").GetBoolean()).IsTrue();
        }
        await Assert.That((await ReadMemberAsync(factory, id)).RoleId).IsEqualTo((int)RoleEnum.OrgAdmin);
        using (var demoted = await admin.PutAsJsonAsync("/api/organizationmember/role",
            new { id = data.AdminMemberId, role = RoleEnum.OrgMember }))
        {
            await Assert.That(demoted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(demoted)).GetProperty("success").GetBoolean()).IsTrue();
        }
        using var successor = Client(factory, data.Invitee);
        using (var removed = await successor.DeleteAsync($"/api/organizationmember/{data.AdminMemberId}"))
        {
            await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(removed)).GetProperty("success").GetBoolean()).IsTrue();
        }
        await Assert.That((await ReadMemberAsync(factory, data.AdminMemberId)).IsDeleted).IsTrue();
        await Assert.That((await ReadMemberAsync(factory, id)).IsDeleted).IsFalse();
    }

    [Test]
    public async Task InvitationValidation_IsReadOnlyAndDeclineRemainsRecipientBound()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.Admin);
        using var recipient = Client(factory, data.Invitee);
        using var outsider = Client(factory, data.Outsider);
        using var created = await admin.PostAsJsonAsync("/api/organizationmember",
            new { organizationId = data.OrganizationId, email = data.Invitee.Email });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var before = await ReadMemberAsync(factory, id);
        using var invitations = await recipient.GetAsync("/api/organizationmember/invitations");
        await Assert.That(invitations.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await JsonAsync(invitations)).EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())).IsEquivalentTo(new[] { id });
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .SetTenant(PlatformDefaults.DefaultTenantId);
            var query = scope.ServiceProvider.GetRequiredService<
                IQueryHandler<ValidateOrganizationInvitationQuery, BaseCommandResponse<Guid>>>();
            foreach (var userId in new[] { Guid.Empty, data.Outsider.Id })
            {
                var rejected = await query.QueryAsync(
                    new ValidateOrganizationInvitationQuery { InvitationId = id, UserId = userId }, default);
                await Assert.That(rejected.IsSuccess).IsFalse();
            }
            var valid = await query.QueryAsync(
                new ValidateOrganizationInvitationQuery { InvitationId = id, UserId = data.Invitee.Id }, default);
            await Assert.That(valid.IsSuccess).IsTrue();
            await Assert.That(valid.Id).IsEqualTo(id);
        }
        using (var accepted = await recipient.PostAsync($"/api/organizationmember/invitations/{id}/accept", null))
        {
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(accepted)).GetProperty("success").GetBoolean()).IsTrue();
        }
        var after = await ReadMemberAsync(factory, id);
        await Assert.That(after.IsDeleted).IsFalse();
        await Assert.That(after.RoleId).IsEqualTo(before.RoleId);
        await Assert.That(after.UserId).IsEqualTo(before.UserId);
        await Assert.That(after.UpdatedAt).IsEqualTo(before.UpdatedAt);
        foreach (var action in new[] { "accept", "decline" })
        {
            using var forbidden = await outsider.PostAsync($"/api/organizationmember/invitations/{id}/{action}", null);
            await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(forbidden)).GetProperty("success").GetBoolean()).IsFalse();
        }
        await Assert.That((await ReadMemberAsync(factory, id)).IsDeleted).IsFalse();
        using var declined = await recipient.PostAsync($"/api/organizationmember/invitations/{id}/decline", null);
        await Assert.That(declined.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await JsonAsync(declined)).GetProperty("success").GetBoolean()).IsTrue();
        await Assert.That((await ReadMemberAsync(factory, id)).IsDeleted).IsTrue();
        using var missing = await recipient.PostAsync($"/api/organizationmember/invitations/{id}/accept", null);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await JsonAsync(missing)).GetProperty("success").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task MemberReads_KeepAuthenticatedTenantBoundariesAndBoundedDisclosure()
    {
        Guid organizationId = Guid.Empty;
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider
            {
                CheckPredicate = request => request.ResourceId == organizationId.ToString()
                    || request.Facts is OrganizationMemberAuthorizationFacts facts
                        && facts.TenantId == PlatformDefaults.DefaultTenantId
            }
        };
        var data = await SeedAsync(factory);
        organizationId = data.OrganizationId;
        using var client = Client(factory, data.Admin);
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.GetAsync($"/api/organizationmember/{organizationId}");
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var list = await client.GetAsync($"/api/organizationmember/{organizationId}");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var items = (await JsonAsync(list)).GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
        await Assert.That(items.Select(item => item.GetProperty("id").GetGuid()))
            .IsEquivalentTo(new[] { data.AdminMemberId });
        using var detail = await client.GetAsync($"/api/organizationmember/member/{data.AdminMemberId}");
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await JsonAsync(detail);
        await Assert.That(body.GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(body.GetProperty("organizationId").GetGuid()).IsEqualTo(Guid.Empty);
        await Assert.That(body.GetProperty("organizationFullName").GetString()).IsEqualTo("Native organization");
        await Assert.That(body.GetProperty("userId").GetGuid()).IsEqualTo(data.Admin.Id);
        await Assert.That(body.GetProperty("userEmail").GetString()).IsEqualTo(data.Admin.Email);
        await Assert.That(body.TryGetProperty("user", out _)).IsFalse();
        await Assert.That(body.TryGetProperty("organizationTenant", out _)).IsFalse();
        using var foreign = await client.GetAsync($"/api/organizationmember/member/{data.ForeignMemberId}");
        await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var missing = await client.GetAsync($"/api/organizationmember/member/{Guid.CreateVersion7()}");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static HttpClient Client(AuthenticatedWebApplicationFactory factory, User user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(user.Id, "Member", ("email", user.Email)));
        return client;
    }

    private static async Task<SeedData> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            Slug = "native-org-members",
            FullName = "Native organization",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        var foreignTenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Slug = "foreign-org-members",
            FullName = "Foreign organization",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        db.Tenants.AddRange(tenant, foreignTenant);
        var admin = NewUser("admin");
        var invitee = NewUser("invitee");
        var outsider = NewUser("outsider");
        db.Users.AddRange(admin, invitee, outsider);
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new() { FullName = "Native organization" }
        };
        var foreignOrganization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new() { FullName = "Foreign organization" }
        };
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        };
        var foreignParticipation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = foreignTenant.Id,
            Tenant = foreignTenant,
            OrganizationId = foreignOrganization.Id,
            Organization = foreignOrganization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!
        };
        db.OrganizationTenants.AddRange(participation, foreignParticipation);
        var adminMember = NewMember(participation, admin, RoleEnum.OrgAdmin);
        var foreignMember = NewMember(foreignParticipation, invitee, RoleEnum.OrgMember);
        db.OrganizationMembers.AddRange(adminMember, foreignMember);
        await db.SaveChangesAsync();
        return new SeedData(organization.Id, participation.Id, adminMember.Id, foreignMember.Id, admin, invitee, outsider);
    }

    private static User NewUser(string name) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new() { Email = $"{name}@example.test", FirstName = name, LastName = "Member" }
    };

    private static OrganizationMember NewMember(OrganizationTenant participation, User user, RoleEnum role) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationTenantId = participation.Id,
        OrganizationTenant = participation,
        TenantId = participation.TenantId,
        Tenant = participation.Tenant,
        UserId = user.Id,
        User = user,
        RoleId = (int)role,
        Role = null!
    };

    private static async Task<OrganizationMember> ReadMemberAsync(AuthenticatedWebApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().OrganizationMembers
            .IgnoreQueryFilters(["SoftDelete"]).AsNoTracking().SingleAsync(member => member.Id == id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private sealed record SeedData(
        Guid OrganizationId, Guid ParticipationId, Guid AdminMemberId, Guid ForeignMemberId,
        User Admin, User Invitee, User Outsider);
}
