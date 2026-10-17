using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionTemplates.Handlers.Commands;
using Explore.Application.Features.EventTemplates.Handlers.Commands;
using Explore.Application.Responses;
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
    public async Task DirectTemplateCreationRejectsNonAdministrator(bool session)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = session ? await SeedDeletionTemplate(db, false) : Guid.Empty;
        string key = $"create-{Guid.CreateVersion7():N}";

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            CreateTemplate(scope.ServiceProvider, session, parentId, key));

        await Assert.That(await CreatedTemplateExists(db, session, key)).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CurrentAdministratorCreatesTemplateInExactTenant(bool session, bool instance)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = session ? await SeedDeletionTemplate(db, false) : Guid.Empty;
        if (instance)
            await GrantTemplateAdministrator(db, data.OutsiderId);
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
            await db.SaveChangesAsync();
        }
        string key = $"create-{Guid.CreateVersion7():N}";

        var response = await CreateTemplate(scope.ServiceProvider, session, parentId, key);

        await Assert.That(response.IsSuccess).IsTrue();
        if (session)
        {
            var created = await db.EventSessionTemplates.AsNoTracking().SingleAsync(row => row.Id == response.Id);
            await Assert.That(created.TenantId).IsEqualTo(db.TenantContext!.TenantId);
            await Assert.That(created.EventTemplateId).IsEqualTo(parentId);
        }
        else
        {
            var created = await db.EventTemplates.AsNoTracking().SingleAsync(row => row.Id == response.Id);
            await Assert.That(created.TenantId).IsEqualTo(db.TenantContext!.TenantId);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedAdministratorRevocationPreventsTemplateCreation(bool session)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = session ? await SeedDeletionTemplate(db, false) : Guid.Empty;
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        string key = $"create-{Guid.CreateVersion7():N}";
        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.PlatformUserRoles.Remove(
                await revocation.PlatformUserRoles.SingleAsync(row => row.Id == grantId, token));
            await revocation.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateTemplate(scope.ServiceProvider, session, parentId, key, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await CreatedTemplateExists(db, session, key)).IsFalse();
    }

    [Test]
    public async Task ParentDeletionAfterProviderDecisionPreventsSessionTemplateCreation()
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = await SeedDeletionTemplate(db, false);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        Guid tenantId = db.TenantContext!.TenantId;
        string key = $"create-{Guid.CreateVersion7():N}";
        boundary.Arm(db, async token =>
        {
            await using var deletion = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            deletion.TenantContext = new AuthorityTenant(tenantId);
            (await deletion.EventTemplates.SingleAsync(row => row.Id == parentId, token)).IsDeleted = true;
            await deletion.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            CreateTemplate(scope.ServiceProvider, true, parentId, key, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await CreatedTemplateExists(db, true, key)).IsFalse();
    }

    [Test]
    public async Task InstanceAdministratorCannotCreateSessionTemplateUnderForeignParent()
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        Guid parentId;
        await using (var foreign = await scope.ServiceProvider
                         .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync())
        {
            foreign.TenantContext = new AuthorityTenant(data.ForeignTenantId);
            parentId = await SeedDeletionTemplate(foreign, false);
        }
        string key = $"create-{Guid.CreateVersion7():N}";

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            CreateTemplate(scope.ServiceProvider, true, parentId, key));

        await Assert.That(await CreatedTemplateExists(db, true, key)).IsFalse();
    }

    private static async Task<BaseCommandResponse<Guid>> CreateTemplate(
        IServiceProvider services, bool session, Guid parentId, string key, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await (session
            ? ActivatorUtilities.CreateInstance<CreateEventSessionTemplateCommandHandler>(services).ExecuteAsync(new()
            {
                SessionTemplateDto = new() { EventTemplateId = parentId, SessionTemplateKey = key, DisplayName = "Created with current authority" }
            }, token)
            : ActivatorUtilities.CreateInstance<CreateEventTemplateCommandHandler>(services).ExecuteAsync(new()
            {
                TemplateDto = new() { TemplateKey = key, DisplayName = "Created with current authority" }
            }, token));
    }

    private static Task<bool> CreatedTemplateExists(ExploreDbContext db, bool session, string key) =>
        session ? db.EventSessionTemplates.AsNoTracking().AnyAsync(row => row.SessionTemplateKey == key)
            : db.EventTemplates.AsNoTracking().AnyAsync(row => row.TemplateKey == key);
}
