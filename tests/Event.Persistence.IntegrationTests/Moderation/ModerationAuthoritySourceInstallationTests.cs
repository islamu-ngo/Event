using System.Data.Common;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ModerationAuthoritySourceInstallationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task Identical_installation_preserves_the_advanced_native_epoch()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var handler = Handler(context, seed.UserId);
        var first = await handler.ExecuteAsync(seed.Command);
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(first.Id).IsEqualTo(seed.Command.HostBindingId);
        await Assert.That(first.EpochId).IsEqualTo(seed.Command.EpochId);
        var epoch = await context.ModerationQualificationEpochs.SingleAsync();
        epoch.Advance(1);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repeat = await handler.ExecuteAsync(seed.Command);
        await Assert.That(repeat.IsSuccess).IsTrue();
        await Assert.That(repeat.EpochId).IsEqualTo(first.EpochId);
        await Assert.That(repeat.Revision).IsEqualTo(2L);
        await Assert.That(await context.ModerationHostBindings.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationQualificationEpochs.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task User_owned_machine_key_cannot_inherit_its_owners_instance_installation_authority()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var result = await Handler(context, seed.UserId, machineCaller: true).ExecuteAsync(seed.Command);
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await AssertEmptyAsync(context);
    }

    [Test]
    public async Task Claimed_admin_and_tenant_only_grants_cannot_establish_a_host_root()
    {
        var seed = await SeedAsync(admin: false);
        await using (var writer = fixture.CreateDbContext())
        {
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = seed.Command.TenantId,
                Tenant = null!,
                UserId = seed.UserId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active
            };
            writer.AddRange(membership, new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = membership.TenantId,
                Tenant = null!,
                TenantUserId = membership.Id,
                TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                Role = null!
            });
            await writer.SaveChangesAsync();
        }
        await using var context = BoundContext(seed.Command.TenantId);
        var result = await Handler(context, seed.UserId).ExecuteAsync(seed.Command);
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await AssertEmptyAsync(context);
    }

    [Test]
    public async Task Revoked_native_admin_assignment_denies_an_identical_repeat()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var handler = Handler(context, seed.UserId);
        await Assert.That((await handler.ExecuteAsync(seed.Command)).IsSuccess).IsTrue();
        await using (var writer = fixture.CreateDbContext())
        {
            await writer.PlatformUserRoles.Where(row => row.Id == seed.GrantId).ExecuteDeleteAsync();
        }
        var repeat = await handler.ExecuteAsync(seed.Command);
        await Assert.That(repeat.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Unauthenticated_identity_cannot_install_even_with_real_native_admin_rows()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var result = await Handler(context, seed.UserId, authenticated: false).ExecuteAsync(seed.Command);
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AuthenticationRequired);
        await AssertEmptyAsync(context);
    }

    [Test]
    public async Task Exact_provider_login_binding_is_required_without_claim_identity_fallback()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var handler = Handler(context, seed.UserId, provider: true);
        await Assert.That((await handler.ExecuteAsync(seed.Command)).IsSuccess).IsTrue();
        await using (var writer = fixture.CreateDbContext())
        {
            await writer.UserExternalLogins.Where(row => row.Id == seed.LoginId).ExecuteDeleteAsync();
        }
        var repeat = await handler.ExecuteAsync(seed.Command);
        await Assert.That(repeat.FailureCode).IsEqualTo(FailureCodes.AuthenticationRequired);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Ambient_bypass_and_foreign_provider_tenant_fail_closed()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        context.EnableTenantFilterBypass("Installation adversarial boundary witness.");
        await Assert.That((await Handler(context, seed.UserId).ExecuteAsync(seed.Command)).FailureCode)
            .IsEqualTo("ModerationAuthorityUnavailable");
        context.ClearTenantFilterBypass();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)", seed.OtherTenantId.ToString());
        await Assert.That((await Handler(context, seed.UserId).ExecuteAsync(seed.Command)).FailureCode)
            .IsEqualTo("ModerationAuthorityUnavailable");
        await AssertEmptyAsync(context);
    }

    [Test]
    public async Task Deleted_selected_principal_does_not_fall_back_to_another_service()
    {
        var seed = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            await writer.ServicePrincipals.Where(row => row.Id == seed.Command.MaintenanceServicePrincipalId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            writer.Add(new ServicePrincipal
            {
                Id = Guid.CreateVersion7(),
                Code = "other-maintenance",
                DisplayName = "Other maintenance"
            });
            await writer.SaveChangesAsync();
        }
        await using var context = BoundContext(seed.Command.TenantId);
        var result = await Handler(context, seed.UserId).ExecuteAsync(seed.Command);
        await Assert.That(result.FailureCode).IsEqualTo("ModerationAuthorityUnavailable");
        await AssertEmptyAsync(context);
    }

    [Test]
    public async Task Missing_selected_principal_does_not_create_bootstrap_identity()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var result = await Handler(context, seed.UserId).ExecuteAsync(seed.Command with
        {
            MaintenanceServicePrincipalId = Guid.CreateVersion7()
        });
        await Assert.That(result.FailureCode).IsEqualTo("ModerationAuthorityUnavailable");
        await AssertEmptyAsync(context);
    }

    [Test]
    public async Task Missing_and_foreign_tenants_cannot_receive_qualification()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var foreign = await Handler(context, seed.UserId).ExecuteAsync(seed.Command with { TenantId = seed.OtherTenantId });
        await Assert.That(foreign.FailureCode).IsEqualTo("ModerationAuthorityUnavailable");
        await AssertEmptyAsync(context);
        var missingId = Guid.CreateVersion7();
        await using var missingContext = BoundContext(missingId);
        var missing = await Handler(missingContext, seed.UserId).ExecuteAsync(seed.Command with { TenantId = missingId });
        await Assert.That(missing.FailureCode).IsEqualTo("ModerationAuthorityUnavailable");
        await AssertEmptyAsync(missingContext);
    }

    [Test]
    public async Task Inactive_native_tenant_cannot_establish_qualification()
    {
        var seed = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            var inactive = await writer.TenantStatuses.FirstAsync(status => !status.IsActiveState);
            await writer.Tenants.Where(row => row.Id == seed.Command.TenantId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantStatusId, inactive.Id));
        }
        await using var context = BoundContext(seed.Command.TenantId);
        var result = await Handler(context, seed.UserId).ExecuteAsync(seed.Command);
        await Assert.That(result.FailureCode).IsEqualTo("ModerationAuthorityUnavailable");
        await AssertEmptyAsync(context);
    }

    [Test]
    public async Task Conflicting_history_and_source_id_reuse_leave_both_sources_unchanged()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var handler = Handler(context, seed.UserId);
        await Assert.That((await handler.ExecuteAsync(seed.Command)).IsSuccess).IsTrue();
        foreach (var conflict in new[]
        {
            seed.Command with { TrustDomainId = Guid.CreateVersion7() },
            seed.Command with { SolutionId = Guid.CreateVersion7() },
            seed.Command with { InstanceId = Guid.CreateVersion7() },
            seed.Command with { HostBindingId = Guid.CreateVersion7() },
            seed.Command with { EpochId = Guid.CreateVersion7() },
            seed.Command with { MaintenanceServicePrincipalId = seed.OtherPrincipalId }
        })
        {
            await Assert.That((await handler.ExecuteAsync(conflict)).FailureCode)
                .IsEqualTo(FailureCodes.ConcurrencyConflict);
        }
        await using var other = BoundContext(seed.OtherTenantId);
        var reused = await Handler(other, seed.UserId).ExecuteAsync(seed.Command with { TenantId = seed.OtherTenantId });
        await Assert.That(reused.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(await other.ModerationQualificationEpochs.CountAsync()).IsEqualTo(0);
        var binding = await context.ModerationHostBindings.SingleAsync();
        await Assert.That(binding.TrustDomainId).IsEqualTo(seed.Command.TrustDomainId);
        await Assert.That(binding.MaintenanceServicePrincipalId).IsEqualTo(seed.Command.MaintenanceServicePrincipalId);
        var epoch = await context.ModerationQualificationEpochs.SingleAsync();
        await Assert.That(epoch.Id).IsEqualTo(seed.Command.EpochId);
        await Assert.That(epoch.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Each_actual_tenant_gets_its_own_epoch_under_the_same_host()
    {
        var seed = await SeedAsync();
        await using var first = BoundContext(seed.Command.TenantId);
        await Assert.That((await Handler(first, seed.UserId).ExecuteAsync(seed.Command)).IsSuccess).IsTrue();
        var otherCommand = seed.Command with { TenantId = seed.OtherTenantId, EpochId = Guid.CreateVersion7() };
        await using var other = BoundContext(seed.OtherTenantId);
        var result = await Handler(other, seed.UserId).ExecuteAsync(otherCommand);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(seed.Command.HostBindingId);
        await Assert.That(result.EpochId).IsEqualTo(otherCommand.EpochId);
        await Assert.That(result.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task Identity_permission_and_native_sources_remain_fenced_until_commit()
    {
        var seed = await SeedAsync();
        await using (var first = BoundContext(seed.Command.TenantId))
        {
            await Assert.That((await Handler(first, seed.UserId).ExecuteAsync(seed.Command)).IsSuccess).IsTrue();
        }
        var gate = new CommitGate();
        await using var owner = BoundContext(seed.Command.TenantId, gate);
        var operation = Handler(owner, seed.UserId).ExecuteAsync(seed.Command);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertLockedAsync<User>(seed.Command.TenantId, seed.UserId);
            await AssertLockedAsync<UserExternalLogin>(seed.Command.TenantId, seed.LoginId);
            await AssertLockedAsync<PlatformUserRole>(seed.Command.TenantId, seed.GrantId);
            await AssertLockedAsync<Role>(seed.Command.TenantId, seed.RoleId);
            await AssertLockedAsync<ServicePrincipal>(seed.Command.TenantId, seed.Command.MaintenanceServicePrincipalId);
            await AssertLockedAsync<Tenant>(seed.Command.TenantId, seed.Command.TenantId);
            await AssertLockedAsync<TenantStatus>(seed.Command.TenantId, seed.TenantStatusId);
            await AssertLockedAsync<ModerationHostBinding>(seed.Command.TenantId, seed.Command.HostBindingId);
            await AssertLockedAsync<ModerationQualificationEpoch>(seed.Command.TenantId, seed.Command.EpochId);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        await Assert.That((await operation).IsSuccess).IsTrue();
    }

    [Test]
    public async Task Lost_commit_acknowledgement_reconciles_exact_established_sources()
    {
        var seed = await SeedAsync();
        var lost = new LostAcknowledgement();
        await using var context = BoundContext(seed.Command.TenantId, lost);
        var result = await Handler(context, seed.UserId).ExecuteAsync(seed.Command);
        await Assert.That(lost.Injected).IsTrue();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(seed.Command.HostBindingId);
        await Assert.That(result.EpochId).IsEqualTo(seed.Command.EpochId);
        await Assert.That(result.Revision).IsEqualTo(1L);
        await Assert.That(await context.ModerationHostBindings.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationQualificationEpochs.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Invalid_preallocated_identity_fails_validation_without_installing()
    {
        var seed = await SeedAsync();
        await using var context = BoundContext(seed.Command.TenantId);
        var result = await Handler(context, seed.UserId).ExecuteAsync(seed.Command with { EpochId = Guid.NewGuid() });
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors).IsNotNull();
        await AssertEmptyAsync(context);
    }

    private ExploreDbContext BoundContext(Guid tenantId, params IInterceptor[] interceptors)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        return fixture.CreateTenantFilteredDbContext(tenant,
            interceptors.Append(PostgresTenantSessionInterceptor.Instance).ToArray());
    }

    private static EstablishModerationAuthoritySourcesCommandHandler Handler(
        ExploreDbContext context, Guid userId, bool authenticated = true, bool provider = false,
        bool machineCaller = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, "platform.admin")
        };
        if (provider)
        {
            claims.Add(new Claim("iss", "https://installation.example.invalid/realm"));
            claims.Add(new Claim("auth_provider", "keycloak"));
        }
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "installation-test" : null))
            }
        };
        var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        if (machineCaller)
            machine.SetPrincipal(new ApiKeyPrincipalContext(
                Guid.CreateVersion7().ToString(), context.TenantFilterTenantId,
                ExternalApiKeyOwnerType.User, userId, []));
        return new(new ModerationAuthoritySourceRepository(context, admin, machine));
    }

    private async Task<Seed> SeedAsync(bool admin = true)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var userId = Guid.CreateVersion7();
        var user = new User
        {
            Id = userId,
            Pii = new UserPii
            {
                UserId = userId,
                Email = $"{userId:N}@example.invalid",
                FirstName = "Installation",
                LastName = "Operator"
            }
        };
        var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Installation tenant",
            Slug = $"installation-{Guid.CreateVersion7():N}",
            TenantStatusId = status.Id,
            TenantStatus = null!
        };
        var otherTenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Other actual tenant",
            Slug = $"installation-{Guid.CreateVersion7():N}",
            TenantStatusId = status.Id,
            TenantStatus = null!
        };
        var principal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = "selected-maintenance",
            DisplayName = "Selected maintenance"
        };
        var otherPrincipal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = "other-maintenance",
            DisplayName = "Other maintenance"
        };
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        var grantId = Guid.CreateVersion7();
        var loginId = Guid.CreateVersion7();
        var account = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://installation.example.invalid/realm", userId.ToString());
        context.AddRange(user, tenant, otherTenant, principal, otherPrincipal, new UserExternalLogin
        {
            Id = loginId,
            UserId = userId,
            User = user,
            AuthenticationProviderId = (int)account.ProviderKind,
            AuthenticationProvider = null!,
            ProviderKey = account.Value
        });
        if (admin)
        {
            context.Add(new PlatformUserRole
            {
                Id = grantId,
                UserId = userId,
                User = user,
                RoleId = role.Id,
                Role = role
            });
        }
        await context.SaveChangesAsync();
        return new(new EstablishModerationAuthoritySourcesCommand
        {
            HostBindingId = Guid.CreateVersion7(),
            TrustDomainId = Guid.CreateVersion7(),
            SolutionId = Guid.CreateVersion7(),
            InstanceId = Guid.CreateVersion7(),
            MaintenanceServicePrincipalId = principal.Id,
            TenantId = tenant.Id,
            EpochId = Guid.CreateVersion7()
        }, userId, grantId, role.Id, status.Id, otherTenant.Id, otherPrincipal.Id, loginId);
    }

    private static async Task AssertEmptyAsync(ExploreDbContext context)
    {
        await Assert.That(await context.ModerationHostBindings.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationQualificationEpochs.CountAsync()).IsEqualTo(0);
    }

    private async Task AssertLockedAsync<TEntity>(Guid tenantId, object id) where TEntity : class
    {
        await using var contender = BoundContext(tenantId);
        await using var transaction = await contender.Database.BeginTransactionAsync();
        var entity = contender.Model.FindEntityType(typeof(TEntity))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = contender.GetService<ISqlGenerationHelper>();
        var key = sql.DelimitIdentifier(entity.FindPrimaryKey()!.Properties.Single().GetColumnName(store)!);
        await using var command = contender.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = $"SELECT {key} FROM {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())} " +
            $"WHERE {key} = @key FOR UPDATE NOWAIT";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        var blocked = false;
        try
        {
            await command.ExecuteScalarAsync();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            blocked = true;
        }
        await Assert.That(blocked).IsTrue();
    }

    private sealed record Seed(EstablishModerationAuthoritySourcesCommand Command, Guid UserId,
        Guid GrantId, int RoleId, int TenantStatusId, Guid OtherTenantId, Guid OtherPrincipalId, Guid LoginId);

    private sealed class CommitGate : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private sealed class LostAcknowledgement : DbTransactionInterceptor
    {
        public bool Injected { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Injected)
            {
                Injected = true;
                throw new TimeoutException("Installation acknowledgement lost after the native commit.");
            }
            return Task.CompletedTask;
        }
    }
}
