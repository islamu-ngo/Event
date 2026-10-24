using System.Diagnostics.Metrics;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
[Category(InfrastructureTestCategories.RabbitMQ)]
[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[ClassDataSource<RabbitMqContainerFixture, MailpitContainerFixture>(Shared = [SharedType.PerClass, SharedType.PerClass])]
[NotInParallel("RabbitMqBroker")]
public sealed class RabbitMqEmailDispatchConsumerMailpitTests(
    RabbitMqContainerFixture rabbitMq,
    MailpitContainerFixture mailpit)
{
    private static readonly JsonSerializerOptions PointerJsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Timeout(180_000)]
    public async Task Consumer_WithValidPointer_DrainsToMailpitAndAcksAfterDurableOutcome()
    {
        await mailpit.ClearMessagesAsync();
        var settings = CreateSettings(rabbitMq);
        var subject = $"RabbitMQ registration confirmation {Guid.CreateVersion7():N}";
        const string body = "Your registration was confirmed through the RabbitMQ consumer.";
        await using var database = await new PersistedEmailDispatchScenario(mailpit).CreateDatabaseAsync(subject, body);
        await using var provider = PersistedEmailDispatchScenario.CreateServiceProvider(
            database, CreateSmtpEmailService(), settings.ConsumerId);
        await using var context = database.CreateContext(database.TenantId);
        var dispatch = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        var observer = new SettlementObserver();
        Task settled = observer.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(45));
        var consumer = new EmailDispatchRabbitMqConsumerService(
            new ConfigurationBuilder().Build(), settingsMonitor, transport, provider,
            CreateMetrics(), observer);

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var published = await transport.PublishDispatchPointerAsync(
                EmailDispatchPointer.FromOutbox(dispatch), CancellationToken.None);
            await Assert.That(published.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);
            await settled;
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
            consumer.Dispose();
        }

        await PersistedEmailDispatchScenario.AssertSentStateAsync(database);
        var receipt = await context.EmailDispatchReceipts.AsNoTracking().SingleAsync();
        await Assert.That(receipt.ConsumerId).IsEqualTo(settings.ConsumerId);
        await using var broker = await new ConnectionFactory { Uri = new Uri(rabbitMq.AmqpConnectionString) }
            .CreateConnectionAsync();
        await using var channel = await broker.CreateChannelAsync();
        await Assert.That(await channel.BasicGetAsync(settings.DispatchQueueName, autoAck: true)).IsNull();
        var messages = await mailpit.GetMessagesAsync(CancellationToken.None);
        var summary = messages.Single(message => message.Subject == subject);
        await Assert.That(summary.To.Single().Address).IsEqualTo(dispatch.RecipientEmail);
        var detail = await mailpit.GetMessageAsync(summary.Id);
        await Assert.That(detail.Text).Contains(body);
        await Assert.That(detail.Html).Contains($"<p>{body}</p>");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Consumer_KeepsAcceptedPointerRecoverableUntilSettlementCommits(bool failSent)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var gate = new AcceptanceCommitGate(failSent, deadline.Token);
        var originalTask = new TaskCompletionSource<Task<EmailDispatchSingleDrainResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var replayTask = new TaskCompletionSource<Task<EmailDispatchSingleDrainResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PersistedEmailDispatchScenario.DispatchDatabase? database = null;
        ServiceProvider? originalProvider = null;
        ServiceProvider? replayProvider = null;
        EmailDispatchRabbitMqConsumerService? originalConsumer = null;
        EmailDispatchRabbitMqConsumerService? replayConsumer = null;
        IConnection? broker = null;
        IChannel? observerChannel = null;
        var failures = new List<Exception>();
        bool originalStopped = false;
        bool replayStopped = false;
        bool originalStarted = false;
        bool replayStarted = false;
        BusinessMetrics? originalMetrics = null;
        BusinessMetrics? replayMetrics = null;
        RabbitMqEmailDispatchTransport? transport = null;
        var settings = CreateSettings(rabbitMq);
        var monitor = CreateOptionsMonitor(settings);
        try
        {
            originalMetrics = CreateMetrics();
            replayMetrics = CreateMetrics();
            transport = CreateTransport(monitor);
            await mailpit.ClearMessagesAsync();
            string subject = $"Durable RabbitMQ settlement {Guid.CreateVersion7():N}";
            database = await new PersistedEmailDispatchScenario(mailpit).CreateDatabaseAsync(
                subject, "Accepted mail must not be blindly resent.", gate);
            EmailDispatchOutbox dispatch;
            await using (var initial = database.CreateContext(database.TenantId))
                dispatch = await initial.EmailDispatchOutbox.AsNoTracking().SingleAsync(deadline.Token);
            gate.Arm(database.TenantId, dispatch.Id);
            var pointer = EmailDispatchPointer.FromOutbox(dispatch);
            originalProvider = PersistedEmailDispatchScenario.CreateServiceProvider(database,
                CreateSmtpEmailService(), settings.ConsumerId, task => originalTask.TrySetResult(task));
            originalConsumer = new EmailDispatchRabbitMqConsumerService(
                new ConfigurationBuilder().Build(), monitor, transport, originalProvider,
                originalMetrics, NullLogger<EmailDispatchRabbitMqConsumerService>.Instance);
            originalStarted = true;
            await originalConsumer.StartAsync(deadline.Token);
            var published = await transport.PublishDispatchPointerAsync(pointer, deadline.Token);
            await Assert.That(published.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);

            await using (var committed = database.CreateContext(database.TenantId))
            {
                var pending = await committed.EmailDispatchOutbox.AsNoTracking().SingleAsync(deadline.Token);
                await Assert.That(pending.Status).IsEqualTo(EmailDispatchStatus.Processing);
                await Assert.That(pending.ProcessingLeaseToken).IsNotNull();
                await Assert.That(pending.AttemptCount).IsEqualTo(1);
                var attempt = await committed.EmailDispatchAttempts.AsNoTracking().SingleAsync(row =>
                    row.TenantId == database.TenantId && row.EmailDispatchOutboxId == dispatch.Id,
                    deadline.Token);
                await Assert.That(attempt.AttemptNumber).IsEqualTo(pending.AttemptCount);
                await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
                await Assert.That(attempt.FailureCategory).IsEqualTo("provider_handoff_started");
                await Assert.That(attempt.CompletedAt).IsNull();
                var receipt = await committed.EmailDispatchReceipts.AsNoTracking().SingleAsync(row =>
                    row.TenantId == database.TenantId && row.EmailDispatchOutboxId == dispatch.Id,
                    deadline.Token);
                await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Processing);
                await Assert.That(receipt.CompletedAt).IsNull();
            }
            await Assert.That((await mailpit.GetMessagesAsync(deadline.Token))
                .Count(message => message.Subject == subject)).IsEqualTo(1);

            broker = await new ConnectionFactory
            {
                Uri = new Uri(rabbitMq.AmqpConnectionString), AutomaticRecoveryEnabled = false,
                ClientProvidedName = $"{settings.ClientProvidedName}:settlement-observer"
            }.CreateConnectionAsync(deadline.Token);
            observerChannel = await broker.CreateChannelAsync(cancellationToken: deadline.Token);
            await observerChannel.BasicQosAsync(0, 1, false, deadline.Token);
            var recovered = new TaskCompletionSource<(bool Redelivered, EmailDispatchPointer Pointer)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var observer = new AsyncEventingBasicConsumer(observerChannel);
            observer.ReceivedAsync += (_, args) =>
            {
                try
                {
                    var recoveredPointer = JsonSerializer.Deserialize<EmailDispatchPointer>(
                        args.Body.Span, PointerJsonOptions)
                        ?? throw new InvalidOperationException("email_settlement_pointer_missing");
                    recovered.TrySetResult((args.Redelivered, recoveredPointer));
                }
                catch (Exception failure) { recovered.TrySetException(failure); }
                return Task.CompletedTask;
            };
            await observerChannel.BasicConsumeAsync(settings.DispatchQueueName, autoAck: false,
                consumerTag: $"{settings.ConsumerId}:settlement-observer", noLocal: false, exclusive: false,
                arguments: null, consumer: observer, cancellationToken: deadline.Token);
            await CloseOriginalConsumerConnectionAsync(settings, deadline.Token);
            var redelivery = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);
            await Assert.That(redelivery.Redelivered).IsTrue();
            await Assert.That(redelivery.Pointer).IsEqualTo(pointer);
            await Assert.That(gate.Release.Task.IsCompleted).IsFalse();
            await Assert.That(gate.Committed.Task.IsCompleted).IsFalse();

            gate.Release.TrySetResult();
            await gate.Committed.Task.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);
            var actualDrain = await originalTask.Task.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);
            var result = await actualDrain.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);
            await Assert.That(result.Outcome).IsEqualTo(failSent
                ? EmailDispatchDrainOutcome.Unknown : EmailDispatchDrainOutcome.Sent);
            await Assert.That(gate.SentFaultReached).IsEqualTo(failSent);
            await originalConsumer.StopAsync(deadline.Token);
            originalStopped = true;
            await AssertAcceptanceStateAsync(database, failSent, deadline.Token);

            // The observer keeps the sole pointer unavailable to automatic recovery
            // until the original consumer has stopped. Closing it requeues naturally.
            await observerChannel.CloseAsync(200, "Accepted settlement observer finished",
                abort: false, cancellationToken: deadline.Token);
            await observerChannel.DisposeAsync();
            observerChannel = null;
            var replayObserver = new SettlementObserver(EmailDispatchDrainOutcome.AlreadySettled);
            replayProvider = PersistedEmailDispatchScenario.CreateServiceProvider(database,
                CreateSmtpEmailService(), settings.ConsumerId, task => replayTask.TrySetResult(task));
            replayConsumer = new EmailDispatchRabbitMqConsumerService(
                new ConfigurationBuilder().Build(), monitor, transport, replayProvider,
                replayMetrics, replayObserver);
            replayStarted = true;
            await replayConsumer.StartAsync(deadline.Token);
            await replayObserver.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);
            var actualReplay = await replayTask.Task.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token);
            await Assert.That((await actualReplay.WaitAsync(TimeSpan.FromSeconds(45), deadline.Token)).Outcome)
                .IsEqualTo(EmailDispatchDrainOutcome.AlreadySettled);
            await replayConsumer.StopAsync(deadline.Token);
            replayStopped = true;
            await AssertAcceptanceStateAsync(database, failSent, deadline.Token);
            await Assert.That((await mailpit.GetMessagesAsync(deadline.Token))
                .Count(message => message.Subject == subject)).IsEqualTo(1);
            await using var finalChannel = await broker.CreateChannelAsync(cancellationToken: deadline.Token);
            await Assert.That(await finalChannel.BasicGetAsync(settings.DispatchQueueName,
                autoAck: false, cancellationToken: deadline.Token)).IsNull();
            var queue = await rabbitMq.GetQueueAsync(settings.DispatchQueueName, deadline.Token);
            try
            {
                while (queue.MessagesReady != 0 || queue.MessagesUnacknowledged != 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
                    queue = await rabbitMq.GetQueueAsync(settings.DispatchQueueName, deadline.Token);
                }
            }
            catch (OperationCanceledException failure) when (deadline.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"RabbitMQ settlement did not converge before the deadline. MessagesReady={queue.MessagesReady}; MessagesUnacknowledged={queue.MessagesUnacknowledged}.",
                    failure);
            }
            await Assert.That(queue.MessagesReady).IsEqualTo(0);
            await Assert.That(queue.MessagesUnacknowledged).IsEqualTo(0);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            gate.Release.TrySetResult();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            bool originalDrained = await DrainAndStopAsync(originalConsumer, originalTask,
                originalStarted, originalStopped, failures, cleanup.Token);
            bool replayDrained = await DrainAndStopAsync(replayConsumer, replayTask,
                replayStarted, replayStopped, failures, cleanup.Token);
            if (originalDrained && replayDrained)
            {
                try { if (observerChannel is not null) await observerChannel.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
                try { if (broker is not null) await broker.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
                try { if (replayProvider is not null) await replayProvider.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
                try { if (originalProvider is not null) await originalProvider.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
                try { if (database is not null) await database.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
                try { originalMetrics?.Dispose(); }
                catch (Exception failure) { failures.Add(failure); }
                try { replayMetrics?.Dispose(); }
                catch (Exception failure) { failures.Add(failure); }
                try { if (transport is not null) await transport.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
            }
            // An unsettled native task must not race provider/database disposal.
            // Such a failure retains custody for the parent's finite process boundary.
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0)
            throw new AggregateException("Native email settlement or owned cleanup failed.", failures);
    }

    private async Task CloseOriginalConsumerConnectionAsync(
        EmailDispatchRabbitMqSettings settings, CancellationToken cancellationToken)
    {
        var authority = new ConnectionFactory { Uri = new Uri(rabbitMq.AmqpConnectionString) };
        using var client = new HttpClient { BaseAddress = new Uri(rabbitMq.ManagementBaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{authority.UserName}:{authority.Password}")));
        using var response = await client.GetAsync("/api/connections", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("email_consumer_connection_inventory_invalid");
        string expected = $"{settings.ClientProvidedName}:{settings.ConsumerId}";
        var matches = document.RootElement.EnumerateArray().Where(connection =>
            connection.TryGetProperty("client_properties", out var properties)
            && properties.TryGetProperty("connection_name", out var name)
            && name.ValueKind == JsonValueKind.String && name.GetString() == expected).ToArray();
        if (matches.Length != 1 || !matches[0].TryGetProperty("name", out var identity)
            || identity.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(identity.GetString()))
            throw new InvalidOperationException("email_consumer_connection_identity_unproved");
        using var closed = await client.DeleteAsync(
            "/api/connections/" + Uri.EscapeDataString(identity.GetString()!), cancellationToken);
        closed.EnsureSuccessStatusCode();
    }

    private static async Task AssertAcceptanceStateAsync(
        PersistedEmailDispatchScenario.DispatchDatabase database, bool unknown, CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext(database.TenantId);
        var outbox = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(outbox.Status).IsEqualTo(unknown ? EmailDispatchStatus.Unknown : EmailDispatchStatus.Sent);
        await Assert.That(outbox.AttemptCount).IsEqualTo(1);
        await Assert.That(outbox.ProcessingLeaseToken).IsNull();
        var attempt = await context.EmailDispatchAttempts.AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(attempt.Outcome).IsEqualTo(unknown ? EmailDispatchAttemptOutcome.Unknown : EmailDispatchAttemptOutcome.Succeeded);
        var receipt = await context.EmailDispatchReceipts.AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(receipt.Status).IsEqualTo(unknown ? EmailDispatchReceiptStatus.Unknown : EmailDispatchReceiptStatus.Completed);
        var delivery = await context.NotificationDeliveries.AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(delivery.StatusId).IsEqualTo((int)(unknown ? NotificationDeliveryStatusEnum.Unknown : NotificationDeliveryStatusEnum.Delivered));
    }

    private static async Task<bool> DrainAndStopAsync(EmailDispatchRabbitMqConsumerService? consumer,
        TaskCompletionSource<Task<EmailDispatchSingleDrainResult>> observed, bool started, bool stopped,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        if (consumer is null) return true;
        if (started && !stopped)
        {
            try
            {
                var native = await observed.Task.WaitAsync(cancellationToken);
                try { await native.WaitAsync(cancellationToken); }
                catch (Exception failure) when (native.IsCompleted) { failures.Add(failure); }
                await consumer.StopAsync(cancellationToken);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
                return false;
            }
        }
        try { consumer.Dispose(); }
        catch (Exception failure) { failures.Add(failure); }
        return true;
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    [Timeout(180_000)]
    public async Task Consumer_WithMalformedOrMissingPointer_RejectsToDeadLetterWithoutSendingMail(bool malformed)
    {
        await mailpit.ClearMessagesAsync();
        var settings = CreateSettings(rabbitMq);
        await using var database = await new PersistedEmailDispatchScenario(mailpit).CreateDatabaseAsync(
            "Unclaimed notification", "This notification must not be delivered.");
        await using var provider = PersistedEmailDispatchScenario.CreateServiceProvider(
            database, CreateSmtpEmailService(), settings.ConsumerId);
        var settingsMonitor = CreateOptionsMonitor(settings);
        await using var transport = CreateTransport(settingsMonitor);
        await transport.DeclareTopologyAsync(CancellationToken.None);
        await using var broker = await new ConnectionFactory { Uri = new Uri(rabbitMq.AmqpConnectionString) }
            .CreateConnectionAsync();
        await using var channel = await broker.CreateChannelAsync();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadLetters = new AsyncEventingBasicConsumer(channel);
        deadLetters.ReceivedAsync += (_, args) =>
        {
            received.TrySetResult(Encoding.UTF8.GetString(args.Body.Span));
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(settings.DeadLetterQueueName, autoAck: true,
            consumerTag: "mailpit-dead-letter-observer", noLocal: false, exclusive: false,
            arguments: null, consumer: deadLetters, cancellationToken: CancellationToken.None);
        Task<string> delivered = received.Task.WaitAsync(TimeSpan.FromSeconds(45));
        var consumer = new EmailDispatchRabbitMqConsumerService(
            new ConfigurationBuilder().Build(), settingsMonitor, transport, provider,
            CreateMetrics(), NullLogger<EmailDispatchRabbitMqConsumerService>.Instance);
        string malformedPayload = $"not-json-{Guid.CreateVersion7():N}";
        var missing = new EmailDispatchPointer(Guid.CreateVersion7(), Guid.CreateVersion7(),
            EmailDispatchKind.RegistrationConfirmation, "notification_intent", Guid.CreateVersion7(), null, null);

        await consumer.StartAsync(CancellationToken.None);
        try
        {
            if (malformed)
            {
                await Assert.That(await rabbitMq.PublishStringAsync(
                    settings.ExchangeName, settings.DispatchRoutingKey, malformedPayload)).IsTrue();
            }
            else
            {
                var published = await transport.PublishDispatchPointerAsync(missing, CancellationToken.None);
                await Assert.That(published.Outcome).IsEqualTo(EmailDispatchPublishOutcome.Confirmed);
            }
            string payload = await delivered;
            if (malformed)
                await Assert.That(payload).IsEqualTo(malformedPayload);
            else
                await Assert.That(payload).Contains(missing.PublishEventId.ToString());
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
            consumer.Dispose();
        }

        await using var context = database.CreateContext(database.TenantId);
        var dispatch = await context.EmailDispatchOutbox.AsNoTracking().SingleAsync();
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(dispatch.AttemptCount).IsEqualTo(0);
        await Assert.That(dispatch.ProcessingLeaseToken).IsNull();
        await Assert.That(await context.EmailDispatchAttempts.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EmailDispatchReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That((await mailpit.GetMessagesAsync(CancellationToken.None)).Count).IsEqualTo(0);
        await Assert.That(await channel.BasicGetAsync(settings.DispatchQueueName, autoAck: true)).IsNull();
    }

    private sealed class AcceptanceCommitGate(bool failSent, CancellationToken deadline) : DbTransactionInterceptor
    {
        private Guid _tenantId;
        private Guid _outboxId;
        private DbTransaction? _heldTransaction;
        private DbConnection? _heldConnection;
        private ExploreDbContext? _heldContext;
        private bool _armed;
        internal bool SentFaultReached { get; private set; }
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Arm(Guid tenantId, Guid outboxId)
        {
            _tenantId = tenantId;
            _outboxId = outboxId;
            _armed = true;
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!_armed || Committed.Task.IsCompleted || eventData.Context is not ExploreDbContext context)
                return result;
            try
            {
                var outbox = await context.EmailDispatchOutbox.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.TenantId == _tenantId && row.Id == _outboxId, cancellationToken);
                if (outbox is null || outbox.Status is not (EmailDispatchStatus.Sent or EmailDispatchStatus.Unknown))
                    return result;
                bool unknown = outbox.Status == EmailDispatchStatus.Unknown;
                var attempt = await context.EmailDispatchAttempts.AsNoTracking().SingleAsync(row =>
                    row.TenantId == _tenantId && row.EmailDispatchOutboxId == _outboxId, cancellationToken);
                var receipt = await context.EmailDispatchReceipts.AsNoTracking().SingleAsync(row =>
                    row.TenantId == _tenantId && row.EmailDispatchOutboxId == _outboxId, cancellationToken);
                var delivery = await context.NotificationDeliveries.AsNoTracking().SingleAsync(row =>
                    row.TenantId == _tenantId && row.EmailDispatchOutboxId == _outboxId, cancellationToken);
                if (attempt.Outcome != (unknown ? EmailDispatchAttemptOutcome.Unknown : EmailDispatchAttemptOutcome.Succeeded)
                    || receipt.Status != (unknown ? EmailDispatchReceiptStatus.Unknown : EmailDispatchReceiptStatus.Completed)
                    || delivery.StatusId != (int)(unknown ? NotificationDeliveryStatusEnum.Unknown : NotificationDeliveryStatusEnum.Delivered)
                    || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                    || !ReferenceEquals(context.Database.GetDbConnection(), transaction.Connection))
                    throw new InvalidOperationException("email_settlement_native_transaction_unproved");
                if (failSent && !unknown && !SentFaultReached)
                {
                    SentFaultReached = true;
                    throw new AcceptedSettlementFault();
                }
                if (unknown != failSent || (failSent && !SentFaultReached) || _heldTransaction is not null)
                    throw new InvalidOperationException("email_settlement_native_phase_unexpected");
                _heldContext = context;
                _heldTransaction = transaction;
                _heldConnection = transaction.Connection;
                Reached.TrySetResult();
                using var bound = CancellationTokenSource.CreateLinkedTokenSource(deadline, cancellationToken);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(45), bound.Token);
                if (!ReferenceEquals(_heldContext, context)
                    || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                    || !ReferenceEquals(context.Database.GetDbConnection(), _heldConnection))
                    throw new InvalidOperationException("email_settlement_native_transaction_changed");
                return result;
            }
            catch (AcceptedSettlementFault) { throw; }
            catch (Exception failure)
            {
                Reached.TrySetException(failure);
                Committed.TrySetException(failure);
                throw;
            }
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(transaction, _heldTransaction) && ReferenceEquals(eventData.Context, _heldContext))
            {
                _armed = false;
                Committed.TrySetResult();
            }
            return Task.CompletedTask;
        }

        private sealed class AcceptedSettlementFault() : InvalidOperationException("email_accepted_settlement_precommit_fault") { }
    }

    private sealed class SettlementObserver(EmailDispatchDrainOutcome expected = EmailDispatchDrainOutcome.Sent)
        : ILogger<EmailDispatchRabbitMqConsumerService>
    {
        internal TaskCompletionSource Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
            var values = fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (values.GetValueOrDefault("BrokerAction") is EmailDispatchRabbitMqSettlementAction.Ack
                && values.GetValueOrDefault("DrainOutcome") is EmailDispatchDrainOutcome outcome && outcome == expected)
                Acknowledged.TrySetResult();
        }
    }

    private SmtpEmailService CreateSmtpEmailService()
    {
        var resolver = Substitute.For<ISmtpConfigResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new SmtpConfiguration
        {
            Host = mailpit.SmtpHost,
            Port = mailpit.SmtpPort,
            Security = SmtpSecurityMode.None,
            FromAddress = "noreply@example.test",
            FromName = "ISLAMU Event Tests",
            TimeoutSeconds = 10
        });

        return new SmtpEmailService(resolver, NullLogger<SmtpEmailService>.Instance);
    }

    private static RabbitMqEmailDispatchTransport CreateTransport(
        IOptionsMonitor<EmailDispatchRabbitMqSettings> settingsMonitor) =>
        new(
            new ConfigurationBuilder().Build(),
            settingsMonitor,
            CreateMetrics(),
            NullLogger<RabbitMqEmailDispatchTransport>.Instance);

    private static IOptionsMonitor<EmailDispatchRabbitMqSettings> CreateOptionsMonitor(
        EmailDispatchRabbitMqSettings settings)
    {
        var monitor = Substitute.For<IOptionsMonitor<EmailDispatchRabbitMqSettings>>();
        monitor.CurrentValue.Returns(settings);
        return monitor;
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private static EmailDispatchRabbitMqSettings CreateSettings(RabbitMqContainerFixture rabbitMq)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        return new EmailDispatchRabbitMqSettings
        {
            Enabled = true,
            ConnectionString = rabbitMq.AmqpConnectionString,
            ExchangeName = $"test.email-dispatch.consumer.{suffix}",
            DispatchQueueName = $"test.email-dispatch.consumer.dispatch.{suffix}",
            DispatchRoutingKey = $"test.email-dispatch.consumer.dispatch.{suffix}",
            DeadLetterExchangeName = $"test.email-dispatch.consumer.dlx.{suffix}",
            DeadLetterQueueName = $"test.email-dispatch.consumer.dead-letter.{suffix}",
            DeadLetterRoutingKey = $"test.email-dispatch.consumer.dead-letter.{suffix}",
            ParkingQueueName = $"test.email-dispatch.consumer.parking.{suffix}",
            ParkingRoutingKey = $"test.email-dispatch.consumer.parking.{suffix}",
            ClientProvidedName = $"test-email-dispatch-consumer-{suffix}",
            ConsumerId = $"test-email-dispatch-consumer-{suffix}",
            PrefetchCount = 1,
            PublishTimeoutSeconds = 5
        };
    }
}
