// ABOUTME: Proves SMTP PATCH writes only supplied groups across a deterministic concurrent configuration save.
// ABOUTME: Uses native handlers, persisted administrator grants, real SQLite settings writers, and the production lock seam.

using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Infrastructure.Mail;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class InstanceSmtpSettingsMutationTests
{
    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task PatchWaitingAtSmtpFence_PreservesConcurrentSaveOutsideSuppliedGroups(
        bool includeConfiguration, bool enableDelivery)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"smtp-adapter-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            Guid actor;
            await using (var seed = CreateContext(databasePath))
            {
                actor = await InstanceSettingsCommandFixture.SeedAdministratorAsync(seed);
                await ConfirmEmailDisableAsync(seed, actorUserId: actor);
                await ApplyEmailSettingsAsync(seed,
                    [new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetLock, IsLocked: true)], actor);
            }

            var reachedFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            await using var waitingContext = CreateContext(databasePath);
            var waitingLock = new RelationalSettingMutationLock(waitingContext, new EfCoreUnitOfWork(waitingContext),
                async (key, token) =>
                {
                    if (key != GovernanceSettingKeys.Email.DeliveryEnabled) return;
                    reachedFence.TrySetResult();
                    await resume.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                });
            using var waiting = new InstanceSettingsCommandFixture(waitingContext, actor, waitingLock);
            await using var concurrentContext = CreateContext(databasePath);
            using var concurrent = new InstanceSettingsCommandFixture(concurrentContext, actor);
            var supplied = Configuration("supplied", 2525, "SslOnConnect", 41, true);
            var committed = Configuration("concurrent", 465, "None", 52, true);
            Task<BaseCommandResponse<Guid>> pending = CreateHandler(waiting).Handle(new UpdateInstanceSmtpSettingsCommand
            {
                UserId = actor,
                Patch = new PatchInstanceSmtpSettingsDto
                {
                    Configuration = includeConfiguration
                        ? OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Set(supplied)
                        : OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Unspecified(),
                    DeliveryEnabled = enableDelivery ? OptionalUpdate<bool>.Set(true) : OptionalUpdate<bool>.Unspecified()
                }
            }, cancellation.Token);
            try
            {
                Task observed = await Task.WhenAny(reachedFence.Task, pending).WaitAsync(TimeSpan.FromSeconds(15));
                await Assert.That(observed).IsSameReferenceAs(reachedFence.Task);
                await Assert.That(waitingContext.Database.CurrentTransaction).IsNull();
                var saved = await CreateHandler(concurrent).Handle(new UpdateInstanceSmtpSettingsCommand
                {
                    UserId = actor,
                    Patch = new PatchInstanceSmtpSettingsDto
                    {
                        Configuration = OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Set(committed),
                        DeliveryEnabled = includeConfiguration ? OptionalUpdate<bool>.Set(true) : OptionalUpdate<bool>.Unspecified()
                    }
                }, cancellation.Token);
                await Assert.That(saved.IsSuccess).IsTrue();
            }
            finally
            {
                resume.TrySetResult();
                cancellation.CancelAfter(TimeSpan.FromSeconds(15));
                await pending;
            }

            await Assert.That((await pending).IsSuccess).IsTrue();
            await using var readback = CreateContext(databasePath);
            var rows = await readback.SystemSettings.AsNoTracking().ToDictionaryAsync(row => row.SettingKey);
            var expected = includeConfiguration ? supplied : committed;
            var configurationValues = new Dictionary<string, string>
            {
                [GovernanceSettingKeys.Email.SmtpHost] = JsonSerializer.Serialize(expected.Host),
                [GovernanceSettingKeys.Email.SmtpPort] = JsonSerializer.Serialize(expected.Port),
                [GovernanceSettingKeys.Email.SmtpSecurity] = JsonSerializer.Serialize(expected.Security),
                [GovernanceSettingKeys.Email.FromAddress] = JsonSerializer.Serialize(expected.FromAddress),
                [GovernanceSettingKeys.Email.FromName] = JsonSerializer.Serialize(expected.FromName),
                [GovernanceSettingKeys.Email.SmtpTimeoutSeconds] = JsonSerializer.Serialize(expected.TimeoutSeconds),
                [GovernanceSettingKeys.Email.SmtpSkipCertValidation] = JsonSerializer.Serialize(expected.SkipCertificateValidation)
            };
            foreach (var (key, value) in configurationValues)
                await Assert.That(rows[key].Value).IsEqualTo(value);
            await Assert.That(rows[GovernanceSettingKeys.Email.DeliveryEnabled].Value).IsEqualTo("true");
            await Assert.That(rows[GovernanceSettingKeys.Email.SmtpHost].IsLocked).IsTrue();
            // The two explicit configurations share certificate validation; it is not changed again.
            string[] changedKeys = includeConfiguration
                ? configurationValues.Keys.Where(key => key != GovernanceSettingKeys.Email.SmtpSkipCertValidation).ToArray()
                : [GovernanceSettingKeys.Email.DeliveryEnabled];
            await Assert.That(waiting.Notifications.Published.Select(notification => notification.Key)).IsEquivalentTo(changedKeys);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }
    }

    private static InstanceSmtpConfigurationWriteDto Configuration(string name, int port, string security, int timeout, bool skipCertificateValidation) =>
        new()
        {
            Host = $"smtp.{name}.test", Port = port, Security = security,
            FromAddress = $"events@{name}.test", FromName = name,
            TimeoutSeconds = timeout, SkipCertificateValidation = skipCertificateValidation
        };

    private static UpdateInstanceSmtpSettingsCommandHandler CreateHandler(InstanceSettingsCommandFixture fixture) =>
        new(fixture.AdminContext,
            new InstanceSmtpSettingService(fixture.SystemSettings, fixture.EmailDeliverySettingsWriter, fixture.Mediator),
            new SmtpConfigResolver(new EmailDeliveryCapabilityResolver(fixture.Settings,
                    Substitute.For<ISecretResolver>(), new SecretBindingRepository(fixture.Context)),
                fixture, fixture.Settings));
}
