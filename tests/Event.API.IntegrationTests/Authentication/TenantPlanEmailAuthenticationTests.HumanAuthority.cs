using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class TenantPlanEmailAuthenticationTests
{
    [Test]
    [Arguments("revoked")]
    [Arguments("unchanged")]
    [Arguments("unrelated")]
    public async Task OrdinaryBatch_AfterPreflight_FencesAuthorityBeforeEitherMemberWrite(string change)
    {
        var transaction = new BeforeOwnerTransaction();
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(transaction);
        Guid unrelatedActor;
        await using (var seed = fixture.CreateIndependentContext())
        {
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.UserId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            unrelatedActor = await SeedUnrelatedAdministratorAsync(seed);
        }

        var services = fixture.Services;
        var owner = fixture.Context;
        var preparation = services.GetRequiredService<IHumanCommitScopePreparation>();
        await preparation.PrepareAsync(CancellationToken.None);
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        await Assert.That(lease.HumanScope!.Purpose).IsEqualTo(HumanAdministratorCommitPurpose.RuntimeTenant);
        await Assert.That(lease.SelectedTenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(ReferenceEquals(lease.Context, owner)).IsTrue();
        await Assert.That(lease.HumanScope.Subject.PrincipalId).IsEqualTo(fixture.UserId);
        await Assert.That(lease.HumanLifetime!.AccountKey).IsEqualTo(fixture.Lifetime.AccountKey);
        await Assert.That(lease.HumanLifetime.ExpiresAt).IsEqualTo(fixture.Lifetime.ExpiresAt);
        await Assert.That(lease.HumanLifetime.LocalSession).IsEqualTo(fixture.Lifetime.LocalSession);

        bool observed = false;
        transaction.Owner = owner;
        transaction.BeforeStart = async token =>
            {
                observed = true;
                await Assert.That(owner.Database.CurrentTransaction).IsNull();
                if (change == "unchanged") return;
                await using var revoker = fixture.CreateIndependentContext();
                Guid revokedActor = change == "revoked" ? fixture.UserId : unrelatedActor;
                await revoker.PlatformUserRoles.Where(row => row.UserId == revokedActor).ExecuteDeleteAsync(token);
            };
        var notifications = new Notifications();
        // Only transaction observation and the external notification sink differ from production.
        // All authority, lifetime, preparation, repositories and writers use this RuntimeTenant lease.
        var handler = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)
                [new NotificationObserver(owner, notifications)]);
        var request = new UpdateSettingBatchCommand
        {
            Category = "AiAssistant",
            Scope = SettingScope.Instance,
            Values = new Dictionary<string, string>
            {
                [GovernanceSettingKeys.AiAssistant.Enabled] = "true",
                [GovernanceSettingKeys.AiAssistant.ModelId] = "committed-model"
            }
        };
        if (change == "revoked")
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(request, CancellationToken.None));
        else
            await Assert.That((await handler.ExecuteAsync(request, CancellationToken.None)).Success).IsTrue();

        await Assert.That(observed).IsTrue();
        await using var observer = fixture.CreateIndependentContext();
        var repository = SystemSettingRepository.CreateReadOnly(observer,
            new RelationalSettingMutationLock(observer, new EfCoreUnitOfWork(observer)));
        var enabled = await repository.GetByKey(GovernanceSettingKeys.AiAssistant.Enabled);
        var model = await repository.GetByKey(GovernanceSettingKeys.AiAssistant.ModelId);
        if (change == "revoked")
        {
            await Assert.That(enabled).IsNull();
            await Assert.That(model).IsNull();
            await Assert.That(notifications.Published).IsEmpty();
        }
        else
        {
            await Assert.That(enabled!.Value).IsEqualTo("true");
            await Assert.That(model!.Value).IsEqualTo("\"committed-model\"");
            await Assert.That(notifications.Published.Select(notification => notification.Key))
                .IsEquivalentTo([GovernanceSettingKeys.AiAssistant.Enabled, GovernanceSettingKeys.AiAssistant.ModelId]);
            await Assert.That(notifications.Published.All(notification => notification.ActorUserId == fixture.UserId)).IsTrue();
        }
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("unchanged")]
    [Arguments("unrelated")]
    public async Task ApplyingPlan_FencesPersistedHumanAuthorityBeforeAnySettingEffect(string change)
    {
        var notifications = new Notifications();
        await using var fixture = await CreateFixtureAsync(notifications);
        var scenario = await SeedPlanAsync(fixture, mixed: true);
        Guid unrelatedActor;
        await using (var seed = fixture.CreateIndependentContext())
            unrelatedActor = await SeedUnrelatedAdministratorAsync(seed);

        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            await Assert.That(await services.GetRequiredService<IAdminContext>()
                .IsInstanceAdminAsync(CancellationToken.None)).IsTrue();
            await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
            if (change != "unchanged")
            {
                await using var revoker = fixture.CreateIndependentContext();
                Guid revokedActor = change == "revoked" ? scenario.ActorId : unrelatedActor;
                await revoker.PlatformUserRoles.Where(row => row.UserId == revokedActor).ExecuteDeleteAsync();
            }
            var response = await PlanHandler(services).ExecuteAsync(Request(scenario), CancellationToken.None);
            await Assert.That(response.IsSuccess).IsEqualTo(change != "revoked");
            if (change == "revoked")
                await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        }, planAssignment: true);
        if (change == "revoked")
            await AssertUnchangedAsync(fixture, notifications, scenario);
        else
            await AssertAppliedAsync(fixture, notifications, scenario, mixed: true);
    }

    private static async Task<Guid> SeedUnrelatedAdministratorAsync(ExploreDbContext context)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"unrelated-plan-admin-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Unrelated",
                LastName = "Administrator"
            },
            CreatedAt = DateTime.UtcNow
        };
        var role = await context.Roles.SingleAsync(row => row.MasterCode == "platform.admin");
        await new PlatformUserRoleRepository(context).Create(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            RoleId = role.Id,
            Role = role,
            GrantedAt = DateTime.UtcNow
        });
        return user.Id;
    }

    private sealed class BeforeOwnerTransaction : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal Func<CancellationToken, Task>? BeforeStart { get; set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner) && BeforeStart is { } observe)
            {
                BeforeStart = null;
                await observe(cancellationToken);
            }
            return result;
        }
    }
}
