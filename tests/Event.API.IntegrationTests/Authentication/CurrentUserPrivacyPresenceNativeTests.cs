using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>PR 79: ordinary profile reads retain erasure suppression without reading private saga receipts.</summary>
[NotInParallel]
public sealed class CurrentUserPrivacyPresenceNativeTests
{
    [Test]
    public async Task RuntimeProfileReadWithdrawsCachedUserAfterErasureWithoutSagaReceiptAccess()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var postgres = new PostgreSqlContainerFixture();
        await postgres.InitializeAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var setup = new ExploreDbContext(options.Options);
        setup.BindTenantLease();
        await setup.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await PostgresTenantCreationPermissions.ApplyAsync(
                setup, postgres.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
            await PostgresProvisioningPolicyPermissions.ApplyAsync(setup, postgres.NativeQueueEnrollment, token);
            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
                setup, postgres.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        });
        Guid tenantId = Guid.CreateVersion7();
        setup.Tenants.Add(new Tenant
        {
            Id = tenantId,
            FullName = "Private profile presence",
            Slug = $"profile-presence-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await setup.SaveChangesAsync(token);
        await using var fixture = await TenantBrandingNativeAuthenticationFixture
            .CreateForRuntimeReadsAsync(postgres, tenantId, TimeProvider.System);
        Guid userId = fixture.Subject.PrincipalId;

        await fixture.InOriginalRequestAsync(async () =>
        {
            ExploreDbContext owner = fixture.Owner;
            var lease = fixture.Services.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.RequireOriginalOwner(owner);
            await owner.Database.OpenConnectionAsync(token);
            var connection = owner.Database.GetDbConnection();
            var contextId = owner.ContextId;
            int backend = ((NpgsqlConnection)connection).ProcessID;
            string runtime = postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime).Username!;
            await Assert.That(await owner.Database.SqlQueryRaw<string>(
                "SELECT current_user::text || ':' || session_user::text || ':' || current_setting('role') AS \"Value\"")
                .SingleAsync(token)).IsEqualTo($"{runtime}:{runtime}:none");
            PostgresException? runtimeDenied = null;
            try
            {
                _ = await owner.Users.IgnoreAutoIncludes()
                    .AnyAsync(row => row.Id == userId, token);
            }
            catch (PostgresException exception)
            {
                runtimeDenied = exception;
            }
            await Assert.That(runtimeDenied).IsNotNull();
            await Assert.That(runtimeDenied!.SqlState).IsEqualTo("42501");
            var query = fixture.Services.GetRequiredService<IQueryHandler<GetUserRequest, UserDto?>>();

            UserDto? visible = await query.QueryAsync(new GetUserRequest(userId), token);
            await Assert.That(visible).IsNotNull();
            await Assert.That(visible!.Id).IsEqualTo(userId);

            DateTime now = DateTime.UtcNow;
            PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
                Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User, userId,
                PrivacyErasureReasonCode.AccountDeletion, 1, now, now);
            setup.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(
                intent, 1, SHA256.HashData(RandomNumberGenerator.GetBytes(32)),
                now.AddHours(1), now));
            await setup.SaveChangesAsync(token);

            UserDto? withheld = await query.QueryAsync(new GetUserRequest(userId), token);
            await Assert.That(withheld).IsNull();
            lease.RequireOriginalOwner(owner);
            await Assert.That(fixture.Owner).IsSameReferenceAs(owner);
            await Assert.That(owner.ContextId).IsEqualTo(contextId);
            await Assert.That(owner.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
            await Assert.That(owner.Database.CurrentTransaction).IsNull();

            var globalFactory = fixture.Services.GetRequiredService<IGlobalExploreDbContextFactory>();
            await using ExploreDbContext global = await globalFactory.CreateDbContextAsync(token);
            string globalLogin = postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Global).Username!;
            await Assert.That(await global.Database.SqlQueryRaw<string>(
                "SELECT current_user::text || ':' || session_user::text || ':' || current_setting('role') AS \"Value\"")
                .SingleAsync(token)).IsEqualTo($"{globalLogin}:{globalLogin}:none");
            await Assert.That(await global.PrivacyErasureSagas.IgnoreAutoIncludes()
                .AnyAsync(row => row.SubjectId == userId, token)).IsTrue();

            PostgresException? denied = null;
            try
            {
                _ = await global.PrivacyErasureSagas.IgnoreAutoIncludes().AsNoTracking()
                    .Where(row => row.SubjectId == userId)
                    .Select(row => row.ReceiptHash).SingleAsync(token);
            }
            catch (PostgresException exception)
            {
                denied = exception;
            }
            await Assert.That(denied).IsNotNull();
            await Assert.That(denied!.SqlState).IsEqualTo("42501");

            PostgresException? birthDateDenied = null;
            try
            {
                _ = await global.Set<UserPii>().IgnoreAutoIncludes().AsNoTracking()
                    .Where(row => row.UserId == userId)
                    .Select(row => EF.Property<DateOnly?>(row, "_dateOfBirth")).SingleAsync(token);
            }
            catch (PostgresException exception)
            {
                birthDateDenied = exception;
            }
            await Assert.That(birthDateDenied).IsNotNull();
            await Assert.That(birthDateDenied!.SqlState).IsEqualTo("42501");
            return true;
        });
    }

}
