using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionTemplates.Handlers.Commands;
using Explore.Application.Features.EventTemplates.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectTemplateDeletionRejectsNonAdministrator(bool session)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var target = await SeedDeletionTemplate(db, session);

        await Assert.ThrowsAsync<AuthorizationException>(() => DeleteTemplate(scope.ServiceProvider, session, target));

        await Assert.That(await DeletionTemplateExists(db, session, target)).IsTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CurrentAdministratorCanDeleteExactTemplate(bool session, bool instance)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var target = await SeedDeletionTemplate(db, session);
        if (instance)
        {
            db.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = data.OutsiderId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
        }
        else
        {
            var member = await db.TenantUsers.SingleAsync(row => row.UserId == data.OutsiderId);
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        await Assert.That(await DeleteTemplate(scope.ServiceProvider, session, target)).IsTrue();

        await Assert.That(await DeletionTemplateExists(db, session, target)).IsFalse();
    }

    [Test]
    public async Task DeletedSessionTemplateParentCannotBeReplacedByTrackedState()
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var target = await SeedDeletionTemplate(db, session: true);
        var child = await db.EventSessionTemplates.Include(row => row.EventTemplate)
            .SingleAsync(row => row.Id == target);
        var parent = child.EventTemplate!;
        parent.IsDeleted = true;
        db.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = data.OutsiderId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        parent.IsDeleted = false;

        await Assert.ThrowsAsync<AuthorizationException>(() => DeleteTemplate(scope.ServiceProvider, true, target));

        await Assert.That(await DeletionTemplateExists(db, true, target)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedAdministratorRevocationPreventsTemplateDeletion(bool session)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var target = await SeedDeletionTemplate(db, session);
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = data.OutsiderId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        };
        db.PlatformUserRoles.Add(grant);
        await db.SaveChangesAsync();
        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.PlatformUserRoles.Remove(
                await revocation.PlatformUserRoles.SingleAsync(row => row.Id == grant.Id, token));
            await revocation.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            DeleteTemplate(scope.ServiceProvider, session, target, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await DeletionTemplateExists(db, session, target)).IsTrue();
    }

    private static async Task<bool> DeleteTemplate(
        IServiceProvider services, bool session, Guid id, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await (session
            ? ActivatorUtilities.CreateInstance<DeleteEventSessionTemplateCommandHandler>(services)
                .ExecuteAsync(new() { Id = id }, token)
            : ActivatorUtilities.CreateInstance<DeleteEventTemplateCommandHandler>(services)
                .ExecuteAsync(new() { Id = id }, token));
    }

    private static Task<bool> DeletionTemplateExists(ExploreDbContext db, bool session, Guid id) =>
        session ? db.EventSessionTemplates.AsNoTracking().AnyAsync(row => row.Id == id)
            : db.EventTemplates.AsNoTracking().AnyAsync(row => row.Id == id);

    private static async Task<Guid> SeedDeletionTemplate(ExploreDbContext db, bool session)
    {
        var template = new EventTemplate
        {
            Id = Guid.CreateVersion7(),
            TenantId = db.TenantContext!.TenantId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            TemplateKey = $"deletion-{Guid.CreateVersion7():N}",
            DisplayName = "Deletion authority",
            Version = 1,
            IsActive = true
        };
        db.EventTemplates.Add(template);
        await db.SaveChangesAsync();
        if (!session)
            return template.Id;
        var child = new EventSessionTemplate
        {
            Id = Guid.CreateVersion7(),
            TenantId = template.TenantId,
            EventTemplateId = template.Id,
            ConcurrencyStamp = Guid.CreateVersion7(),
            SessionTemplateKey = $"deletion-{Guid.CreateVersion7():N}",
            DisplayName = "Session deletion authority",
            Version = 1,
            IsActive = true
        };
        db.EventSessionTemplates.Add(child);
        await db.SaveChangesAsync();
        return child.Id;
    }
}
