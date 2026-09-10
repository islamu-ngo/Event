using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Services.Registration;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed class AnonymousRetentionContactDeliveryTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = Start.AddHours(2);

    [Test]
    [Arguments(false, "live")]
    [Arguments(true, "live")]
    [Arguments(false, "expired")]
    [Arguments(true, "expired")]
    [Arguments(false, "deleted-source")]
    [Arguments(true, "deleted-source")]
    [Arguments(false, "save-crossing")]
    [Arguments(true, "save-crossing")]
    [Arguments(false, "nonanonymous")]
    [Arguments(true, "nonanonymous")]
    [Arguments(false, "smtp-live")]
    [Arguments(true, "smtp-live")]
    [Arguments(false, "smtp-config")]
    [Arguments(true, "smtp-config")]
    [Arguments(false, "smtp-connect")]
    [Arguments(true, "smtp-connect")]
    [Arguments(false, "smtp-auth")]
    [Arguments(true, "smtp-auth")]
    [Arguments(false, "smtp-accepted")]
    [Arguments(true, "smtp-accepted")]
    [Arguments(false, "smtp-ambiguous")]
    [Arguments(true, "smtp-ambiguous")]
    [Arguments(false, "channel-crossing")]
    [Arguments(true, "channel-crossing")]
    [Arguments(false, "missing-order-bound")]
    [Arguments(true, "missing-order-bound")]
    [Arguments(false, "account-live")]
    [Arguments(false, "account-erased")]
    [Arguments(false, "account-deleted")]
    [Arguments(false, "account-unverified")]
    [Arguments(false, "account-changed")]
    [Arguments(false, "held-live")]
    [Arguments(true, "held-live")]
    [Arguments(false, "held-expired")]
    [Arguments(true, "held-expired")]
    public async Task QueuedContactUsesIncludedDeadlineAtActualDrain(bool recovery, string scenario)
    {
        var clock = new Clock();
        var transport = new Transport();
        var gate = new DispatchGate();
        var protection = new CountingProtection(new EphemeralDataProtectionProvider());
        var save = new SaveBoundary(clock);
        await using var smtp = scenario.StartsWith("smtp-", StringComparison.Ordinal)
            ? new AdmissionContactSmtpPeer(scenario, () => clock.Now = new DateTimeOffset(Deadline)) : null;
        SmtpConfiguration? smtpConfiguration = smtp?.Start();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            if (smtp is null) services.AddSingleton<IEmailService>(transport);
            else
            {
                var resolver = Substitute.For<ISmtpConfigResolver>();
                resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(_ =>
                {
                    smtp.CrossConfigurationBoundary();
                    return smtpConfiguration;
                });
                services.AddSingleton(resolver);
            }
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(save));
            services.AddSingleton<IAdmissionDeliveryEnvelopeProtector>(new AdmissionDeliveryEnvelopeProtector(protection));
            services.AddSingleton<IAdmissionRecoveryDeliveryEnvelopeProtector>(new AdmissionRecoveryDeliveryEnvelopeProtector(protection));
            services.AddScoped<IOutboxMessageDispatcher>(provider =>
                new GatedDispatcher(ActivatorUtilities.CreateInstance<CompositeOutboxMessageDispatcher>(provider), gate));
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveQualifiedAsync(Arg.Any<string>(), Arg.Any<SecretScope>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new("admission-test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, new DateTimeOffset(Start))));
            services.AddSingleton(secrets);
        });
        fixture.Services.GetRequiredService<IConfiguration>()["PublicBaseUrl"] = "https://events.example.test";
        bool held = scenario.StartsWith("held-", StringComparison.Ordinal);
        var seed = await SeedAsync(fixture, scenario != "nonanonymous", held);
        bool accountContact = scenario.StartsWith("account-", StringComparison.Ordinal);
        if (accountContact)
        {
            var order = await fixture.Context.RegistrationOrders.Include(value => value.Pii).SingleAsync(value => value.Id == seed.Request.RegistrationOrderId);
            await Assert.That(order.TryLinkGuestOrderToAccount(fixture.UserId, "queued@example.test")).IsTrue();
            await fixture.Context.SaveChangesAsync();
            await fixture.Context.RegistrationOrderPii.Where(value => value.RegistrationOrderId == order.Id).ExecuteDeleteAsync();
            await fixture.Context.Users.Where(value => value.Id == fixture.UserId).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.EmailVerified, true));
            await fixture.Context.UserPii.Where(value => value.UserId == fixture.UserId).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Email, "queued@example.test"));
            fixture.Context.ChangeTracker.Clear();
        }
        DateTime includedDeadline = held ? Deadline.AddDays(1) : Deadline;
        clock.Now = new DateTimeOffset(includedDeadline.AddTicks(-1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var issuance = fixture.Services.GetRequiredService<IAdmissionIssuanceService>();
        Task pending;
        if (recovery)
        {
            var issued = await issuance.IssueConfirmedAsync(seed.Request, timeout.Token);
            await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
            await Assert.That(issued.DeliveryOutcome).IsEqualTo(AdmissionDeliveryOutcome.Delivered);
            transport.Messages.Clear();
            var staged = await fixture.Services.GetRequiredService<IAdmissionRecoveryDeliveryStager>().StageAsync(
                new(fixture.TenantId, Guid.CreateVersion7(), issued.IssuedTicketIds.Single(), AdmissionRecoveryPurpose.TicketRecovery,
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), timeout.Token);
            await Assert.That(staged.Outcome).IsEqualTo(AdmissionRecoveryDeliveryOutcome.Accepted);
            var message = await fixture.Context.OutboxMessages.SingleAsync(value =>
                value.EventType == AdmissionRecoveryDeliveryEvents.RecoveryDeliveryRequested, timeout.Token);
            gate.Armed = true;
            pending = fixture.Services.GetRequiredService<IOutboxMessageDispatcher>().DispatchAsync(message, timeout.Token);
        }
        else
        {
            gate.Armed = true;
            pending = issuance.IssueConfirmedAsync(seed.Request, timeout.Token);
        }

        try
        {
            OutboxMessage queued = await gate.Entered.Task.WaitAsync(timeout.Token);
            protection.Decryptions = 0;
            // The order expires later. The earlier deadline of the included contact must survive its source row.
            if (scenario == "deleted-source")
            {
                clock.Now = new DateTimeOffset(Deadline);
                await fixture.Context.RegistrationOrderPii.Where(value => value.RegistrationOrderId == seed.Request.RegistrationOrderId)
                    .ExecuteDeleteAsync(timeout.Token);
            }
            if (scenario == "missing-order-bound")
                await fixture.Context.RegistrationOrders.Where(value => value.Id == seed.Request.RegistrationOrderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.AnonymousPiiRetentionUntilUtc, (DateTime?)null), timeout.Token);
            if (scenario == "account-erased")
                await fixture.Context.UserPii.Where(value => value.UserId == fixture.UserId).ExecuteDeleteAsync(timeout.Token);
            if (scenario == "account-deleted")
                await fixture.Context.Users.Where(value => value.Id == fixture.UserId).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true), timeout.Token);
            if (scenario == "account-unverified")
                await fixture.Context.Users.Where(value => value.Id == fixture.UserId).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.EmailVerified, false), timeout.Token);
            if (scenario == "account-changed")
                await fixture.Context.UserPii.Where(value => value.UserId == fixture.UserId).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Email, "changed@example.test"), timeout.Token);
            if (scenario is "save-crossing" or "channel-crossing")
            {
                save.Armed = true;
                save.CrossAtChannel = scenario == "channel-crossing";
            }
            else if (scenario is not ("live" or "held-live" or "missing-order-bound") && smtp is null)
                clock.Now = new DateTimeOffset(accountContact ? Deadline.AddDays(2) : includedDeadline);
            if (smtp is not null)
            {
                smtp.Messages.Clear();
                smtp.Envelopes.Clear();
                smtp.Armed = true;
            }
            gate.Release.TrySetResult();
            Exception? failure = null;
            try { await pending.WaitAsync(timeout.Token); }
            catch (InvalidOperationException exception) { failure = exception; }

            bool allowed = scenario is "live" or "held-live" or "nonanonymous" or "smtp-live" or "smtp-accepted" or "account-live";
            if (!allowed && !accountContact)
            {
                if (recovery) await Assert.That(failure is AdmissionContactRetentionExpiredException).IsEqualTo(scenario != "smtp-ambiguous");
                else
                {
                    var result = await (Task<AdmissionIssuanceResult>)pending;
                    await Assert.That(result.DeliveryOutcome).IsEqualTo(scenario == "smtp-ambiguous"
                        ? AdmissionDeliveryOutcome.RecoverablePending : AdmissionDeliveryOutcome.Unrecoverable);
                    if (scenario != "smtp-ambiguous") await Assert.That(result.DeliveryFailure).IsEqualTo(AdmissionDeliveryFailure.RetentionExpired);
                }
            }
            if (smtp is null) await Assert.That(transport.Messages.Count).IsEqualTo(allowed ? 1 : 0);
            else
            {
                bool handedOff = allowed || scenario == "smtp-ambiguous";
                await Assert.That(smtp.Messages.Count).IsEqualTo(handedOff ? 1 : 0);
                await Assert.That(smtp.Envelopes.Count).IsEqualTo(handedOff ? 2 : 0);
                await Assert.That(smtp.Crossed).IsEqualTo(scenario != "smtp-live");
                if (handedOff) await Assert.That(smtp.Messages.Single()).Contains("queued@example.test");
            }
            await Assert.That(protection.Decryptions).IsEqualTo(allowed || scenario is "save-crossing" or "channel-crossing" or "account-changed" || smtp is not null ? 1 : 0);
            if (scenario is "save-crossing" or "channel-crossing") await Assert.That(save.Triggered).IsTrue();
            if (allowed)
            {
                await Assert.That(failure).IsNull();
                if (smtp is null)
                {
                    await Assert.That(transport.Messages.Single().To).IsEqualTo("queued@example.test");
                    await Assert.That(transport.Messages.Single().PlainTextBody).IsNotEmpty();
                }
            }
            if (recovery)
            {
                var intent = await fixture.Context.AdmissionRecoveryDeliveryIntents.AsNoTracking().SingleAsync(value => value.Id == queued.Id, timeout.Token);
                await Assert.That(intent.HandoffCompletedAt.HasValue).IsEqualTo(allowed);
                await Assert.That(string.IsNullOrEmpty(intent.ProtectedMaterial)).IsEqualTo(allowed);
            }
            else
            {
                var intent = await fixture.Context.AdmissionDeliveryIntents.AsNoTracking().SingleAsync(value => value.Id == queued.Id, timeout.Token);
                await Assert.That(intent.HandoffCompletedAt.HasValue).IsEqualTo(allowed);
                await Assert.That(string.IsNullOrEmpty(intent.ProtectedCredential)).IsEqualTo(allowed);
            }
            await Assert.That(await fixture.Context.AdmissionTickets.CountAsync(timeout.Token)).IsEqualTo(1);
            await Assert.That(await fixture.Context.RegistrationFinalizationEffects.CountAsync(timeout.Token)).IsEqualTo(1);
        }
        finally
        {
            gate.Release.TrySetResult();
            if (!pending.IsCompleted) await pending.WaitAsync(timeout.Token);
        }
    }

    private static async Task<Seed> SeedAsync(EventVisitorCapabilitySqliteFixture fixture, bool anonymous, bool held)
    {
        var target = await fixture.SeedEventAsync(published: true);
        var catalogIds = await fixture.SeedTicketAsync(target.Id);
        var token = fixture.Services.GetRequiredService<IGuestCapabilityTokenService>().Issue();
        var order = RegistrationOrder.Create(fixture.TenantId, target.Id, anonymous ? (Guid?)null : fixture.UserId, null,
            BookingPartyTypeEnum.Individual, catalogIds.CatalogId,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required, (int)(anonymous ? IdentityAccessModeEnum.GuestAllowed : IdentityAccessModeEnum.AccountRequired),
                anonymous ? GuestRecoveryPolicyEnum.EmailOptional : null), null, anonymous ? token.Hash : null, "USD", Start,
            Start.AddHours(1), anonymous ? Deadline.AddDays(1) : null);
        order.SetPii(RegistrationOrderPii.CreateFromVerifiedContact(order.Id, fixture.TenantId, null, "queued@example.test", null, null,
            "queued@example.test", (int)(held ? RegistrationRetentionPolicyEnum.LegalHold : RegistrationRetentionPolicyEnum.StandardOperational), Start));
        var catalog = await fixture.Context.EventTicketCatalogVersions.Include(value => value.TicketTypes).SingleAsync(value => value.Id == catalogIds.CatalogId);
        var line = RegistrationOrderLine.Create(catalog, catalog.TicketTypes.Single(), order.Id, 1, null, null);
        order.AddLine(line);
        var participant = RegistrationParticipant.Create(fixture.TenantId, order.Id, fixture.UserId, ParticipantTypeEnum.Adult, null);
        order.AddParticipant(participant);
        var assignment = RegistrationTicketAssignment.CreateAssigned(Guid.CreateVersion7(), line.Id, 1, participant, Start);
        order.AddAssignment(line, assignment, participant);
        fixture.Context.Add(order);
        await fixture.Context.SaveChangesAsync();
        if (!held)
            await fixture.Context.RegistrationOrderPii.Where(value => value.RegistrationOrderId == order.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.RetentionUntil, Deadline));
        var readiness = fixture.Services.GetRequiredService<IParticipantAdmissionEligibilityRepository>();
        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            await readiness.EnsureForAssignmentsAsync(fixture.TenantId, target.Id, order.Id, [assignment.Id], Start, token);
            var completion = await readiness.LoadCompletionForUpdateAsync(fixture.TenantId, target.Id, order.Id, assignment.Id, participant.Id, fixture.UserId, token);
            completion!.Eligibility.RecordSubjectCompletion(completion.Participant, fixture.UserId, completion.SubjectConsentRecordId, Start, Guid.CreateVersion7());
            await readiness.ApplyDecisionAsync(completion.Eligibility, token);
        });
        await fixture.Context.RegistrationOrders.Where(value => value.Id == order.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.RegistrationOrderStatusId, (int)RegistrationOrderStatusEnum.Confirmed)
            .SetProperty(value => value.ConfirmedAt, Start));
        fixture.Context.ChangeTracker.Clear();
        order = await fixture.Context.RegistrationOrders.SingleAsync(value => value.Id == order.Id);
        var effect = RegistrationFinalizationEffect.Create(order, Start);
        fixture.Context.Add(effect);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new(new(fixture.TenantId, order.Id, effect.Id, AdmissionIssuanceAuthority.ConfirmedFreeOrder));
    }

    private sealed record Seed(AdmissionIssuanceRequest Request);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(Start);
        public int? RemainingLiveReads { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            if (RemainingLiveReads is 0) Now = new DateTimeOffset(Deadline);
            if (RemainingLiveReads > 0) RemainingLiveReads--;
            return Now;
        }
    }
    private sealed class Transport : IEmailService
    {
        public List<EmailMessage> Messages { get; } = [];
        public Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.FromResult(EmailResult.Ok(Guid.CreateVersion7().ToString("N")));
        }
    }
    private sealed class DispatchGate
    {
        public bool Armed { get; set; }
        public TaskCompletionSource<OutboxMessage> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class GatedDispatcher(IOutboxMessageDispatcher inner, DispatchGate gate) : IOutboxMessageDispatcher
    {
        public Task ReconcileDeadLetterAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            inner.ReconcileDeadLetterAsync(message, cancellationToken);

        public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (gate.Armed)
            {
                gate.Entered.TrySetResult(message);
                await gate.Release.Task.WaitAsync(cancellationToken);
            }
            await inner.DispatchAsync(message, cancellationToken);
        }
    }
    private sealed class SaveBoundary(Clock clock) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool CrossAtChannel { get; set; }
        public bool Triggered { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                Triggered = true;
                if (CrossAtChannel) clock.RemainingLiveReads = 1;
                else clock.Now = new DateTimeOffset(Deadline);
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CountingProtection(IDataProtectionProvider inner) : IDataProtectionProvider
    {
        public int Decryptions { get; set; }
        public IDataProtector CreateProtector(string purpose) => new Protector(inner.CreateProtector(purpose), this);
        private sealed class Protector(IDataProtector inner, CountingProtection owner) : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => new Protector(inner.CreateProtector(purpose), owner);
            public byte[] Protect(byte[] plaintext) => inner.Protect(plaintext);
            public byte[] Unprotect(byte[] protectedData)
            {
                owner.Decryptions++;
                return inner.Unprotect(protectedData);
            }
        }
    }
}
