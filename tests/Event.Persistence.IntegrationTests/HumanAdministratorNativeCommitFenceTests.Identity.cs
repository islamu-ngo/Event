using System.Data;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class HumanAdministratorNativeCommitFenceTests
{
    [Test]
    public async Task AuthenticatedProviderBindingIsResolvedThenFencedAndCannotBeSubstituted()
    {
        var rows = await SeedAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", rows.User.ToString("D")), new Claim("auth_provider", "local"),
                new Claim("iss", LocalIdentityOptions.Issuer)], "fixture"));
        var account = principal.GetProviderIdentity()!.AccountKey;
        Guid binding = Guid.CreateVersion7();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = binding,
                UserId = rows.User,
                User = null!,
                AuthenticationProviderId = (int)account.ProviderKind,
                AuthenticationProvider = null!,
                ProviderKey = account.Value,
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        var resolver = new ResolveCurrentUserIdByIdentityRequestHandler(
            new ExternalLoginAuthenticationRepository(
                new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global))));
        var subject = await HumanCommitSubject.ResolveAsync(principal, resolver, CancellationToken.None);
        await Assert.That(subject!.PrincipalId).IsEqualTo(rows.User);
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
        var scope = new HumanAdministratorCommitScope(context, subject,
            HumanAdministratorCommitPurpose.EmailInstance, fixture.NativeQueueEnrollment);
        var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.That(await CheckAsync(authority, rows, false)).IsTrue();
            await using var contender = fixture.CreateDbContext();
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var conflict = await Assert.ThrowsAsync<PostgresException>(async () =>
                await contender.Database.ExecuteSqlRawAsync(
                    $"SELECT id FROM {Table(contender, typeof(UserExternalLogin))} WHERE id = {{0}} FOR UPDATE NOWAIT", binding));
            await Assert.That(conflict!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
            await owner.RollbackAsync();
        }
        await using (var revoke = fixture.CreateDbContext())
            await revoke.UserExternalLogins.Where(value => value.Id == binding).ExecuteDeleteAsync();
        await using var retry = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await CheckAsync(authority, rows, false)).IsFalse();
        await retry.RollbackAsync();
        // Resolving again must not fall back to the still-present GUID claim.
        await Assert.That(await HumanCommitSubject.ResolveAsync(principal, resolver, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task MissingNativeRoutineFailsClosedInsteadOfReadingIamTables()
    {
        var rows = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string function = PostgresHumanAdministratorCommitAuthorityContract.InstanceFunctionSql(migration);
        string renamed = $"human_administrator_missing_{Guid.CreateVersion7():N}";
        await migration.Database.ExecuteSqlRawAsync(
            $"ALTER FUNCTION {function}(uuid,integer,text,integer,uuid) RENAME TO \"{renamed}\"");
        try
        {
            await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
            var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.EmailInstance);
            var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
            await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<PostgresException>(() => CheckAsync(authority, rows, false));
            await owner.RollbackAsync();
            await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        }
        finally
        {
            string schema = function[..function.LastIndexOf('.')];
            await migration.Database.ExecuteSqlRawAsync(
                $"ALTER FUNCTION {schema}.\"{renamed}\"(uuid,integer,text,integer,uuid) RENAME TO human_administrator_instance_fence");
        }
    }
}
