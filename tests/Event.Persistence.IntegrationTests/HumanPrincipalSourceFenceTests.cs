using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain.Constants;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class HumanPrincipalSourceFenceTests
{
    [Test]
    public async Task DeletedOwnerStillContributesPermissionListButNotLocalAdministratorMembership()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var unrelated = await context.RolePermissions.AsNoTracking().FirstAsync(row =>
            row.RoleId != (int)RoleEnum.OrgAdmin && row.RoleId != (int)RoleEnum.GroupAdmin);
        await context.RolePermissions.ExecuteDeleteAsync();
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "Source organization" },
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
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            FullName = "Source group",
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var groupPlacement = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            GroupId = group.Id,
            Group = group,
            ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
            ApprovalStatus = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.GroupMembers.Add(new GroupMember
        {
            Id = Guid.CreateVersion7(),
            GroupTenantId = groupPlacement.Id,
            GroupTenant = groupPlacement,
            TenantId = fixture.TenantId,
            Tenant = null!,
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.GroupAdmin,
            Role = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var source = new HumanPrincipalSourceFence(context, await SubjectAsync(fixture), PolicySourceSetPurpose.RuntimeTenant);
        var before = await source.ObserveAsync();
        await Assert.That(before.PermissionMappingsExist).IsFalse();
        await Assert.That(before.EventCreateOrganizations.Contains(organization.Id)).IsTrue();
        await Assert.That(before.EventFinanceGroups.Contains(group.Id)).IsTrue();
        await Assert.That(before.Organizations.Single(row => row.OwnerId == organization.Id).OwnerIsDeleted).IsTrue();
        await Assert.That(await new OrganizationMemberRepository(context)
            .GetByOrganizationAndUser(organization.Id, fixture.UserId)).IsNull();
        await Assert.That(before.EventCreateOrganizations.SequenceEqual(await new OrganizationMemberRepository(context)
            .GetOrganizationIdsWhereUserHasPermission(fixture.UserId, PermissionCodes.EventCreate))).IsTrue();
        await Assert.That(before.EventFinanceGroups.SequenceEqual(await new GroupMemberRepository(context)
            .GetGroupIdsWhereUserHasPermission(fixture.UserId, PermissionCodes.EventManageFinance))).IsTrue();
        context.RolePermissions.Add(unrelated);
        await context.SaveChangesAsync();
        var after = await source.ObserveAsync();
        await Assert.That(after.PermissionMappingsExist).IsTrue();
        await Assert.That(after.EventCreateOrganizations.Contains(organization.Id)).IsFalse();
        await Assert.That(after.EventFinanceGroups.Contains(group.Id)).IsFalse();
    }

    [Test]
    public async Task ColocatedLocalFactsRetainRevocationInputsAndRejectSubstituteIdentityContext()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var identity = new LocalIdentityUser
        {
            Id = fixture.UserId,
            FirstName = "Local",
            LastName = "Source",
            PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            EmailConfirmed = true
        };
        Guid loginId = Guid.CreateVersion7();
        Guid operationId = Guid.CreateVersion7();
        context.AddRange(identity, new UserExternalLogin
        {
            Id = loginId,
            UserId = fixture.UserId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = fixture.UserId.ToString("D"),
            CreatedAt = DateTime.UtcNow
        }, new IdentityUserToken<Guid>
        {
            UserId = fixture.UserId,
            LoginProvider = LocalCredentialStateMetadata.TokenLoginProvider,
            Name = LocalCredentialStateMetadata.TokenName,
            Value = JsonSerializer.Serialize(new LocalCredentialStateMetadata(1, LocalCredentialState.Ready, operationId, fixture.UserId))
        }, new LocalIdentityCredentialOperation(new LocalCredentialOperationReceipt(
            operationId, LocalCredentialOperationKind.Create, LocalCredentialOperationStage.ProvisioningPending,
            fixture.UserId, fixture.UserId, fixture.ActorId, loginId, DateTime.UtcNow), Guid.CreateVersion7()));
        await context.SaveChangesAsync();
        await context.Set<LocalIdentityCredentialOperation>().Where(row => row.Id == operationId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Stage, LocalCredentialOperationStage.Replaced));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", fixture.UserId.ToString("D")), new Claim("auth_provider", "local"),
                new Claim("iss", LocalIdentityOptions.Issuer)], "fixture"));
        var subject = await HumanCommitSubject.ResolveAsync(principal,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(), default)
            ?? throw new InvalidOperationException("A persisted Local binding is required.");
        var authority = new LocalSessionAuthority(fixture.UserId, identity.SecurityStamp!, true);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            var unsupported = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant, authority);
            await unsupported.ObserveAsync();
        });
        await using (var externalIdentity = EmailDispatchSqliteFixture.CreateContext(fixture.DatabasePath))
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                var unsupported = new HumanPrincipalSourceFence(context, subject,
                    PolicySourceSetPurpose.RuntimeTenant, authority, externalIdentity);
                await unsupported.ObserveAsync();
            });
            await Assert.That(context.Database.CurrentTransaction).IsNull();
        }
        var source = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant, authority, context);
        var before = await source.ObserveAsync();
        HumanPrincipalSourceValidation.RequireCurrent(subject, authority, before);
        await Assert.That(before.LocalSession!.Accounts.Single().StampMatches).IsTrue();
        await Assert.That(before.LocalSession.Accounts.Single().HasPassword).IsTrue();
        await Assert.That(before.LocalSession.CredentialState!.OperationId).IsEqualTo(operationId);
        await Assert.That(before.LocalSession.Operations.Single().Stage).IsEqualTo((int)LocalCredentialOperationStage.Replaced);
        await Assert.That(before.LocalSession.PersonalActors.Any(row => row.Id == fixture.ActorId)).IsTrue();
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var held = await source.AcquireAsync(await sets.AcquireAsync());
            await Assert.That(held.Facts.LocalSession!.Accounts.Single().StampMatches).IsTrue();
            await held.ValidateAsync();
            await owner.RollbackAsync();
        }
        await context.LocalIdentityUsers.Where(row => row.Id == fixture.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("N")));
        var revoked = await source.ObserveAsync();
        await Assert.That(revoked.LocalSession!.Accounts.Single().StampMatches).IsFalse();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        {
            HumanPrincipalSourceValidation.RequireCurrent(subject, authority, revoked);
            return Task.CompletedTask;
        });
        await context.UserExternalLogins.Where(row => row.Id == loginId).ExecuteDeleteAsync();
        await Assert.That((await source.ObserveAsync()).Bindings.Length).IsEqualTo(0);
    }

    [Test]
    public async Task OrdinaryActorFactsMatchOriginalListsWithoutInventingAdministratorAuthority()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        await context.PlatformUserRoles.Where(row => row.UserId == fixture.UserId).ExecuteDeleteAsync();
        var subject = await SubjectAsync(fixture);
        var source = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var prepared = await source.ObserveAsync();
        await Assert.That(prepared.Users.Single().Id).IsEqualTo(fixture.UserId);
        await Assert.That(prepared.PlatformGrants.Length).IsEqualTo(0);
        var organizations = new OrganizationMemberRepository(context);
        var groups = new GroupMemberRepository(context);
        await Assert.That(prepared.EventCreateOrganizations.SequenceEqual(
            await organizations.GetOrganizationIdsWhereUserHasPermission(fixture.UserId, PermissionCodes.EventCreate))).IsTrue();
        await Assert.That(prepared.EventFinanceOrganizations.SequenceEqual(
            await organizations.GetOrganizationIdsWhereUserHasPermission(fixture.UserId, PermissionCodes.EventManageFinance))).IsTrue();
        await Assert.That(prepared.EventCreateGroups.SequenceEqual(
            await groups.GetGroupIdsWhereUserHasPermission(fixture.UserId, PermissionCodes.EventCreate))).IsTrue();
        await Assert.That(prepared.EventFinanceGroups.SequenceEqual(
            await groups.GetGroupIdsWhereUserHasPermission(fixture.UserId, PermissionCodes.EventManageFinance))).IsTrue();
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await source.AcquireAsync(await sets.AcquireAsync());
        await Assert.That(lease.Facts.Users.Single()).IsEqualTo(prepared.Users.Single());
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ObserveAsync());
        await lease.ValidateAsync();
        await context.Users.Where(user => user.Id == fixture.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.IsDeleted, true));
        var finalFacts = await lease.ValidateAsync();
        await Assert.That(finalFacts.Users.Single().IsDeleted).IsTrue();
        await Assert.That(lease.Facts.Users.Single().IsDeleted).IsFalse();
        await transaction.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
        await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
        await replacement.RollbackAsync();
    }

    [Test]
    public async Task HeldSqlitePrincipalExcludesMutationAndObservesCommittedDeletionNextAttempt()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var source = new HumanPrincipalSourceFence(context, await SubjectAsync(fixture),
            PolicySourceSetPurpose.RuntimeTenant);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await source.AcquireAsync(await sets.AcquireAsync());
        await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.DatabasePath,
            Pooling = false,
            DefaultTimeout = 1
        }.ToString());
        await writer.OpenAsync();
        await Assert.That(SQLitePCL.raw.sqlite3_exec(writer.Handle, "BEGIN IMMEDIATE"))
            .IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
        await lease.ValidateAsync();
        await transaction.RollbackAsync();
        await context.Users.Where(row => row.Id == fixture.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        var next = await source.ObserveAsync();
        await Assert.That(next.Users.Single().IsDeleted).IsTrue();
    }

    private static async Task<HumanCommitSubject> SubjectAsync(EventVisitorCapabilitySqliteFixture fixture) =>
        await HumanCommitSubject.ResolveAsync(
            fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None) ?? throw new InvalidOperationException("The fixture requires a resolved human.");
}
