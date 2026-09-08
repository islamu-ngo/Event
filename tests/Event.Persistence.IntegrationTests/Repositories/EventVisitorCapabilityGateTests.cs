// ABOUTME: Exercises visitor capability bypasses through native event and registration commands on SQLite.
// ABOUTME: Verifies rejected requests leave event configuration, publication outbox and inventory unchanged.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.EventParticipation.Requests.Commands;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventVisitorCapabilityGateTests
{
    [Test]
    [Arguments("configure", false)]
    [Arguments("draft", false)]
    [Arguments("import", false)]
    [Arguments("create-draft", false)]
    [Arguments("create-published", false)]
    [Arguments("publish", false)]
    [Arguments("approve-publish", false)]
    [Arguments("configure", true)]
    [Arguments("draft", true)]
    [Arguments("import", true)]
    [Arguments("create-draft", true)]
    [Arguments("create-published", true)]
    [Arguments("publish", true)]
    [Arguments("approve-publish", true)]
    public async Task AccountRequiredCommands_RequireAllowedPublicOnboardingEvenWithLocalLogin(string command, bool onboardingAllowed)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        if (onboardingAllowed)
        {
            var changed = await fixture.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                [new(null, GovernanceSettingKeys.Authentication.GoogleSsoEnabled, VisitorAccessSettingMutationKind.SetValue, "true"),
                 new(null, GovernanceSettingKeys.Authentication.GoogleClientId, VisitorAccessSettingMutationKind.SetValue, "\"public-client\""),
                 new(null, GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy, VisitorAccessSettingMutationKind.SetValue, "\"Allowed\""),
                 new(null, GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, VisitorAccessSettingMutationKind.SetValue, "\"https://accounts.example.test/signup\"")], fixture.UserId);
            await Assert.That(changed.Success).IsTrue();
        }
        var entity = await fixture.SeedEventAsync(command is "publish" or "approve-publish");
        Guid configurationStamp = entity.ParticipationConfiguration!.ConcurrencyStamp;
        int eventCount = await fixture.Context.Events.CountAsync();
        var participation = EventVisitorCapabilitySqliteFixture.Participation();
        BaseCommandResponse<Guid> response = command switch
        {
            "configure" => await fixture.ExecuteAsync<ConfigureEventParticipationCommand, BaseCommandResponse<Guid>>(new()
            {
                EventId = entity.Id, ExpectedConcurrencyStamp = configurationStamp,
                ParticipationConfiguration = participation
            }),
            "draft" => await fixture.ExecuteAsync<UpdateEventDraftCommand, BaseCommandResponse<Guid>>(new()
            {
                Id = entity.Id,
                Draft = new UpdateEventDraftRequestDto
                {
                    Title = "Rejected draft update", ExpectedConcurrencyStamp = entity.ConcurrencyStamp,
                    ExpectedParticipationConfigurationConcurrencyStamp = configurationStamp,
                    ParticipationConfiguration = participation
                }
            }),
            "import" => await fixture.ExecuteAsync<ImportEventCommand, BaseCommandResponse<Guid>>(new()
            {
                TenantId = fixture.TenantId,
                Request = new ImportEventRequestDto
                {
                    Title = "Rejected imported setup", OwnerActorId = fixture.ActorId,
                    ProvenanceSource = "visitor-test", ProvenanceExternalId = "external-event",
                    ParticipationConfiguration = participation
                }
            }),
            "create-draft" or "create-published" => await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(new()
            {
                EventDto = new CreateEventDto
                {
                    Title = "Rejected created setup", ParticipationConfiguration = participation,
                    EventStatusId = (int)(command == "create-published" ? EventStatusEnum.Published : EventStatusEnum.Draft),
                    Sessions = [new CreateEventGraphSessionDto
                    {
                        StartTime = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero),
                        EndTime = new DateTimeOffset(2027, 1, 1, 13, 0, 0, TimeSpan.Zero)
                    }]
                }
            }),
            "publish" => await fixture.ExecuteAsync<PublishEventCommand, BaseCommandResponse<Guid>>(new()
            {
                Id = entity.Id, Request = new PublishEventRequestDto { ExpectedConcurrencyStamp = entity.ConcurrencyStamp }
            }),
            _ => await fixture.ExecuteAsync<ApprovePublishEventCommand, BaseCommandResponse<Guid>>(new()
            {
                Id = entity.Id, Request = new PublishEventRequestDto { ExpectedConcurrencyStamp = entity.ConcurrencyStamp }
            })
        };

        if (onboardingAllowed)
        {
            await Assert.That(response.IsSuccess).IsTrue().Because($"{response.FailureCode}: {string.Join(", ", response.Errors ?? [])}");
            fixture.Context.ChangeTracker.Clear();
            var accepted = await fixture.Context.Events.Include(value => value.ParticipationConfiguration)
                .SingleAsync(value => value.Id == response.Id);
            await Assert.That(accepted.ParticipationConfiguration!.IdentityAccessModeId).IsEqualTo((int)IdentityAccessModeEnum.AccountRequired);
            bool published = command is "create-published" or "publish" or "approve-publish";
            await Assert.That(accepted.EventStatusId).IsEqualTo((int)(published ? EventStatusEnum.Published : EventStatusEnum.Draft));
            await Assert.That(await fixture.Context.OutboxMessages.CountAsync()).IsEqualTo(published ? 1 : 0);
            return;
        }

        await Assert.That(response.FailureCode).IsEqualTo("event_visitor_account_onboarding_required");
        fixture.Context.ChangeTracker.Clear();
        var stored = await fixture.Context.Events.Include(value => value.ParticipationConfiguration).SingleAsync(value => value.Id == entity.Id);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(eventCount);
        await Assert.That(stored.ConcurrencyStamp).IsEqualTo(entity.ConcurrencyStamp);
        await Assert.That(stored.ParticipationConfiguration!.ConcurrencyStamp).IsEqualTo(configurationStamp);
        await Assert.That(stored.EventStatusId).IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(await fixture.Context.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("direct")]
    [Arguments("authenticated")]
    [Arguments("guest")]
    public async Task DirectoryOnly_AllNativeStartersRejectNewAllocationWithoutChangingExistingHolds(string surface)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = await fixture.SeedEventAsync();
        var ticket = await fixture.SeedTicketAsync(entity.Id);
        async Task<BaseCommandResponse<Guid>> StartAsync() => surface switch
        {
            "authenticated" => await fixture.ExecuteAsync<StartAuthenticatedRegistrationOrderCommand, BaseCommandResponse<Guid>>(
                new(entity.Id, ticket.CatalogId, BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)])),
            "guest" => await fixture.ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(
                new(entity.Id, ticket.CatalogId, BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)])),
            _ => await fixture.ExecuteAsync<CreateRegistrationOrderWithHoldCommand, BaseCommandResponse<Guid>>(new()
            {
                EventId = entity.Id, TicketCatalogVersionId = ticket.CatalogId, AccountUserId = fixture.UserId,
                BookingPartyType = BookingPartyTypeEnum.Individual, Lines = [new(ticket.TicketId, 1, null)]
            })
        };
        var existing = await StartAsync();
        await Assert.That(existing.IsSuccess).IsTrue();
        var settings = fixture.Services.GetRequiredService<IVisitorAccessSettingsWriter>();
        var changed = await settings.ApplyAsync(
            [new(null, GovernanceSettingKeys.PublicExperience.VisitorAccessMode, VisitorAccessSettingMutationKind.SetValue,
                "\"DirectoryListingOnly\"")], fixture.UserId);
        await Assert.That(changed.Success).IsTrue();

        var response = await StartAsync();

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("registration_order_visitor_allocation_disabled");
        await Assert.That(await fixture.Context.RegistrationOrders.CountAsync()).IsEqualTo(1);
        var hold = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await Assert.That(hold.RegistrationOrderId).IsEqualTo(existing.Id);
        await Assert.That(hold.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Active);

        BaseCommandResponse<Guid> cancelled = existing is GuestRegistrationOrderStartDto guest
            ? await fixture.ExecuteAsync<CancelGuestRegistrationOrderCommand, GuestRegistrationOrderLifecycleResponseDto>(
                new(entity.Id, existing.Id, guest.GuestCapabilityToken))
            : await fixture.ExecuteAsync<CancelAuthenticatedRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>(
                new(entity.Id, existing.Id));
        await Assert.That(cancelled.IsSuccess).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        var released = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await Assert.That(released.RegistrationInventoryHoldStatusId).IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Cancelled);
    }

    [Test]
    [Arguments(ParticipationHandlingModeEnum.WalkIn)]
    [Arguments(ParticipationHandlingModeEnum.ExternalManaged)]
    public async Task DirectoryOnly_DoesNotRestrictWalkInOrExternalConfiguration(ParticipationHandlingModeEnum mode)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = await fixture.SeedEventAsync();
        var changed = await fixture.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
            [new(null, GovernanceSettingKeys.PublicExperience.VisitorAccessMode, VisitorAccessSettingMutationKind.SetValue,
                "\"DirectoryListingOnly\"")], fixture.UserId);
        await Assert.That(changed.Success).IsTrue();
        var response = await fixture.ExecuteAsync<ConfigureEventParticipationCommand, BaseCommandResponse<Guid>>(new()
        {
            EventId = entity.Id, ExpectedConcurrencyStamp = entity.ParticipationConfiguration!.ConcurrencyStamp,
            ParticipationConfiguration = new ConfigureEventParticipationDto
            {
                ParticipationHandlingModeId = (int)mode,
                AdvanceRegistrationObligationId = (int)(mode == ParticipationHandlingModeEnum.WalkIn
                    ? AdvanceRegistrationObligationEnum.NotApplicable : AdvanceRegistrationObligationEnum.Required)
            }
        });
        await Assert.That(response.IsSuccess).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        var stored = await fixture.Context.EventParticipationConfigurations.SingleAsync(value => value.Id == entity.Id);
        await Assert.That(stored.ParticipationHandlingModeId).IsEqualTo((int)mode);
    }

    [Test]
    public async Task UnknownExternalOnboarding_DoesNotAuthorizeAccountRequiredConfiguration()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var changed = await fixture.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
            [new(null, GovernanceSettingKeys.Authentication.GoogleSsoEnabled, VisitorAccessSettingMutationKind.SetValue, "true"),
             new(null, GovernanceSettingKeys.Authentication.GoogleClientId, VisitorAccessSettingMutationKind.SetValue, "\"public-client\""),
             new(null, GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, VisitorAccessSettingMutationKind.SetValue, "\"https://accounts.example.test/signup\"")], fixture.UserId);
        await Assert.That(changed.Success).IsTrue();
        var entity = await fixture.SeedEventAsync();
        var response = await fixture.ExecuteAsync<ConfigureEventParticipationCommand, BaseCommandResponse<Guid>>(new()
        {
            EventId = entity.Id, ExpectedConcurrencyStamp = entity.ParticipationConfiguration!.ConcurrencyStamp,
            ParticipationConfiguration = EventVisitorCapabilitySqliteFixture.Participation()
        });
        await Assert.That(response.FailureCode).IsEqualTo("event_visitor_account_onboarding_required");
    }
}
