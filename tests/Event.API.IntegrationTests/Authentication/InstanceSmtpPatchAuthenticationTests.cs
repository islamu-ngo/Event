using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class InstanceSmtpPatchAuthenticationTests
{
    [Test]
    [Timeout(180_000)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task PatchWaitingAtSmtpFence_PreservesConcurrentSaveOutsideSuppliedGroups(
        bool includeConfiguration, bool enableDelivery)
    {
        var reachedFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExploreDbContext? waitingContext = null;
        var notifications = new List<SettingChangedNotification>();
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(
            instanceAdministrator: true, tenantAdministrator: false, configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Scoped<ISettingMutationLock>(provider =>
                {
                    var context = provider.GetRequiredService<ExploreDbContext>();
                    return new RelationalSettingMutationLock(context, provider.GetRequiredService<IUnitOfWork>(),
                        async (key, token) =>
                        {
                            if (!ReferenceEquals(context, waitingContext)
                                || key != GovernanceSettingKeys.Email.DeliveryEnabled)
                                return;
                            reachedFence.TrySetResult();
                            await resume.Task.WaitAsync(token);
                        });
                }));
                services.AddScoped<INotificationHandler<SettingChangedNotification>>(provider =>
                    new CommittedNotifications(provider.GetRequiredService<ExploreDbContext>(),
                        () => waitingContext, notifications));
            });
        await using (var seed = fixture.CreateIndependentContext())
        {
            var unit = new EfCoreUnitOfWork(seed);
            var writer = new InternalEmailDeliverySettingsWriter(seed,
                new RelationalSettingMutationLock(seed, unit), unit);
            (await writer.DisableInternalAsync(null, fixture.Subject.PrincipalId)).EnsureAccepted();
            (await writer.ApplyInternalAsync(
                [new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetLock,
                    IsLocked: true)], fixture.Subject.PrincipalId)).EnsureAccepted();
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        var supplied = Configuration("supplied", 2525, "SslOnConnect", 41, true);
        var committed = Configuration("concurrent", 465, "None", 52, true);
        BaseCommandResponse<Guid>? response = null;
        Task pending = fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            waitingContext = services.GetRequiredService<ExploreDbContext>();
            response = await services.GetRequiredService<ICommandHandler<UpdateInstanceSmtpSettingsCommand,
                BaseCommandResponse<Guid>>>().ExecuteAsync(new UpdateInstanceSmtpSettingsCommand
                {
                    Patch = new PatchInstanceSmtpSettingsDto
                    {
                        Configuration = includeConfiguration
                        ? OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Set(supplied)
                        : OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Unspecified(),
                        DeliveryEnabled = enableDelivery
                        ? OptionalUpdate<bool>.Set(true) : OptionalUpdate<bool>.Unspecified()
                    }
                }, cancellation.Token);
        });
        try
        {
            Task observed = await Task.WhenAny(reachedFence.Task, pending).WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(observed).IsSameReferenceAs(reachedFence.Task);
            await Assert.That(waitingContext!.Database.CurrentTransaction).IsNull();
            await fixture.ExecuteAsync(tenantOwner: false, async services =>
            {
                var concurrentContext = services.GetRequiredService<ExploreDbContext>();
                await Assert.That(ReferenceEquals(concurrentContext, waitingContext)).IsFalse();
                await Assert.That(ReferenceEquals(concurrentContext.Database.GetDbConnection(),
                    waitingContext.Database.GetDbConnection())).IsFalse();
                var saved = await services.GetRequiredService<ICommandHandler<UpdateInstanceSmtpSettingsCommand,
                    BaseCommandResponse<Guid>>>().ExecuteAsync(new UpdateInstanceSmtpSettingsCommand
                    {
                        Patch = new PatchInstanceSmtpSettingsDto
                        {
                            Configuration = OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Set(committed),
                            DeliveryEnabled = includeConfiguration
                            ? OptionalUpdate<bool>.Set(true) : OptionalUpdate<bool>.Unspecified()
                        }
                    }, cancellation.Token);
                await Assert.That(saved.IsSuccess).IsTrue();
                await Assert.That(concurrentContext.Database.CurrentTransaction).IsNull();
            });
            // Observe the independently committed configuration before releasing the waiting patch.
            await using var committedObserver = fixture.CreateIndependentContext();
            var committedRows = await committedObserver.SystemSettings.AsNoTracking()
                .ToDictionaryAsync(row => row.SettingKey);
            foreach (var (key, value) in ConfigurationValues(committed))
                await Assert.That(committedRows[key].Value).IsEqualTo(value);
            await Assert.That(committedRows[GovernanceSettingKeys.Email.DeliveryEnabled].Value)
                .IsEqualTo(includeConfiguration ? "true" : "false");
            await Assert.That(notifications).IsEmpty();
        }
        finally
        {
            resume.TrySetResult();
            cancellation.CancelAfter(TimeSpan.FromSeconds(15));
            await pending;
        }

        await Assert.That(response!.IsSuccess).IsTrue();
        await using var readback = fixture.CreateIndependentContext();
        var rows = await readback.SystemSettings.AsNoTracking().ToDictionaryAsync(row => row.SettingKey);
        var configurationValues = ConfigurationValues(includeConfiguration ? supplied : committed);
        foreach (var (key, value) in configurationValues)
            await Assert.That(rows[key].Value).IsEqualTo(value);
        await Assert.That(rows[GovernanceSettingKeys.Email.DeliveryEnabled].Value).IsEqualTo("true");
        await Assert.That(rows[GovernanceSettingKeys.Email.SmtpHost].IsLocked).IsTrue();
        // Both explicit configurations request the same certificate-validation setting.
        string[] changedKeys = includeConfiguration
            ? configurationValues.Keys.Where(key => key != GovernanceSettingKeys.Email.SmtpSkipCertValidation).ToArray()
            : [GovernanceSettingKeys.Email.DeliveryEnabled];
        await Assert.That(notifications.Select(notification => notification.Key)).IsEquivalentTo(changedKeys);
        await Assert.That(notifications.All(notification => notification.ActorUserId == fixture.Subject.PrincipalId
            && notification.TenantId is null)).IsTrue();
    }

    private static InstanceSmtpConfigurationWriteDto Configuration(
        string name, int port, string security, int timeout, bool skipCertificateValidation) =>
        new()
        {
            Host = $"smtp.{name}.test",
            Port = port,
            Security = security,
            FromAddress = $"events@{name}.test",
            FromName = name,
            TimeoutSeconds = timeout,
            SkipCertificateValidation = skipCertificateValidation
        };

    private static Dictionary<string, string> ConfigurationValues(InstanceSmtpConfigurationWriteDto configuration) =>
        new()
        {
            [GovernanceSettingKeys.Email.SmtpHost] = JsonSerializer.Serialize(configuration.Host),
            [GovernanceSettingKeys.Email.SmtpPort] = JsonSerializer.Serialize(configuration.Port),
            [GovernanceSettingKeys.Email.SmtpSecurity] = JsonSerializer.Serialize(configuration.Security),
            [GovernanceSettingKeys.Email.FromAddress] = JsonSerializer.Serialize(configuration.FromAddress),
            [GovernanceSettingKeys.Email.FromName] = JsonSerializer.Serialize(configuration.FromName),
            [GovernanceSettingKeys.Email.SmtpTimeoutSeconds] = JsonSerializer.Serialize(configuration.TimeoutSeconds),
            [GovernanceSettingKeys.Email.SmtpSkipCertValidation] = JsonSerializer.Serialize(configuration.SkipCertificateValidation)
        };

    private sealed class CommittedNotifications(ExploreDbContext context, Func<ExploreDbContext?> waitingContext,
        List<SettingChangedNotification> notifications) : INotificationHandler<SettingChangedNotification>
    {
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Settings notifications must follow the selected owner's commit.");
            if (ReferenceEquals(context, waitingContext()))
                notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
