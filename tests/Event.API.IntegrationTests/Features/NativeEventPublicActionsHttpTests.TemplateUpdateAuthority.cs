using Explore.Application.Contracts.Persistence;
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
    public async Task DirectTemplateUpdateRejectsNonAdministrator(bool session)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var id = await SeedDeletionTemplate(db, session);
        var before = await TemplateUpdateState(db, session, id);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            UpdateTemplate(scope.ServiceProvider, session, id, before.Stamp));

        await Assert.That(await TemplateUpdateState(db, session, id)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentAdministratorUpdatesPersistedTemplateDespitePostedTenantHint(bool session)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var id = await SeedDeletionTemplate(db, session);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var before = await TemplateUpdateState(db, session, id);

        var result = await UpdateTemplate(scope.ServiceProvider, session, id, before.Stamp);

        await Assert.That(result.IsSuccess).IsTrue();
        var after = await TemplateUpdateState(db, session, id);
        await Assert.That(after.Name).IsEqualTo("Authorized template update");
        await Assert.That(after.Description).IsEqualTo(before.Description);
        await Assert.That(after.Stamp).IsNotEqualTo(before.Stamp);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TrackedTemplateCannotSupplyUnpatchedPersistedFields(bool session)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var id = await SeedDeletionTemplate(db, session);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var before = await TemplateUpdateState(db, session, id);
        if (session)
            (await db.EventSessionTemplates.SingleAsync(row => row.Id == id)).Description = "Unsubmitted description";
        else
            (await db.EventTemplates.SingleAsync(row => row.Id == id)).Description = "Unsubmitted description";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateTemplate(scope.ServiceProvider, session, id, before.Stamp));

        await Assert.That(await TemplateUpdateState(db, session, id)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedAdministratorRevocationPreventsTemplateUpdate(bool session)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var id = await SeedDeletionTemplate(db, session);
        var grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        var before = await TemplateUpdateState(db, session, id);
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
            UpdateTemplate(scope.ServiceProvider, session, id, before.Stamp, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await TemplateUpdateState(db, session, id)).IsEqualTo(before);
    }

    [Test]
    public async Task ParentDeletionAfterProviderDecisionPreventsSessionTemplateUpdate()
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var id = await SeedDeletionTemplate(db, session: true);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var before = await TemplateUpdateState(db, true, id);
        var parentId = await db.EventSessionTemplates.AsNoTracking()
            .Where(row => row.Id == id).Select(row => row.EventTemplateId).SingleAsync();
        Guid tenantId = db.TenantContext!.TenantId;
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
            UpdateTemplate(scope.ServiceProvider, true, id, before.Stamp, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await TemplateUpdateState(db, true, id)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TemplateMutationReadRequiresItsNativeTransaction(bool session)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var id = await SeedDeletionTemplate(db, session);

        if (session)
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<IEventSessionTemplateRepository>()
                .GetTrackedSessionTemplateWithDefinitions(id, default));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<IEventTemplateRepository>()
                .GetTrackedTemplateWithDefinitions(id, default));
    }

    private static async Task<BaseCommandResponse<Guid>> UpdateTemplate(
        IServiceProvider services, bool session, Guid id, Guid stamp, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await (session
            ? ActivatorUtilities.CreateInstance<UpdateEventSessionTemplateCommandHandler>(services).ExecuteAsync(new()
            {
                SessionTemplateId = id,
                ExpectedConcurrencyStamp = stamp,
                TenantId = Guid.CreateVersion7(),
                SessionTemplateDto = new() { Metadata = new() { DisplayName = "Authorized template update" } }
            }, token)
            : ActivatorUtilities.CreateInstance<UpdateEventTemplateCommandHandler>(services).ExecuteAsync(new()
            {
                TemplateId = id,
                ExpectedConcurrencyStamp = stamp,
                TenantId = Guid.CreateVersion7(),
                TemplateDto = new() { Metadata = new() { DisplayName = "Authorized template update" } }
            }, token));
    }

    private static async Task<Guid> GrantTemplateAdministrator(ExploreDbContext db, Guid userId)
    {
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        };
        db.PlatformUserRoles.Add(grant);
        await db.SaveChangesAsync();
        return grant.Id;
    }

    private static async Task<(string Name, string? Description, Guid Stamp)> TemplateUpdateState(
        ExploreDbContext db, bool session, Guid id)
    {
        if (session)
        {
            var value = await db.EventSessionTemplates.AsNoTracking().SingleAsync(row => row.Id == id);
            return (value.DisplayName, value.Description, value.ConcurrencyStamp);
        }
        var template = await db.EventTemplates.AsNoTracking().SingleAsync(row => row.Id == id);
        return (template.DisplayName, template.Description, template.ConcurrencyStamp);
    }
}
