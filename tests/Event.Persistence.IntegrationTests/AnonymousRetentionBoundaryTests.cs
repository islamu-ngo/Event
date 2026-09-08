using Explore.Application.Features.RegistrationSubmissions.Commands;
using MediatR;
using System.Text.Json;
using Explore.Domain.Constants;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class AnonymousRetentionBoundaryTests
{
    private static readonly DateTime Bound = new(2027, 1, 8, 14, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task HistoricalGuestWithoutOriginalBoundCannotCreateParticipantPii()
    {
        await using var fixture = await WriterFixture.CreateAsync();
        RegistrationOrder order = await fixture.SeedOrderAsync();
        var response = await fixture.Participants.AddAsync((int)ParticipantTypeEnum.Adult,
            order.Id, null, new ParticipantDetailsDto("Entry name", null, null), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await fixture.Context.RegistrationParticipantPii.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task NameEditsAndRecreatedRowsKeepTheOriginalBoundAcrossRescheduleAndClaim()
    {
        await using var fixture = await WriterFixture.CreateAsync();
        RegistrationOrder order = await fixture.SeedOrderAsync(bound: Bound);
        var created = await fixture.Participants.AddAsync((int)ParticipantTypeEnum.Adult,
            order.Id, null, new ParticipantDetailsDto("Entry", null, null), CancellationToken.None);
        await Assert.That(created.IsSuccess).IsTrue();
        var repository = fixture.Services.GetRequiredService<IRegistrationParticipantRepository>();
        var original = await repository.GetParticipantForUpdateAsync(created.Id, order.Id, fixture.TenantId, CancellationToken.None);
        await Assert.That(original!.Pii!.RetentionUntil).IsEqualTo(Bound);
        var inventory = fixture.Services.GetRequiredService<IRegistrationInventoryRepository>();
        var claimed = (await inventory.GetOrderWithPiiAsync(order.Id, fixture.TenantId, CancellationToken.None))!;
        claimed.SetPii(RegistrationOrderPii.Create(order.Id, fixture.TenantId, null, "claim@example.test", null, null,
            (int)RegistrationRetentionPolicyEnum.StandardOperational, fixture.Clock.Now.UtcDateTime, Bound));
        claimed.TryLinkGuestOrderToAccount(fixture.UserId, "claim@example.test");
        await fixture.Context.SaveChangesAsync();
        await fixture.Context.Events.Where(value => value.Id == order.EventId).ExecuteUpdateAsync(setters =>
            setters.SetProperty(value => value.LastSessionEndUtc, new DateTimeOffset(2027, 4, 1, 14, 0, 0, TimeSpan.Zero)));
        fixture.Context.ChangeTracker.Clear();
        fixture.Clock.Now = fixture.Clock.Now.AddDays(1);
        var updated = await fixture.Participants.UpdateAsync(order.Id, created.Id, (int)ParticipantTypeEnum.Adult,
            null, new ParticipantDetailsDto("Edited", null, null), CancellationToken.None);
        await Assert.That(updated.IsSuccess).IsTrue();
        await Assert.That((await repository.GetParticipantForUpdateAsync(created.Id, order.Id, fixture.TenantId, CancellationToken.None))!.Pii!.RetentionUntil).IsEqualTo(Bound);
        await fixture.Context.RegistrationParticipantPii.ExecuteDeleteAsync();
        fixture.Context.ChangeTracker.Clear();
        var recreated = await fixture.Participants.UpdateAsync(order.Id, created.Id, (int)ParticipantTypeEnum.Adult,
            null, new ParticipantDetailsDto("Recreated", null, null), CancellationToken.None);
        await Assert.That(recreated.IsSuccess).IsTrue();
        await Assert.That((await repository.GetParticipantForUpdateAsync(created.Id, order.Id, fixture.TenantId, CancellationToken.None))!.Pii!.RetentionUntil).IsEqualTo(Bound);
        fixture.Clock.Now = new DateTimeOffset(Bound);
        var expired = await fixture.Participants.UpdateAsync(order.Id, created.Id, (int)ParticipantTypeEnum.Adult,
            null, new ParticipantDetailsDto("Expired", null, null), CancellationToken.None);
        await Assert.That(expired.IsSuccess).IsFalse();
        await fixture.Context.RegistrationParticipantPii.ExecuteDeleteAsync();
        fixture.Context.ChangeTracker.Clear();
        var late = await fixture.Participants.UpdateAsync(order.Id, created.Id, (int)ParticipantTypeEnum.Adult,
            null, new ParticipantDetailsDto("Late recreation", null, null), CancellationToken.None);
        await Assert.That(late.IsSuccess).IsFalse();
        await Assert.That(await fixture.Context.RegistrationParticipantPii.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task NoneCollectionRejectsContactAndCreatesNoPii()
    {
        await using var fixture = await WriterFixture.CreateAsync();
        var order = await fixture.SeedOrderAsync(ParticipantDataCollectionModeEnum.None, Bound);
        var result = await fixture.Participants.AddAsync((int)ParticipantTypeEnum.Adult, order.Id, null,
            new ParticipantDetailsDto("Unnecessary", "unnecessary@example.test", null), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(await fixture.Context.RegistrationParticipantPii.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.RegistrationOrderPii.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task HeldParticipantEditPreservesPhysicalHoldButCannotWriteAtExpiry()
    {
        await using var fixture = await WriterFixture.CreateAsync();
        var order = await fixture.SeedOrderAsync(bound: Bound);
        var participant = RegistrationParticipant.Create(fixture.TenantId, order.Id, null, ParticipantTypeEnum.Adult, null);
        participant.SetPii(RegistrationParticipantPii.Create(participant.Id, fixture.TenantId, "Held", null, null,
            (int)RegistrationRetentionPolicyEnum.LegalHold, fixture.Clock.Now.UtcDateTime, Bound));
        fixture.Context.RegistrationParticipants.Add(participant);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var updated = await fixture.Participants.UpdateAsync(order.Id, participant.Id, (int)ParticipantTypeEnum.Adult,
            null, new ParticipantDetailsDto("Held edit", null, null), CancellationToken.None);
        await Assert.That(updated.IsSuccess).IsTrue();
        var repository = fixture.Services.GetRequiredService<IRegistrationParticipantRepository>();
        await Assert.That((await repository.GetParticipantForUpdateAsync(participant.Id, order.Id, fixture.TenantId, CancellationToken.None))!.Pii!.RetentionUntil).IsNull();
        fixture.Clock.Now = new DateTimeOffset(Bound);
        await Assert.That((await fixture.Participants.UpdateAsync(order.Id, participant.Id, (int)ParticipantTypeEnum.Adult,
            null, new ParticipantDetailsDto("Denied", null, null), CancellationToken.None)).IsSuccess).IsFalse();
        await Assert.That(await fixture.Context.RegistrationParticipantPii.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(null, false, 8)]
    [Arguments("0", false, 1)]
    [Arguments("30", false, 31)]
    [Arguments("30", true, 3)]
    public async Task AllocationResolvesSettingsOnceAndReplayCannotExtend(string? days, bool locked, int expectedDay)
    {
        await using var fixture = await WriterFixture.CreateAsync();
        if (days is not null)
        {
            fixture.Context.SystemSettings.Add(new SystemSetting { Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.AnonymousRegistration.RetentionDays, Value = "\"2\"", IsLocked = locked });
            fixture.Context.TenantSettingOverrides.Add(new TenantSetting { Id = Guid.CreateVersion7(), TenantId = fixture.TenantId,
                Tenant = null!, SettingKey = GovernanceSettingKeys.AnonymousRegistration.RetentionDays, Value = JsonSerializer.Serialize(days) });
            await fixture.Context.SaveChangesAsync();
        }
        var target = await fixture.Inner.SeedEventAsync(published: true);
        var ticket = await fixture.Inner.SeedTicketAsync(target.Id);
        var proof = await fixture.Inner.IssueGuestProofAsync(new StartGuestRegistrationOrderCommand(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        var response = await fixture.Inner.ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request);
        await Assert.That(response.IsSuccess).IsTrue();
        var inventory = fixture.Services.GetRequiredService<IRegistrationInventoryRepository>();
        var order = (await inventory.GetOrderWithLinesAsync(response.Id, fixture.TenantId, CancellationToken.None))!;
        await Assert.That(order.AnonymousPiiRetentionUntilUtc).IsEqualTo(new DateTime(2027, 1, expectedDay, 14, 0, 0, DateTimeKind.Utc));
        await fixture.Context.Events.Where(value => value.Id == target.Id).ExecuteUpdateAsync(setters =>
            setters.SetProperty(value => value.LastSessionEndUtc, new DateTimeOffset(2027, 4, 1, 14, 0, 0, TimeSpan.Zero)));
        var replay = await fixture.Inner.ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request);
        await Assert.That(replay.Id).IsEqualTo(response.Id);
        await Assert.That((await inventory.GetOrderWithLinesAsync(response.Id, fixture.TenantId, CancellationToken.None))!.AnonymousPiiRetentionUntilUtc)
            .IsEqualTo(order.AnonymousPiiRetentionUntilUtc);
        await Assert.That(await fixture.Context.RegistrationOrderPii.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.RegistrationParticipantPii.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CanonicalNormalizationCapsPlaintextAndCiphertextAndRefusesExpiredWrites(bool held)
    {
        await using var fixture = await WriterFixture.CreateAsync();
        DateTime now = fixture.Clock.Now.UtcDateTime;
        var target = await fixture.Inner.SeedEventAsync(published: true);
        var ticket = await fixture.Inner.SeedTicketAsync(target.Id);
        var workflow = RegistrationWorkflow.Create(fixture.TenantId, target.Id, "RETENTION", now);
        var requirement = RegistrationRequirement.Create(workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, now);
        var channel = RegistrationChannel.Create(requirement, 1, true, null, now);
        requirement.AddChannel(channel);
        workflow.AddRequirement(requirement);
        var form = RegistrationForm.Create(fixture.TenantId, target.Id, "platform.registration", "retention", "Retention", now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", now);
        int policy = (int)(held ? RegistrationRetentionPolicyEnum.LegalHold : RegistrationRetentionPolicyEnum.StandardOperational);
        var plain = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1, "platform.registration", "entry", "Entry",
            RegistrationFieldTypeEnum.ShortText, policy, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, now);
        var sensitive = RegistrationFormField.Create(Guid.CreateVersion7(), section, 2, "platform.registration", "private_entry", "Private entry",
            RegistrationFieldTypeEnum.ShortText, policy, RegistrationOrganizerVisibilityEnum.Hidden, false, false, now);
        version.AddSection(section);
        version.AddField(section, plain);
        version.AddField(section, sensitive);
        form.AddVersion(version);
        fixture.Services.GetRequiredService<FormSchemaArtifactPublicationService>().Publish(version, now);
        var order = RegistrationOrder.Create(fixture.TenantId, target.Id, null, null, BookingPartyTypeEnum.Individual,
            ticket.CatalogId, RegistrationParticipationSnapshot.From(target.ParticipationConfiguration!), workflow.Id,
            CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), "USD", now, now.AddMinutes(15), Bound);
        fixture.Context.AddRange(workflow, form, order);
        await fixture.Context.SaveChangesAsync();
        var attempt = RegistrationAttempt.Create(fixture.TenantId, target.Id, order.Id, workflow.Id, requirement.Id, channel.Id,
            form.Id, version.Id, CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), null, null,
            now, now.AddMinutes(10));
        var submission = RegistrationSubmission.CreateNativeEvidenceOnly(attempt,
            RegistrationEvidenceHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), now, null);
        fixture.Context.AddRange(attempt, submission);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var request = new NormalizeRegistrationSubmissionCommand(fixture.TenantId, submission.Id,
            [new(plain.Id, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, null, JsonSerializer.SerializeToElement("Entry")),
             new(sensitive.Id, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, null, JsonSerializer.SerializeToElement("Private"))]);
        var handler = fixture.Services.GetRequiredService<IRequestHandler<NormalizeRegistrationSubmissionCommand, RegistrationSubmissionNormalizationResult>>();
        var result = await handler.Handle(request, CancellationToken.None);
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.AnswerCount).IsEqualTo(2);
        var answers = await fixture.Context.RegistrationAnswers.AsNoTracking().ToArrayAsync();
        await Assert.That(answers.All(answer => answer.RetentionUntil == (held ? (DateTime?)null : Bound))).IsTrue();
        var ciphertext = await fixture.Context.RegistrationSensitiveAnswerValues.AsNoTracking().SingleAsync();
        await Assert.That(ciphertext.RetentionUntil).IsEqualTo(held ? (DateTime?)null : Bound);
        await Assert.That(answers.Single(answer => answer.RegistrationFormFieldId == sensitive.Id).TextValue).IsNull();
        fixture.Clock.Now = new DateTimeOffset(Bound);
        var expired = await handler.Handle(request, CancellationToken.None);
        await Assert.That(expired.IsValid).IsFalse();
        await Assert.That(expired.AnswerCount).IsEqualTo(0);
        await Assert.That(expired.Issues.Any(issue => issue.Code == "ANONYMOUS_RETENTION_EXPIRED")).IsTrue();
        await Assert.That(await fixture.Context.RegistrationAnswers.CountAsync()).IsEqualTo(2);
        await Assert.That(await fixture.Context.RegistrationSensitiveAnswerValues.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task PersistedOriginalBoundRejectsTrackedMutation()
    {
        await using var fixture = await WriterFixture.CreateAsync();
        var seeded = await fixture.SeedOrderAsync(bound: Bound);
        var order = await fixture.Context.RegistrationOrders.SingleAsync(value => value.Id == seeded.Id);
        fixture.Context.Entry(order).Property(value => value.AnonymousPiiRetentionUntilUtc).CurrentValue = Bound.AddDays(1);
        await Assert.That(async () => await fixture.Context.SaveChangesAsync()).Throws<InvalidOperationException>();
        fixture.Context.ChangeTracker.Clear();
        var inventory = fixture.Services.GetRequiredService<IRegistrationInventoryRepository>();
        await Assert.That((await inventory.GetOrderWithLinesAsync(order.Id, fixture.TenantId, CancellationToken.None))!.AnonymousPiiRetentionUntilUtc).IsEqualTo(Bound);
    }

    private sealed class WriterFixture(EventVisitorCapabilitySqliteFixture inner, Clock clock) : IAsyncDisposable
    {
        public Guid TenantId => inner.TenantId;
        public Guid UserId => inner.UserId;
        public Guid ActorId => inner.ActorId;
        public Clock Clock => clock;
        public EventVisitorCapabilitySqliteFixture Inner => inner;
        public IServiceProvider Services => inner.Services;
        public ExploreDbContext Context => Services.GetRequiredService<ExploreDbContext>();
        public RegistrationParticipantCommandService Participants => Services.GetRequiredService<RegistrationParticipantCommandService>();

        public static async Task<WriterFixture> CreateAsync()
        {
            var clock = new Clock();
            var inner = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
            return new WriterFixture(inner, clock);
        }

        public async Task<RegistrationOrder> SeedOrderAsync(ParticipantDataCollectionModeEnum collection = ParticipantDataCollectionModeEnum.PerTicketRequired,
            DateTime? bound = null)
        {
            var target = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(), Title = "Retention event", TenantId = TenantId, Tenant = null!,
                ActorId = ActorId, Actor = null!, OrganizerActorId = ActorId,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
                SessionCount = 1, FirstSessionStartUtc = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero),
                LastSessionEndUtc = new DateTimeOffset(2027, 1, 1, 14, 0, 0, TimeSpan.Zero)
            };
            target.ParticipationConfiguration = EventParticipationConfiguration.Create(target.Id, TenantId,
                (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
                (int)IdentityAccessModeEnum.GuestAllowed, GuestRecoveryPolicyEnum.EmailOptional, Clock.Now.UtcDateTime);
            var catalog = EventTicketCatalogVersion.Create(TenantId, target.Id, "USD", 1);
            var ticket = EventTicketType.Create(Guid.CreateVersion7(), TenantId, catalog.Id, "Admission", "USD",
                TicketPricingModeEnum.Free, null, null, null, collection, null,
                null, null, false, false, null, null, null, null);
            catalog.AddTicketType(ticket, null);
            catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, TenantId, target.Id, 1));
            catalog.Publish();
            var order = RegistrationOrder.Create(TenantId, target.Id, null, null, BookingPartyTypeEnum.Individual,
                catalog.Id, RegistrationParticipationSnapshot.From(target.ParticipationConfiguration), null,
                CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                "USD", Clock.Now.UtcDateTime, Clock.Now.UtcDateTime.AddMinutes(15), bound);
            order.AddLine(RegistrationOrderLine.Create(Guid.CreateVersion7(), catalog, ticket, order.Id, 1, null, null));
            Context.AddRange(target, catalog, order);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return order;
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
