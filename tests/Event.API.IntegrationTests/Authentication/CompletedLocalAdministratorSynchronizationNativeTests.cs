using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Completed Local administrators synchronize using the declared Local writer,
/// without attempting another bootstrap claim or gaining bootstrap storage privileges.
/// </summary>
[NotInParallel]
public sealed class CompletedLocalAdministratorSynchronizationNativeTests
{
    [Test]
    public Task CompletedAdministratorSynchronizesWithoutReclaimingBootstrap() =>
        SynchronizeAsync(BindingChange.None);

    [Test]
    public Task RemovedExternalLoginRejectsAuthenticatedAdministratorSynchronization() =>
        SynchronizeAsync(BindingChange.RemoveExternalLogin);

    [Test]
    public Task SuspendedPersonalActorRejectsAuthenticatedAdministratorSynchronization() =>
        SynchronizeAsync(BindingChange.SuspendPersonalActor);

    private static async Task SynchronizeAsync(BindingChange change)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var postgres = new PostgreSqlContainerFixture();
        await postgres.InitializeAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var setup = new ExploreDbContext(options.Options);
        setup.EnableTenantFilterBypass("Native regression arrangement and persisted authority observation under Migrator.");
        setup.BindTenantLease();
        Guid tenantId = Guid.CreateVersion7();
        setup.Tenants.Add(new Tenant
        {
            Id = tenantId,
            FullName = "Completed Local administrator",
            Slug = $"completed-local-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await setup.SaveChangesAsync();
        await using var fixture = await TenantBrandingNativeAuthenticationFixture
            .CreateForRuntimeReadsAsync(postgres, tenantId, TimeProvider.System);
        Guid userId = fixture.Subject.PrincipalId;
        await Assert.That(await setup.Set<PlatformUserRole>().AsNoTracking().AnyAsync(row =>
            row.Id == fixture.PlatformGrantId && row.UserId == userId
            && row.RoleId == (int)RoleEnum.Admin)).IsTrue();
        await Assert.That(await setup.Set<TenantUserRoleGrant>().AsNoTracking().AnyAsync(row =>
            row.Id == fixture.TenantGrantId && row.TenantId == tenantId
            && row.RoleId == (int)RoleEnum.TenantAdmin)).IsTrue();
        DateTime now = DateTime.UtcNow;
        const string fingerprint =
            "a734955af3916e01b02c3ab6e020bb98f334b91b7b280780ce821c139083c9a3";
        long generation = (await setup.InstanceBootstrapStates.AsNoTracking()
            .Select(row => (long?)row.Generation).MaxAsync() ?? 0) + 1;
        var bootstrap = InstanceBootstrapState.CreateConfiguredAdministratorPending(
            Guid.CreateVersion7(), AuthenticationProviderKind.Local, DeploymentMode.SingleTenant,
            generation, fingerprint, fingerprint, now);
        bootstrap.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Local, generation, fingerprint, userId, now);
        setup.InstanceBootstrapStates.Add(bootstrap);
        await setup.SaveChangesAsync();

        // Authentication has already succeeded. Change the actual binding, not the retained principal.
        if (change == BindingChange.RemoveExternalLogin)
        {
            var login = await setup.Set<UserExternalLogin>().SingleAsync(row =>
                row.UserId == userId
                && row.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                && row.ProviderKey == fixture.Subject.AccountKey.Value);
            setup.RemoveRange(await setup.Set<UserIdentityEmailEvidence>()
                .Where(row => row.ExternalLoginId == login.Id).ToListAsync(token));
            setup.Remove(login);
            await setup.SaveChangesAsync(token);
        }
        else if (change == BindingChange.SuspendPersonalActor)
        {
            var actor = await setup.Set<Actor>().SingleAsync(row =>
                row.UserId == userId && row.ActorTypeId == (int)ActorTypeEnum.User);
            actor.IsSuspended = true;
            actor.SuspendedAt = now;
            actor.SuspendedBy = userId;
            await setup.SaveChangesAsync();
        }

        string before = await ReadAuthorityStateAsync(setup);
        await fixture.InOriginalRequestAsync(async () =>
        {
            await using var selected = await fixture.Services
                .GetRequiredService<ILocalIdentityExecutionScopeFactory>()
                .CreateAsync(token);
            IServiceProvider services = selected.Services;
            var owner = services.GetRequiredService<ExploreDbContext>();
            var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(owner);
            await Assert.That(lease.Context).IsSameReferenceAs(owner);
            await Assert.That(owner.Database.CurrentTransaction).IsNull();
            var connection = owner.Database.GetDbConnection();
            var contextId = owner.ContextId;
            int backend = ((Npgsql.NpgsqlConnection)connection).ProcessID;
            string writer = postgres.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter).Username!;
            await Assert.That(await owner.Database.SqlQueryRaw<string>(
                "SELECT current_user::text AS \"Value\"").SingleAsync()).IsEqualTo(writer);
            await Assert.That(await owner.Database.SqlQueryRaw<string>(
                "SELECT session_user::text AS \"Value\"").SingleAsync()).IsEqualTo(writer);

            var manager = services.GetRequiredService<UserManager<LocalIdentityUser>>();
            var account = await manager.FindByIdAsync(userId.ToString("D"))
                ?? throw new InvalidOperationException("The authenticated Local account must still exist.");
            var command = new SyncUserCommand
            {
                AccountKey = fixture.Subject.AccountKey,
                UserDto = new UserDto
                {
                    Id = account.Id,
                    Email = account.Email!,
                    FirstName = account.FirstName,
                    LastName = account.LastName,
                    EmailVerified = account.EmailConfirmed,
                    AuthProvider = "local",
                    AuthProviderId = account.Id.ToString("D")
                }
            };

            var response = await services
                .GetRequiredService<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(command, token);

            await Assert.That(response.IsSuccess).IsEqualTo(change == BindingChange.None);
            if (change == BindingChange.None)
                await Assert.That(response.Id).IsEqualTo(userId);
            lease.RequireOriginalOwner(owner);
            await Assert.That(selected.Services.GetRequiredService<ExploreDbContext>())
                .IsSameReferenceAs(owner);
            await Assert.That(owner.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(owner.ContextId).IsEqualTo(contextId);
            await Assert.That(((Npgsql.NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
            await Assert.That(owner.Database.CurrentTransaction).IsNull();
            await Assert.That(fixture.Subject.PrincipalId).IsEqualTo(userId);
            await Assert.That(fixture.Subject.AccountKey).IsEqualTo(fixture.Lifetime.AccountKey);
            return true;
        });

        await Assert.That(await ReadAuthorityStateAsync(setup)).IsEqualTo(before);
        if (change == BindingChange.RemoveExternalLogin)
            await Assert.That(await setup.Set<UserExternalLogin>().AsNoTracking().AnyAsync(row =>
                row.UserId == userId
                && row.AuthenticationProviderId == (int)AuthenticationProviderKind.Local)).IsFalse();
        if (change == BindingChange.SuspendPersonalActor)
            await Assert.That(await setup.Set<Actor>().AsNoTracking().Where(row =>
                row.UserId == userId && row.ActorTypeId == (int)ActorTypeEnum.User)
                .Select(row => row.IsSuspended).SingleAsync()).IsTrue();
    }

    private static async Task<string> ReadAuthorityStateAsync(ExploreDbContext context)
    {
        var bootstrap = await context.InstanceBootstrapStates.AsNoTracking()
            .OrderBy(row => row.Id).ToListAsync();
        var platform = await context.Set<PlatformUserRole>().AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.UserId, row.RoleId, row.GrantedAt }).ToListAsync();
        var tenant = await context.Set<TenantUserRoleGrant>().AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.TenantId, row.TenantUserId, row.RoleId,
                row.RoleScopeId, row.GrantedAt }).ToListAsync();
        return JsonSerializer.Serialize(new { bootstrap, platform, tenant });
    }

    private enum BindingChange
    {
        None,
        RemoveExternalLogin,
        SuspendPersonalActor
    }
}
