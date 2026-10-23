using System.Security.Claims;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

/// <summary>Real EF source images; these tests do not certify PostgreSQL grants or producer coordination.</summary>
public sealed class IdentityWriterHumanSourceQueryTests
{
    [Test]
    public async Task PersistedDeletionAndRevocationAreNotHiddenByFiltersOrTrackedEntities()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var subject = await SubjectAsync(fixture);
        var trackedUser = await context.Users.SingleAsync(row => row.Id == fixture.UserId);
        var membership = await context.TenantUsers.SingleAsync(row =>
            row.UserId == fixture.UserId && row.TenantId == fixture.TenantId);
        DateTime revokedAt = DateTime.UtcNow;
        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = null!,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = revokedAt,
            CreatedAt = revokedAt
        });
        await context.SaveChangesAsync();
        await context.Users.Where(row => row.Id == fixture.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        await context.TenantUsers.Where(row => row.Id == membership.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        await context.TenantUserRoleGrants.Where(row => row.TenantUserId == membership.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, revokedAt));
        await Assert.That(trackedUser.IsDeleted).IsFalse();
        await Assert.That(membership.IsDeleted).IsFalse();

        var execution = Bind(fixture);
        await using var securityGate = new HumanSecuritySourceGate(context);
        await using var securityLease = await securityGate.AcquireAsync();
        var facts = await IdentityWriterHumanSourceQueries.ReadPrincipalAsync(execution, subject, null);

        await Assert.That(facts.PrincipalId).IsEqualTo(fixture.UserId);
        await Assert.That(facts.SelectedTenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(facts.Users.Single().IsDeleted).IsTrue();
        await Assert.That(facts.TenantMemberships.Single(row => row.Id == membership.Id).IsDeleted).IsTrue();
        await Assert.That(facts.TenantGrants.Length > 0).IsTrue();
        await Assert.That(facts.TenantGrants.All(row => row.RevokedAt == revokedAt)).IsTrue();
        await Assert.That(trackedUser.IsDeleted).IsFalse();
        await Assert.That(membership.IsDeleted).IsFalse();
    }

    [Test]
    public async Task UnrelatedPermissionMappingDisablesFallbackWithoutDroppingDeletedOwnerFacts()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var subject = await SubjectAsync(fixture);
        var unrelated = await context.RolePermissions.AsNoTracking().FirstAsync(row =>
            row.RoleId != (int)RoleEnum.OrgAdmin && row.RoleId != (int)RoleEnum.GroupAdmin);
        await context.RolePermissions.ExecuteDeleteAsync();
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "Source projection owner" },
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var placement = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
            ApprovalStatus = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.OrganizationMembers.Add(new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            OrganizationTenantId = placement.Id,
            OrganizationTenant = placement,
            TenantId = fixture.TenantId,
            Tenant = null!,
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var execution = Bind(fixture);
        await using var securityGate = new HumanSecuritySourceGate(context);
        await using var securityLease = await securityGate.AcquireAsync();
        var before = await IdentityWriterHumanSourceQueries.ReadPrincipalAsync(execution, subject, null);
        await Assert.That(before.PermissionMappingsExist).IsFalse();
        await Assert.That(before.Organizations.Single(row => row.OwnerId == organization.Id).OwnerIsDeleted).IsTrue();
        await Assert.That(before.EventCreateOrganizations.Contains(organization.Id)).IsTrue();
        await Assert.That(before.EventFinanceOrganizations.Contains(organization.Id)).IsTrue();

        context.RolePermissions.Add(unrelated);
        await context.SaveChangesAsync();
        var after = await IdentityWriterHumanSourceQueries.ReadPrincipalAsync(execution, subject, null);
        await Assert.That(after.PermissionMappingsExist).IsTrue();
        await Assert.That(after.EventCreateOrganizations.Contains(organization.Id)).IsFalse();
        await Assert.That(after.EventFinanceOrganizations.Contains(organization.Id)).IsFalse();
    }

    [Test]
    public async Task RouteContainsExactlySixKeysAndPreservesMetadataWithoutReturningUnrelatedValues()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        string key = GovernanceSettingKeys.Security.AuthorizationProvider;
        await context.SystemSettings.Where(row => HumanAuthorizationRouteFacts.Keys.Contains(row.SettingKey))
            .ExecuteDeleteAsync();
        var selected = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = key,
            Value = "\"local\"",
            ValueType = SettingValueType.String,
            IsLocked = true,
            Description = "Original route description",
            Category = "Security",
            AllowedValues = "[\"local\",\"cerbos\"]",
            CreatedAt = DateTime.UtcNow
        };
        context.SystemSettings.Add(selected);
        context.SystemSettings.Add(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = "source_projection.unrelated",
            Value = "\"excluded\"",
            ValueType = SettingValueType.String,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var execution = Bind(fixture);
        await using var securityGate = new HumanSecuritySourceGate(context);
        await using var securityLease = await securityGate.AcquireAsync();
        var facts = await IdentityWriterHumanSourceQueries.ReadRouteAsync(execution, await SubjectAsync(fixture));

        await Assert.That(facts.Settings.Select(row => row.Key).SequenceEqual(HumanAuthorizationRouteFacts.Keys)).IsTrue();
        var route = facts.Settings.Single(row => row.Key == key).System!;
        await Assert.That(route.Value).IsEqualTo(selected.Value);
        await Assert.That(route.IsLocked).IsTrue();
        await Assert.That(route.Description).IsEqualTo(selected.Description);
        await Assert.That(route.Category).IsEqualTo(selected.Category);
        await Assert.That(route.AllowedValues).IsEqualTo(selected.AllowedValues);
        await Assert.That(facts.Settings.Count(row => row.System is null)).IsEqualTo(5);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalPrivateScalarsBecomeOrdinalStampAndUnicodeWhitespaceFacts(bool malformedMetadata)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        string stamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var identity = new LocalIdentityUser
        {
            Id = fixture.UserId,
            FirstName = "Source",
            LastName = "Projection",
            PasswordHash = "\u0085\u00a0\u1680\u2000\u2028\u202f\u205f\u3000",
            SecurityStamp = stamp,
            EmailConfirmed = true
        };
        context.AddRange(identity, new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = fixture.UserId.ToString("D"),
            CreatedAt = DateTime.UtcNow
        });
        if (malformedMetadata)
            context.Add(new IdentityUserToken<Guid>
            {
                UserId = fixture.UserId,
                LoginProvider = LocalCredentialStateMetadata.TokenLoginProvider,
                Name = LocalCredentialStateMetadata.TokenName,
                Value = "{\"Version\":"
            });
        await context.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", fixture.UserId.ToString("D")), new Claim("auth_provider", "local"),
                new Claim("iss", LocalIdentityOptions.Issuer)], "source-projection"));
        var subject = await HumanCommitSubject.ResolveAsync(principal,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(), default)
            ?? throw new InvalidOperationException("The test requires the persisted Local binding.");
        var execution = Bind(fixture);
        await using var securityGate = new HumanSecuritySourceGate(context);
        await using var securityLease = await securityGate.AcquireAsync();
        await using var lifecycle = await LocalIdentityLifecycleGateCustody.EnterAsync(context, CancellationToken.None);
        var exact = await IdentityWriterHumanSourceQueries.ReadPrincipalAsync(
            execution, subject, new LocalSessionAuthority(fixture.UserId, stamp, true));
        await Assert.That(exact.LocalSession!.Accounts.Single().HasPassword).IsFalse();
        await Assert.That(exact.LocalSession.Accounts.Single().StampMatches).IsTrue();
        await Assert.That(exact.LocalSession.CredentialState).IsNull();

        await context.LocalIdentityUsers.Where(row => row.Id == fixture.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, stamp + " ")
                .SetProperty(row => row.PasswordHash, stamp));
        var changed = await IdentityWriterHumanSourceQueries.ReadPrincipalAsync(
            execution, subject, new LocalSessionAuthority(fixture.UserId, stamp, true));
        await Assert.That(changed.LocalSession!.Accounts.Single().HasPassword).IsTrue();
        await Assert.That(changed.LocalSession.Accounts.Single().StampMatches).IsFalse();
        await Assert.That(identity.SecurityStamp).IsEqualTo(stamp);
    }

    private static LocalIdentityExecutionLease Bind(EventVisitorCapabilitySqliteFixture fixture)
    {
        var execution = new LocalIdentityExecutionLease();
        execution.Bind(fixture.Context, fixture.Context);
        return execution;
    }

    private static async Task<HumanCommitSubject> SubjectAsync(EventVisitorCapabilitySqliteFixture fixture) =>
        await HumanCommitSubject.ResolveAsync(
            fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None) ?? throw new InvalidOperationException("The fixture requires a resolved human.");
}
