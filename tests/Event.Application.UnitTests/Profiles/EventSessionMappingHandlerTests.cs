using Explore.Application;
using Event.Application.UnitTests.Operations;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionAgendaItems.Handlers.Commands;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Features.EventSessionGroups.Handlers.Commands;
using Explore.Application.Features.EventSessionGroups.Requests.Commands;
using Explore.Application.Features.EventSessionLanguages.Handlers.Commands;
using Explore.Application.Features.EventSessionLanguages.Handlers.Queries;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Features.EventSessionSpeakers.Handlers.Commands;
using Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

[Category("EventSessionMapping")]
public sealed class EventSessionMappingHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid SessionId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly Guid CreatedId = Guid.Parse("01900000-0000-7000-8000-000000000004");
    private static readonly DateTimeOffset Start = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CreateLanguage_OnlyCopiesRelationshipKeysAndUsesContextTenant()
    {
        var repository = Substitute.For<IEventSessionLanguageRepository>();
        var sessions = Substitute.For<IEventSessionRepository>();
        var languages = Substitute.For<ILanguageRepository>();
        var session = Session();
        sessions.GetSessionWithDetails(SessionId, Arg.Any<CancellationToken>()).Returns(session);
        var events = Substitute.For<IEventRepository>();
        events.GetEventWithDetails(EventId, Arg.Any<CancellationToken>()).Returns(session.Event);
        sessions.Exists(SessionId).Returns(true);
        languages.Exists(7).Returns(true);
        EventSessionLanguage? stored = null;
        repository.Create(Arg.Any<EventSessionLanguage>()).Returns(call =>
        {
            stored = call.Arg<EventSessionLanguage>() ?? throw new InvalidOperationException("Expected language at persistence boundary.");
            stored.Id = 42;
            return stored;
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IMachinePrincipalAccessor>());
        services.AddSingleton(repository);
        services.AddSingleton(sessions);
        services.AddSingleton(languages);
        services.AddSingleton(events);
        services.AddScoped<AuthorizationResourceContextResolver>();
        services.AddSingleton(Tenant());
        services.AddSingleton<IAuthorizationProvider>(new OperationAuthorizationTests.Policy(
            AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)));
        services.AddNativeOperations([typeof(CreateEventSessionLanguageCommand), typeof(CreateEventSessionLanguageCommandHandler)]);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventSessionLanguageCommand, BaseCommandResponse<int>>>();
        var result = await handler.ExecuteAsync(new CreateEventSessionLanguageCommand
        {
            EventSessionLanguageDto = new CreateEventSessionLanguageDto { EventSessionId = SessionId, LanguageId = 7 }
        }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(42);
        await Assert.That(stored!.TenantId).IsEqualTo(TenantId);
        await Assert.That(stored.EventSessionId).IsEqualTo(SessionId);
        await Assert.That(stored.LanguageId).IsEqualTo(7);
        await Assert.That(stored.ConcurrencyStamp).IsEqualTo(Guid.Empty);
        await Assert.That(stored.Language).IsNull();
        await Assert.That(stored.EventSession).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateSpeaker_PreservesParentTenantFence(bool crossTenant)
    {
        var repository = Substitute.For<IEventSessionSpeakerRepository>();
        var sessions = Substitute.For<IEventSessionRepository>();
        var actors = Substitute.For<IActorRepository>();
        var session = Session();
        if (crossTenant)
            session.TenantId = EventId;
        sessions.Exists(SessionId).Returns(true);
        sessions.GetById(SessionId).Returns(session);
        actors.Exists(CreatedId).Returns(true);
        actors.GetById(CreatedId).Returns(new Actor { Id = CreatedId, ActorType = null!, Pii = null! });
        EventSessionSpeaker? stored = null;
        repository.Create(Arg.Any<EventSessionSpeaker>()).Returns(call =>
        {
            stored = call.Arg<EventSessionSpeaker>() ?? throw new InvalidOperationException("Expected speaker at persistence boundary.");
            stored.Id = CreatedId;
            return stored;
        });
        var cache = Substitute.For<HybridCache>();
        var handler = new CreateEventSessionSpeakerCommandHandler(repository, actors, sessions, Tenant(), cache);
        var result = await handler.ExecuteAsync(new CreateEventSessionSpeakerCommand
        {
            SpeakerDto = new CreateEventSessionSpeakerDto { ActorId = CreatedId, EventSessionId = SessionId }
        }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsEqualTo(!crossTenant);
        if (crossTenant)
        {
            await Assert.That(stored).IsNull();
            return;
        }
        await Assert.That(stored!.TenantId).IsEqualTo(TenantId);
        await Assert.That(stored.EventSessionId).IsEqualTo(SessionId);
        await Assert.That(stored.ActorId).IsEqualTo(CreatedId);
        await Assert.That(stored.ConcurrencyStamp).IsEqualTo(Guid.Empty);
        await Assert.That(stored.Actor).IsNull();
    }

    [Test]
    public async Task CreateGroup_PreservesBusinessFieldsWithoutCopyingParentGraph()
    {
        var repository = Substitute.For<IEventSessionGroupRepository>();
        var events = Substitute.For<IEventRepository>();
        events.Exists(EventId).Returns(true);
        events.GetById(EventId).Returns(Session().Event);
        repository.GetActiveByEventAsync(EventId, Arg.Any<CancellationToken>()).Returns([]);
        EventSessionGroup? stored = null;
        repository.Create(Arg.Any<EventSessionGroup>()).Returns(call =>
        {
            stored = call.Arg<EventSessionGroup>() ?? throw new InvalidOperationException("Expected group at persistence boundary.");
            stored.Id = CreatedId;
            return stored;
        });
        var handler = new CreateEventSessionGroupCommandHandler(repository, events,
            Substitute.For<ILocationRepository>(), Substitute.For<ILocationRoomRepository>(), new InlineUnitOfWork(), Attachment());
        var result = await handler.ExecuteAsync(new CreateEventSessionGroupCommand
        {
            EventSessionGroup = new CreateEventSessionGroupRequestDto
            {
                EventId = EventId,
                Name = "Track",
                Slug = "track",
                Description = "Notes",
                Color = "blue",
                SortOrder = 2,
                IsPublished = true
            }
        }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(stored!.TenantId).IsEqualTo(TenantId);
        await Assert.That(stored.Name).IsEqualTo("Track");
        await Assert.That(stored.Slug).IsEqualTo("track");
        await Assert.That(stored.Description).IsEqualTo("Notes");
        await Assert.That(stored.Color).IsEqualTo("blue");
        await Assert.That(stored.IsPublished).IsTrue();
        await Assert.That(stored.SortOrder).IsEqualTo(2);
        await Assert.That(stored.Event).IsNull();
        await Assert.That(stored.Sessions).IsEmpty();
        await Assert.That(stored.CreatedBy).IsNull();
        await Assert.That(stored.ConcurrencyStamp).IsEqualTo(Guid.Empty);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreateAgenda_PreservesScheduleAndRejectsForeignTenant(bool crossTenant)
    {
        var repository = Substitute.For<IEventSessionAgendaItemRepository>();
        var sessions = Substitute.For<IEventSessionRepository>();
        var session = Session();
        if (crossTenant)
            session.TenantId = EventId;
        sessions.Exists(SessionId).Returns(true);
        sessions.GetById(SessionId).Returns(session);
        EventSessionAgendaItem? stored = null;
        repository.Create(Arg.Any<EventSessionAgendaItem>()).Returns(call =>
        {
            stored = call.Arg<EventSessionAgendaItem>() ?? throw new InvalidOperationException("Expected agenda at persistence boundary.");
            stored.Id = CreatedId;
            return stored;
        });
        var handler = new CreateEventSessionAgendaItemCommandHandler(repository, sessions,
            Substitute.For<ILocationRepository>(), Tenant(), new InlineUnitOfWork(), Attachment());
        var result = await handler.ExecuteAsync(new CreateEventSessionAgendaItemCommand
        {
            AgendaItemDto = new CreateEventSessionAgendaItemDto
            {
                EventSessionId = SessionId,
                Title = "Agenda",
                Description = "Notes",
                StartTime = Start,
                EndTime = Start.AddMinutes(20)
            }
        }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsEqualTo(!crossTenant);
        if (crossTenant)
        {
            await Assert.That(stored).IsNull();
            return;
        }
        await Assert.That(stored!.TenantId).IsEqualTo(TenantId);
        await Assert.That(stored.EventSessionId).IsEqualTo(SessionId);
        await Assert.That(stored.Title).IsEqualTo("Agenda");
        await Assert.That(stored.Description).IsEqualTo("Notes");
        await Assert.That(stored.StartTime).IsEqualTo(Start);
        await Assert.That(stored.EndTime).IsEqualTo(Start.AddMinutes(20));
        await Assert.That(stored.EventSession).IsSameReferenceAs(session);
        await Assert.That(stored.EventLocation!.EventId).IsEqualTo(EventId);
    }

    [Test]
    public async Task MissingSpeakerAndLanguageDetails_KeepNullResult()
    {
        var speakers = Substitute.For<IEventSessionSpeakerRepository>();
        var languages = Substitute.For<IEventSessionLanguageRepository>();
        var sessions = Substitute.For<IEventSessionRepository>();
        var speaker = await new GetEventSessionSpeakerDetailsQueryHandler(speakers)
            .QueryAsync(new GetEventSessionSpeakerDetailsQuery { Id = CreatedId }, CancellationToken.None);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(languages);
        services.AddSingleton(Substitute.For<IMachinePrincipalAccessor>());
        services.AddSingleton(sessions);
        services.AddSingleton(Substitute.For<IAuthorizationProvider>());
        services.AddNativeOperations([typeof(GetEventSessionLanguageDetailsQuery), typeof(GetEventSessionLanguageDetailsQueryHandler)]);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var language = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionLanguageDetailsQuery, EventSessionLanguageDto?>>()
            .QueryAsync(new GetEventSessionLanguageDetailsQuery { Id = 7 }, CancellationToken.None);
        await Assert.That(speaker).IsNull();
        await Assert.That(language).IsNull();
    }

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static EventLocationAttachmentService Attachment()
    {
        var repository = Substitute.For<IEventLocationRepository>();
        var location = EventLocation.CreateToBeAnnounced(TenantId, EventId, CreatedId, Start.UtcDateTime);
        repository.FindActiveToBeAnnouncedAsync(EventId, Arg.Any<CancellationToken>()).Returns(location);
        repository.GetForUpdateAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);
        return new EventLocationAttachmentService(repository, Substitute.For<IUserContext>(), Tenant(), TimeProvider.System);
    }

    private static EventSession Session() => new()
    {
        Id = SessionId,
        EventId = EventId,
        TenantId = TenantId,
        Tenant = null!,
        Event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = EventId,
            Title = "Parent",
            TenantId = TenantId,
            Tenant = null!,
            Actor = null!,
            EventStatus = null!,
            EventFormat = null!,
            VisibilityType = null!
        }
    };

    // Execute actual handler transaction delegates in memory; persistence isolation is not under test.
    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
    }
}
