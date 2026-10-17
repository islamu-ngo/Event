using System.Text.Json;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class TenantUserRoleGrantMapperTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DetailAndListKeepDistinctScalarDisclosure(bool detail)
    {
        var grant = Grant();
        object result = detail ? TenantUserRoleGrantMapper.ToDetail(grant) : TenantUserRoleGrantMapper.ToListItem(grant);
        object expected = detail
            ? new
            {
                grant.Id,
                grant.TenantUserId,
                UserId = grant.TenantUser.UserId,
                UserEmail = "member@example.test",
                UserFullName = "Member Name",
                grant.TenantId,
                TenantFullName = "Tenant",
                grant.RoleId,
                RoleName = "Member",
                grant.GrantedAt,
                grant.GrantedBy,
                grant.RevokedAt,
                grant.RevokedBy,
                grant.RevocationReason,
                grant.CreatedAt,
                grant.UpdatedAt
            }
            : new
            {
                grant.Id,
                grant.TenantUserId,
                UserId = grant.TenantUser.UserId,
                UserEmail = "member@example.test",
                UserFullName = "Member Name",
                grant.TenantId,
                TenantFullName = "Tenant",
                grant.RoleId,
                RoleName = "Member",
                grant.GrantedAt,
                grant.RevokedAt
            };

        await Assert.That(JsonSerializer.Serialize(result)).IsEqualTo(JsonSerializer.Serialize(expected));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task MissingUserPiiRemainsAbsentWithoutLosingGrantIdentity(bool detail)
    {
        var grant = Grant();
        grant.TenantUser.User.Pii = null!;
        object result = detail ? TenantUserRoleGrantMapper.ToDetail(grant) : TenantUserRoleGrantMapper.ToListItem(grant);
        var json = JsonSerializer.SerializeToNode(result)!;

        await Assert.That(json["UserEmail"]).IsNull();
        await Assert.That(json["UserFullName"]).IsNull();
        await Assert.That(json["UserId"]!.GetValue<Guid>()).IsEqualTo(grant.TenantUser.UserId);
        await Assert.That(grant.TenantUser.User.Pii).IsNull();
    }

    [Test]
    public async Task MappedResultsRemainDetachedFromTheirSourceEntities()
    {
        var grant = Grant();
        var rows = new List<TenantUserRoleGrant> { grant };
        // The actual query owner's missing-result and disclosure controls now use
        // genuine account custody in HumanAccountMembershipCustodyTests.
        var detail = TenantUserRoleGrantMapper.ToDetail(grant);
        var list = rows.Select(TenantUserRoleGrantMapper.ToListItem).ToList();
        grant.Role.FullName = "Changed role";
        grant.Tenant.FullName = "Changed tenant";
        grant.TenantUser.User.FirstName = "Changed user";
        rows.Clear();

        await Assert.That(detail!.UserFullName).IsEqualTo("Member Name");
        await Assert.That(detail.RoleName).IsEqualTo("Member");
        await Assert.That(detail.TenantFullName).IsEqualTo("Tenant");
        await Assert.That(list.Single().UserFullName).IsEqualTo("Member Name");
    }

    private static TenantUserRoleGrant Grant()
    {
        var tenant = new Tenant
        {
            Id = Guid.Parse("01990000-0000-7000-8000-000000000030"),
            FullName = "Tenant",
            Slug = "tenant",
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.Parse("01990000-0000-7000-8000-000000000031"),
            Pii = new UserPii { Email = "member@example.test", FirstName = "Member", LastName = "Name" }
        };
        var tenantUser = new TenantUser
        {
            Id = Guid.Parse("01990000-0000-7000-8000-000000000032"),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ModerationNote = "Not part of grant disclosure"
        };
        return new TenantUserRoleGrant
        {
            Id = Guid.Parse("01990000-0000-7000-8000-000000000033"),
            TenantId = tenant.Id,
            Tenant = tenant,
            TenantUserId = tenantUser.Id,
            TenantUser = tenantUser,
            RoleId = 3,
            Role = new Role { Id = 3, MasterCode = "PRIVATE_CODE", FullName = "Member" },
            RoleScopeId = 999,
            GrantedAt = DateTime.UnixEpoch,
            GrantedBy = user.Id,
            RevokedAt = DateTime.UnixEpoch.AddDays(1),
            RevokedBy = user.Id,
            RevocationReason = "Completed",
            CreatedAt = DateTime.UnixEpoch,
            CreatedBy = user.Id,
            UpdatedAt = DateTime.UnixEpoch.AddHours(1),
            UpdatedBy = user.Id
        };
    }
}
