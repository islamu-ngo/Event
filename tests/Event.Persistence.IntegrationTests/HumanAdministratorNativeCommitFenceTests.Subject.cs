using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class HumanAdministratorNativeCommitFenceTests
{
    [Test]
    public async Task OrdinarySubjectFenceUsesRestrictedLoginWithoutAdministratorOrIamProjection()
    {
        var rows = await SeedAsync();
        await using var seed = fixture.CreateDbContext();
        await seed.PlatformUserRoles.Where(row => row.UserId == rows.User).ExecuteDeleteAsync();
        await seed.TenantUserRoleGrants.Where(row => row.Id == rows.Grant).ExecuteDeleteAsync();
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
        var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.EmailInstance);
        IHumanSubjectCommitAuthority authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await authority.IsSubjectCurrentForCommitAsync(rows.User, default)).IsTrue();
        await Assert.That(await authority.IsSubjectCurrentForCommitAsync(Guid.CreateVersion7(), default)).IsFalse();
        await Assert.That(await ((IInstanceAdministratorCommitAuthority)new ExternalApiKeyIssuanceAuthority(
            context, new HttpContextAccessor(), scope)).IsInstanceAdministratorForCommitAsync(rows.User, default)).IsFalse();
        await using (var contender = fixture.CreateDbContext())
        {
            await using var conflicting = await contender.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await contender.Database.ExecuteSqlRawAsync(
                    $"SELECT id FROM {Table(contender, typeof(Explore.Domain.User))} WHERE id = {{0}} FOR UPDATE NOWAIT",
                    rows.User));
            await Assert.That(error!.SqlState).IsEqualTo("55P03");
            await conflicting.RollbackAsync();
        }
        await owner.RollbackAsync();
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Users.AsNoTracking().AnyAsync());
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }
}
