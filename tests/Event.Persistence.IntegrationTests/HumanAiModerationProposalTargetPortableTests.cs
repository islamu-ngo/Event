using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class HumanAiModerationProposalTargetPortableTests
{
    [Test]
    [Arguments("current")]
    [Arguments("ordinary")]
    [Arguments("deleted")]
    public async Task DiscoveryRequiresPersistedAdministrationAndAnExactLiveCrossTenantTarget(string state)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var parent = await fixture.SeedEventAsync();
        Guid selected = await SeedSelectedTenantAsync(fixture);
        await using var authenticated = new CurrentHumanPrincipalFixture(fixture.UserId, selected, fixture.DatabasePath);
        await authenticated.InitializeAsync();
        if (state != "ordinary")
            await authenticated.GrantInstanceAdminAsync();
        if (state == "deleted")
            await fixture.Context.Events.Where(row => row.Id == parent.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        var context = authenticated.Context;
        var source = new HumanAiModerationProposalTargetSource(context, await SubjectAsync(authenticated), parent.Id);
        if (state == "ordinary")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ObserveAsync());
        else if (state == "deleted")
            await Assert.That(await source.ObserveAsync()).IsNull();
        else
        {
            var observed = await source.ObserveAsync();
            await Assert.That(observed!.TenantId).IsEqualTo(fixture.TenantId);
            await Assert.That(observed.Events.Single().Id).IsEqualTo(parent.Id);
            await Assert.That(await context.Events.AnyAsync(row => row.Id == parent.Id)).IsFalse();
        }
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(selected);
    }

    [Test]
    public async Task OriginalOwnerExcludesWritersAndRevalidatesItsPersistedAdministratorRevocation()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var parent = await fixture.SeedEventAsync();
        Guid selected = await SeedSelectedTenantAsync(fixture);
        await using var authenticated = new CurrentHumanPrincipalFixture(fixture.UserId, selected, fixture.DatabasePath);
        await authenticated.InitializeAsync();
        await authenticated.GrantInstanceAdminAsync();
        var context = authenticated.Context;
        var subject = await SubjectAsync(authenticated);
        var source = new HumanAiModerationProposalTargetSource(context, subject, parent.Id);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var exclusion = await sets.AcquireAsync();
        var held = await source.AcquireAsync(exclusion, await principal.AcquireAsync(exclusion));
        await Assert.That((await held.ValidateAsync()).Events.Single().Id).IsEqualTo(parent.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ObserveAsync());
        await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.DatabasePath,
            Pooling = false
        }.ToString());
        await writer.OpenAsync();
        await Assert.That(SQLitePCL.raw.sqlite3_exec(writer.Handle, "BEGIN IMMEDIATE")).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
        await context.PlatformUserRoles.Where(row => row.UserId == fixture.UserId
            && row.RoleId == (int)RoleEnum.Admin).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => held.ValidateAsync());
        await owner.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(selected);
    }

    private static async Task<Guid> SeedSelectedTenantAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        Guid id = Guid.CreateVersion7();
        fixture.Context.Tenants.Add(new Explore.Domain.Tenant
        {
            Id = id,
            FullName = "Selected conversation tenant",
            Slug = $"selected-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        });
        await fixture.Context.SaveChangesAsync();
        return id;
    }

    private static async Task<HumanCommitSubject> SubjectAsync(CurrentHumanPrincipalFixture authenticated) =>
        await HumanCommitSubject.ResolveAsync(
            authenticated.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User,
            authenticated.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(), default)
        ?? throw new InvalidOperationException("A signed persisted human is required.");
}
