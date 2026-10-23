using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class EmailSettingBatchAuthenticationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SmtpBatch_CommitsAllFieldsAndNotificationsOrRollsBack(bool rejectPort)
    {
        var notifications = new List<SettingChangedNotification>();
        await using var fixture = await CreateFixtureAsync(notifications);
        await using (var seed = fixture.CreateIndependentContext())
        {
            await SeedEmailAsync(seed, fixture.Subject.PrincipalId);
            if (rejectPort)
            {
                var entity = seed.Model.FindEntityType(typeof(SystemSetting))!;
                var sql = seed.GetService<ISqlGenerationHelper>();
                string table = sql.DelimitIdentifier(entity.GetTableName()!);
                string key = sql.DelimitIdentifier(entity.FindProperty(nameof(SystemSetting.SettingKey))!.GetColumnName());
                await seed.Database.ExecuteSqlRawAsync($"""
                    CREATE TRIGGER reject_smtp_port_update BEFORE UPDATE ON {table}
                    WHEN NEW.{key} = 'email.smtp_port'
                    BEGIN SELECT RAISE(ABORT, 'SMTP port write rejected by test database'); END;
                    """);
            }
        }
        await using var verification = fixture.CreateIndependentContext();
        long revision = await verification.EmailDispatchProcessorStates.AsNoTracking()
            .Select(row => row.DeliveryPolicyRevision).SingleAsync();
        var request = new UpdateSettingBatchCommand
        {
            Category = EmailSettingDefinitions.SmtpHost.Category,
            Scope = SettingScope.Instance,
            Mode = BatchUpdateMode.Strict,
            Values = new Dictionary<string, string>
            {
                [GovernanceSettingKeys.Email.SmtpHost] = "smtp.changed.test",
                [GovernanceSettingKeys.Email.SmtpPort] = "465"
            }
        };
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var handler = services.GetRequiredService<ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>>();
            if (rejectPort)
                await Assert.That(async () => await handler.ExecuteAsync(request, CancellationToken.None))
                    .Throws<DbUpdateException>();
            else
            {
                var result = await handler.ExecuteAsync(request, CancellationToken.None);
                await Assert.That(result.Success).IsTrue();
                await Assert.That(result.Results.All(result => result.Applied)).IsTrue();
            }
            await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        });

        string host = await verification.SystemSettings.AsNoTracking()
            .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpHost)
            .Select(setting => setting.Value).SingleAsync();
        string port = await verification.SystemSettings.AsNoTracking()
            .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpPort)
            .Select(setting => setting.Value).SingleAsync();
        await Assert.That(host).IsEqualTo(rejectPort ? "\"smtp.fixture.test\"" : "\"smtp.changed.test\"");
        await Assert.That(port).IsEqualTo(rejectPort ? "587" : "465");
        await Assert.That(notifications.Count).IsEqualTo(rejectPort ? 0 : 2);
        if (rejectPort)
            await Assert.That(await verification.EmailDispatchProcessorStates.AsNoTracking()
                .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(revision);
        else
        {
            await Assert.That(notifications.Select(notification => notification.Key))
                .IsEquivalentTo([GovernanceSettingKeys.Email.SmtpHost, GovernanceSettingKeys.Email.SmtpPort]);
            await Assert.That(notifications.All(notification => notification.ActorUserId == fixture.Subject.PrincipalId
                && notification.TenantId is null)).IsTrue();
        }
    }

    [Test]
    public async Task StrictSmtpBatch_RejectsForeignCategoryWithoutWritingValidEmailField()
    {
        var notifications = new List<SettingChangedNotification>();
        await using var fixture = await CreateFixtureAsync(notifications);
        await using (var seed = fixture.CreateIndependentContext())
            await SeedEmailAsync(seed, fixture.Subject.PrincipalId);
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var result = await services.GetRequiredService<ICommandHandler<UpdateSettingBatchCommand,
                BatchUpdateResponseDto>>().ExecuteAsync(new UpdateSettingBatchCommand
                {
                    Category = EmailSettingDefinitions.SmtpHost.Category,
                    Scope = SettingScope.Instance,
                    Mode = BatchUpdateMode.Strict,
                    Values = new Dictionary<string, string>
                    {
                        [GovernanceSettingKeys.Email.SmtpHost] = "smtp.changed.test",
                        [GovernanceSettingKeys.TenantDelegation.LockSmtp] = "false"
                    }
                }, CancellationToken.None);
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Results.All(result => !result.Applied)).IsTrue();
            await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        });
        await using var verification = fixture.CreateIndependentContext();
        string host = await verification.SystemSettings.AsNoTracking()
            .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpHost)
            .Select(setting => setting.Value).SingleAsync();
        await Assert.That(host).IsEqualTo("\"smtp.fixture.test\"");
        await Assert.That(notifications).IsEmpty();
    }

    [Test]
    public async Task BestEffortEventsBatch_AppliesPublicationPolicyAndRejectsForeignSmtpWithoutNestedTransaction()
    {
        var notifications = new List<SettingChangedNotification>();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: true, emailEnabled: true);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
            await SeedEmailAsync(seed, fixture.Binding.LocalSubjectId);
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Restore selected-owner adoption replaced by the base HTTP fixture. The actual
            // Global context, human scope, authorities and finalizer remain production-owned.
            services.AddScoped(provider =>
            {
                if (provider.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selectedContext)
                    return selectedContext;
                var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                context.TenantContext = provider.GetService<ITenantContext>();
                context.CurrentUserService = provider.GetService<ICurrentUserService>();
                context.ClearTenantFilterBypass();
                return context;
            });
            services.AddScoped<INotificationHandler<SettingChangedNotification>>(provider =>
                new CommittedNotifications(provider.GetRequiredService<ExploreDbContext>(), notifications));
        }));
        await using var original = new ResourceVisitorImportConsumptionTests.AuthenticatedRequest(host.Services);
        await original.AuthenticateAsync(await fixture.SignInAsync());
        await using var selected = await original.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var preparation = services.GetRequiredService<IHumanCommitScopePreparation>();
        await preparation.PrepareAsync(CancellationToken.None);
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        await Assert.That(lease.HumanScope!.Purpose).IsEqualTo(HumanAdministratorCommitPurpose.GlobalInstance);
        await Assert.That(lease.PolicyPurpose).IsEqualTo(PolicyMutationPurpose.Standard);
        await Assert.That(ReferenceEquals(lease.Context, services.GetRequiredService<ExploreDbContext>())).IsTrue();
        await Assert.That(ReferenceEquals(lease.HumanScope.Subject, preparation.Subject)).IsTrue();
        await Assert.That(preparation.Subject!.PrincipalId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(preparation.Subject.AccountKey).IsEqualTo(lease.HumanLifetime!.AccountKey);
        await Assert.That(lease.HumanLifetime.LocalSession).IsNotNull();
        await Assert.That(lease.HumanLifetime.IsValidAt(fixture.Clock.GetUtcNow())).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        long revision = await observer.EmailDispatchProcessorStates.AsNoTracking()
            .Select(row => row.DeliveryPolicyRevision).SingleAsync();

        var result = await services.GetRequiredService<ICommandHandler<UpdateSettingBatchCommand,
            BatchUpdateResponseDto>>().ExecuteAsync(new UpdateSettingBatchCommand
            {
                Category = EventSettingDefinitions.RequireApproval.Category,
                Scope = SettingScope.Instance,
                Mode = BatchUpdateMode.BestEffort,
                Values = new Dictionary<string, string>
                {
                    [EventSettingDefinitions.RequireApproval.Key] = "true",
                    [GovernanceSettingKeys.Email.SmtpHost] = "smtp.foreign-category.test"
                }
            }, CancellationToken.None);

        await Assert.That(result.Results.Single(row => row.Key == EventSettingDefinitions.RequireApproval.Key).Applied).IsTrue();
        var smtpResult = result.Results.Single(row => row.Key == GovernanceSettingKeys.Email.SmtpHost);
        await Assert.That(smtpResult.Applied).IsFalse();
        await Assert.That(smtpResult.SkipReason).IsNotNull();
        await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(
            row => row.SettingKey == EventSettingDefinitions.RequireApproval.Key)).Value).IsEqualTo("true");
        await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(
            row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost)).Value).IsEqualTo("\"smtp.fixture.test\"");
        await Assert.That(await observer.EmailDispatchProcessorStates.AsNoTracking()
            .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(revision);
        await Assert.That(notifications.Single().Key).IsEqualTo(EventSettingDefinitions.RequireApproval.Key);
        await Assert.That(notifications.Single().ActorUserId).IsEqualTo(preparation.Subject.PrincipalId);
        await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
    }

    private static Task<SmtpAuthenticatedFixture> CreateFixtureAsync(List<SettingChangedNotification> notifications) =>
        SmtpAuthenticatedFixture.CreateAsync(instanceAdministrator: true, tenantAdministrator: false,
            configureServices: services =>
                services.AddScoped<INotificationHandler<SettingChangedNotification>>(provider =>
                    new CommittedNotifications(provider.GetRequiredService<ExploreDbContext>(), notifications)));

    private static async Task SeedEmailAsync(ExploreDbContext context, Guid actorId)
    {
        var unit = new EfCoreUnitOfWork(context);
        var writer = new InternalEmailDeliverySettingsWriter(context,
            new RelationalSettingMutationLock(context, unit), unit);
        (await writer.ApplyInternalAsync(
            [new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, "\"smtp.fixture.test\""),
             new(null, GovernanceSettingKeys.Email.SmtpPort, EmailDeliverySettingMutationKind.SetValue, "587")],
            actorId)).EnsureAccepted();
    }

    private sealed class CommittedNotifications(ExploreDbContext context, List<SettingChangedNotification> notifications)
        : INotificationHandler<SettingChangedNotification>
    {
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Settings notifications must follow the selected owner's commit.");
            notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
