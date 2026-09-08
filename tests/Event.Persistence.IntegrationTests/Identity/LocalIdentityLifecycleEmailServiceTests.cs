// ABOUTME: Exercises real global Local lifecycle authority and durable SMTP delivery across both native store topologies.
// ABOUTME: Covers crash recovery, exact binding, disabled admission, fixed budgets and transient token-only transport.

using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalIdentityLifecycleEmailServiceTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task RealSmtpMessageCarriesOnceEncodedFragmentAndNativeRecoveryConsumesIt(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        await using var peer = new LocalLifecycleSmtpPeer();
        fixture.UseRealSmtp = true;
        await using (var configure = fixture.Open())
        {
            await EmailDispatchSqliteFixture.ApplyEmailSettingsAsync(configure.Application,
                [new(null, GovernanceSettingKeys.Email.SmtpHost, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "\"127.0.0.1\""),
                 new(null, GovernanceSettingKeys.Email.SmtpPort, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue,
                     peer.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                 new(null, GovernanceSettingKeys.Email.SmtpSecurity, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "\"None\"")],
                cancellationToken: fixture.CancellationToken);
        }
        Task<string> received = peer.Message;
        await RequestAsync(fixture);
        await DrainAsync(fixture);
        string raw = await received.WaitAsync(fixture.CancellationToken);
        using var messageBytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(raw));
        using var mime = await MimeKit.MimeMessage.LoadAsync(messageBytes, fixture.CancellationToken);
        string body = mime.TextBody ?? throw new InvalidOperationException("SMTP message has no text body.");
        var link = new Uri(body.Split('\n').Select(line => line.Trim()).Single(line => line.StartsWith("https://", StringComparison.Ordinal)));
        await Assert.That(link.AbsolutePath).IsEqualTo("/auth/local-account-recovery");
        await Assert.That(link.Query).IsEqualTo(string.Empty);
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Fragment[1..]);
        await Assert.That(values.Count).IsEqualTo(7);
        var pointer = new LocalIdentityLifecyclePointer(Guid.Parse(values["operationId"].ToString()), Guid.Parse(values["localSubjectId"].ToString()),
            Guid.Parse(values["personalActorId"].ToString()), Guid.Parse(values["externalLoginId"].ToString()),
            (LocalIdentityLifecyclePurpose)int.Parse(values["purpose"].ToString(), System.Globalization.CultureInfo.InvariantCulture),
            Guid.Parse(values["generation"].ToString()));
        await Assert.That(pointer.Purpose).IsEqualTo(LocalIdentityLifecyclePurpose.PasswordRecovery);
        var operation = await ReadAsync(fixture);
        await Assert.That(operation.DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Accepted);
        await Assert.That(mime.MessageId).IsEqualTo($"local-{operation.Id:N}-{operation.DeliveryAttemptId:N}@lifecycle.invalid");
        string password = "Aa1!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        await using (var consume = fixture.Open())
        {
            var result = await consume.Lifecycle.ConsumeAsync(new(pointer, values["token"].ToString(), password), fixture.CancellationToken);
            await Assert.That(result.Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        }
        await Assert.That(await fixture.Native.PasswordIsValidAsync(password)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task GlobalVerificationOwnsPointerBeforeTransportAndAcceptsNativeToken(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology, verified: false);
        await RequestAsync(fixture, verification: true);
        await using (var read = fixture.Open())
        {
            var operation = await read.Identity.Set<LocalIdentityLifecycleOperation>().SingleAsync(fixture.CancellationToken);
            await Assert.That(operation.DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Pending);
            await Assert.That(operation.DeliveryAttemptCount).IsEqualTo(0);
            await Assert.That(await read.Application.Tenants.CountAsync(fixture.CancellationToken)).IsEqualTo(0);
            await Assert.That(await read.Application.TenantUsers.CountAsync(fixture.CancellationToken)).IsEqualTo(0);
            await Assert.That(await read.Application.EmailDispatchOutbox.CountAsync(fixture.CancellationToken)).IsEqualTo(0);
            await Assert.That(await read.Application.NotificationExternalDelegations.CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        }
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        fixture.Smtp.OnSend = async (handoff, attempt, token) =>
        {
            await using var read = fixture.Open();
            var operation = await read.Identity.Set<LocalIdentityLifecycleOperation>().SingleAsync(token);
            await Assert.That(operation.DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Unknown);
            await Assert.That(operation.DeliveryAttemptId).IsEqualTo(attempt);
            await Assert.That(JsonSerializer.Serialize(operation).Contains(handoff.Token, StringComparison.Ordinal)).IsFalse();
            var storedValues = await read.Identity.Set<IdentityUserToken<Guid>>().Select(row => row.Value).ToListAsync(token);
            await Assert.That(storedValues.Any(value => value?.Contains(handoff.Token, StringComparison.Ordinal) == true)).IsFalse();
        };
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
        var sent = fixture.Smtp.Handoffs.Single();
        await using var consume = fixture.Open();
        var result = await consume.Lifecycle.ConsumeAsync(new(sent.Operation, sent.Token), fixture.CancellationToken);
        await Assert.That(result.Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        await Assert.That(result.Synchronization!.EmailVerified).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task DisabledRepeatReenabledDeliveryRetainsDeadlineAndOperation(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        await SetEnabledAsync(fixture, false);
        await RequestAsync(fixture);
        LocalIdentityLifecycleOperation original = await ReadAsync(fixture);
        await DrainAsync(fixture);
        fixture.Native.Clock.Advance(TimeSpan.FromMinutes(5));
        await RequestAsync(fixture);
        await Assert.That((await ReadAsync(fixture)).ExpiresAt).IsEqualTo(original.ExpiresAt);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await SetEnabledAsync(fixture, true);
        await DrainAsync(fixture);
        await RequestAsync(fixture);
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
        await Assert.That(fixture.Smtp.Handoffs.Single().Operation.OperationId).IsEqualTo(original.Id);
        await Assert.That(fixture.Smtp.Handoffs.Single().ExpiresAtUtc.UtcDateTime).IsEqualTo(original.ExpiresAt);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ExpiredDisabledOperationCannotSendAfterEnable(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        await SetEnabledAsync(fixture, false);
        await RequestAsync(fixture);
        fixture.Native.Clock.Advance(TimeSpan.FromMinutes(15));
        await SetEnabledAsync(fixture, true);
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await Assert.That((await ReadAsync(fixture)).DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Failed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task UnknownAcceptanceAndProcessRestartNeverResend(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        fixture.Smtp.Outcome = SmtpDeliveryOutcome.Uncertain;
        await RequestAsync(fixture);
        await DrainAsync(fixture);
        LocalIdentityLifecycleOperation original = await ReadAsync(fixture);
        await RequestAsync(fixture);
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
        var current = await ReadAsync(fixture);
        await Assert.That(current.DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Unknown);
        await Assert.That(current.DeliveryAttemptId).IsEqualTo(original.DeliveryAttemptId);
        await Assert.That(current.DeliveryCompletedAt).IsNull();
        await Assert.That(current.ExpiresAt).IsEqualTo(original.ExpiresAt);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task CrashAfterDurableAdmissionBeforeSmtpRetainsUncertainty(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        await RequestAsync(fixture);
        await using (var crashing = fixture.Open())
        {
            var pointer = (await crashing.Deliveries.ReadPendingAsync(1, fixture.CancellationToken)).Single();
            await Assert.That(await crashing.Deliveries.TryAdmitAsync(pointer, fixture.CancellationToken)).IsNotNull();
            // The accepting process disappears here, with no token or SMTP call.
        }
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await Assert.That((await ReadAsync(fixture)).DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Unknown);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task WrongAndSupersededBindingsCannotReachTransport(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        await using (var scope = fixture.Open())
        {
            var wrong = await scope.Router.RequestPasswordResetAsync(new(fixture.Native.Receipt.LocalSubjectId, Guid.CreateVersion7()), fixture.CancellationToken);
            await Assert.That(wrong.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.AccountNotLinked);
            await Assert.That(await scope.Identity.Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        }
        await RequestAsync(fixture);
        await using (var mutation = fixture.Open())
            await mutation.Application.UserExternalLogins.Where(row => row.Id == fixture.Native.Receipt.ExternalLoginId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ProviderKey, Guid.CreateVersion7().ToString("D")), fixture.CancellationToken);
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await Assert.That((await ReadAsync(fixture)).DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Failed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task EmailChangeRequiresOriginalStampAndSendsOnlyLiveProposedAddress(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        var request = new AccountAuthorityLifecycleEmailRequest(fixture.Native.Receipt.LocalSubjectId, fixture.Native.Receipt.ExternalLoginId,
            ProposedEmail: "proposed@example.test", ExpectedSecurityStamp: "stale");
        await using (var scope = fixture.Open())
        {
            var rejected = await scope.Router.RequestEmailUpdateVerificationAsync(request, fixture.CancellationToken);
            await Assert.That(rejected.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.AccountNotLinked);
            string stamp = (await fixture.Native.ReadAsync()).SecurityStamp!;
            var accepted = await scope.Router.RequestEmailUpdateVerificationAsync(request with { ExpectedSecurityStamp = stamp }, fixture.CancellationToken);
            await Assert.That(accepted.DelegationRecorded).IsTrue();
        }
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Single().Address).IsEqualTo(request.ProposedEmail);
        var handoff = fixture.Smtp.Handoffs.Single();
        await using var consume = fixture.Open();
        var result = await consume.Lifecycle.ConsumeAsync(new(handoff.Operation, handoff.Token), fixture.CancellationToken);
        await Assert.That(result.Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        await Assert.That(result.Synchronization!.Email).IsEqualTo(request.ProposedEmail);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task KnownNonacceptanceRetriesAreBoundedAndDoNotRotateDeadline(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        fixture.Smtp.Outcome = SmtpDeliveryOutcome.TransientFailure;
        await RequestAsync(fixture);
        var original = await ReadAsync(fixture);
        for (int attempt = 0; attempt < 4; attempt++) await DrainAsync(fixture);
        var current = await ReadAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(3);
        await Assert.That(current.DeliveryAttemptCount).IsEqualTo(3);
        await Assert.That(current.DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Failed);
        await Assert.That(current.ExpiresAt).IsEqualTo(original.ExpiresAt);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task FreshOperationBudgetCannotBeBypassedByExpiryOrPublicRepeats(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        for (int operation = 0; operation < 3; operation++)
        {
            await RequestAsync(fixture);
            await RequestAsync(fixture);
            fixture.Native.Clock.Advance(TimeSpan.FromMinutes(16));
        }
        await using var scope = fixture.Open();
        var rejected = await scope.Router.RequestPasswordResetAsync(new(fixture.Native.Receipt.LocalSubjectId, fixture.Native.Receipt.ExternalLoginId),
            fixture.CancellationToken);
        await Assert.That(rejected.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.AccountNotLinked);
        await Assert.That(await scope.Identity.Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(3);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task GlobalSmtpPauseAndSharedRateAuthorityHoldNativeOperations(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        await RequestAsync(fixture);
        await using (var scope = fixture.Open())
        {
            await scope.Application.EmailDispatchProcessorStates.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsPaused, true),
                fixture.CancellationToken);
        }
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await using (var scope = fixture.Open())
        {
            await scope.Application.EmailDispatchProcessorStates.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsPaused, false)
                .SetProperty(row => row.SmtpAvailableTokens, 0).SetProperty(row => row.SmtpRefillAt, DateTime.UtcNow.AddHours(1)), fixture.CancellationToken);
        }
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await Assert.That((await ReadAsync(fixture)).DeliveryAttemptCount).IsEqualTo(0);
        await using (var scope = fixture.Open())
        {
            await scope.Application.EmailDispatchProcessorStates.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SmtpAvailableTokens, 1),
                fixture.CancellationToken);
        }
        await DrainAsync(fixture);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
        await using var read = fixture.Open();
        await Assert.That((await read.Application.EmailDispatchProcessorStates.SingleAsync(fixture.CancellationToken)).SmtpAvailableTokens).IsEqualTo(0);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task AdmissionWinsDisableCompletesWhileAdmittedTransportFinishes(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        await RequestAsync(fixture);
        var entered = Signal(); var release = Signal(); var disableAttempted = Signal();
        fixture.Smtp.OnSend = async (_, _, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        Task drain = DrainAsync(fixture);
        await entered.Task.WaitAsync(fixture.CancellationToken);
        await using var disabling = fixture.Open((_, _) => { disableAttempted.TrySetResult(); return Task.CompletedTask; });
        Task disable = EmailDispatchSqliteFixture.SetEmailSettingAsync(disabling.Application,
            GovernanceSettingKeys.Email.DeliveryEnabled, "false", mutationLock: disabling.MutationLock, cancellationToken: fixture.CancellationToken);
        await disableAttempted.Task.WaitAsync(fixture.CancellationToken);
        await disable.WaitAsync(fixture.CancellationToken);
        await Assert.That(drain.IsCompleted).IsFalse();
        release.TrySetResult();
        await drain.WaitAsync(fixture.CancellationToken);
        await Assert.That((await ReadAsync(fixture)).DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Accepted);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task DisableWinsWaitingAdmissionSeesCommittedPolicy(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalLifecycleDeliveryFixture.CreateAsync(topology);
        await RequestAsync(fixture);
        var disabled = Signal(); var release = Signal(); var admissionAttempted = Signal();
        await using var disabling = fixture.Open();
        Task hold = disabling.MutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All], async token =>
        {
            await new EfCoreUnitOfWork(disabling.Application).ExecuteSerializableAsync(async inner =>
            {
                await EmailDispatchSqliteFixture.SetEmailSettingAsync(disabling.Application, GovernanceSettingKeys.Email.DeliveryEnabled,
                    "false", mutationLock: disabling.MutationLock, cancellationToken: inner);
                return true;
            }, token);
            disabled.TrySetResult();
            await release.Task.WaitAsync(token);
            return true;
        }, fixture.CancellationToken);
        await disabled.Task.WaitAsync(fixture.CancellationToken);
        await using var draining = fixture.Open((_, _) => { admissionAttempted.TrySetResult(); return Task.CompletedTask; });
        Task drain = draining.Processor.DrainAsync(fixture.CancellationToken);
        await admissionAttempted.Task.WaitAsync(fixture.CancellationToken);
        release.TrySetResult();
        await Task.WhenAll(hold, drain).WaitAsync(fixture.CancellationToken);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await Assert.That((await ReadAsync(fixture)).DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Pending);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task RequestAsync(LocalLifecycleDeliveryFixture fixture, bool verification = false)
    {
        await using var scope = fixture.Open();
        var request = new AccountAuthorityLifecycleEmailRequest(fixture.Native.Receipt.LocalSubjectId, fixture.Native.Receipt.ExternalLoginId);
        var result = verification ? await scope.Router.RequestEmailVerificationAsync(request, fixture.CancellationToken)
            : await scope.Router.RequestPasswordResetAsync(request, fixture.CancellationToken);
        await Assert.That(result.DelegationRecorded).IsTrue();
        await Assert.That(result.NotificationIntentId).IsNull();
        await Assert.That(result.LocalDelegationId).IsNull();
    }
    private static async Task DrainAsync(LocalLifecycleDeliveryFixture fixture)
    {
        await using var scope = fixture.Open();
        await scope.Processor.DrainAsync(fixture.CancellationToken);
    }
    private static async Task<LocalIdentityLifecycleOperation> ReadAsync(LocalLifecycleDeliveryFixture fixture)
    {
        await using var scope = fixture.Open();
        return await scope.Identity.Set<LocalIdentityLifecycleOperation>().AsNoTracking().SingleAsync(fixture.CancellationToken);
    }
    private static async Task SetEnabledAsync(LocalLifecycleDeliveryFixture fixture, bool enabled)
    {
        await using var scope = fixture.Open();
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(scope.Application, GovernanceSettingKeys.Email.DeliveryEnabled,
            enabled ? "true" : "false", cancellationToken: fixture.CancellationToken);
    }
}
