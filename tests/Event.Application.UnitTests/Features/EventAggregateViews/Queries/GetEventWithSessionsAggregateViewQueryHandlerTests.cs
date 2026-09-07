using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventAggregateViews.Handlers.Queries;
using Explore.Application.Features.EventAggregateViews.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Views;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventAggregateViews.Queries;

public class GetEventWithSessionsAggregateViewQueryHandlerTests
{
    private readonly IEventAggregateViewRepository _repository;
    private readonly HybridCache _cache;
    private readonly ILogger<GetEventWithSessionsAggregateViewQueryHandler> _logger;
    private readonly GetEventWithSessionsAggregateViewQueryHandler _handler;

    public GetEventWithSessionsAggregateViewQueryHandlerTests()
    {
        _repository = Substitute.For<IEventAggregateViewRepository>();
        _cache = new TestHybridCache();
        _logger = Substitute.For<ILogger<GetEventWithSessionsAggregateViewQueryHandler>>();
        _handler = new GetEventWithSessionsAggregateViewQueryHandler(_repository, _cache, _logger);
    }

    [Test]
    public async Task Handle_WhenEventMissing_ReturnsErrorEnvelope()
    {
        var eventId = Guid.NewGuid();
        _repository.GetByEventIdAsync(eventId, Arg.Any<CancellationToken>()).Returns((EventWithSessionsView?)null);

        var result = await _handler.Handle(new GetEventWithSessionsAggregateViewQuery(eventId, ExposureLevel.Public), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Id).IsNull();
        await Assert.That(result.Errors).IsNotNull();
    }

    [Test]
    public async Task Handle_PublicCeiling_ReturnsOnlyPublicFacets()
    {
        var eventId = Guid.NewGuid();
        ConfigureViewAndDefinitions(
            eventId,
            CreateEventDefinitions(eventId, ExposureLevel.Public, ExposureLevel.Internal),
            CreateSessionDefinitions(eventId, ExposureLevel.Public, ExposureLevel.Internal));

        var result = await _handler.Handle(new GetEventWithSessionsAggregateViewQuery(eventId, ExposureLevel.Public), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.EventCustomProperties.Count).IsEqualTo(1);
        await Assert.That(result.Id.EventCustomProperties[0].Key).IsEqualTo("public-facet");
        await Assert.That(result.Id.EventSessionCustomProperties.Count).IsEqualTo(1);
        await Assert.That(result.Id.EventSessionCustomProperties[0].Key).IsEqualTo("public-session-facet");
    }

    [Test]
    public async Task Handle_PublicCeiling_ReturnsOnlyPublicExportableFacetsWithGovernanceFlags()
    {
        var eventId = Guid.NewGuid();
        ConfigureViewAndDefinitions(
            eventId,
            [
                CreateEventDefinition(eventId, "tenant.custom", "public-facet", ExposureLevel.Public, isExportable: true, isModerationRelevant: true),
                CreateEventDefinition(eventId, "tenant.custom", "internal-facet", ExposureLevel.Internal, isExportable: true, isModerationRelevant: true)
            ],
            [
                CreateSessionDefinition(eventId, "tenant.session", "public-session-facet", ExposureLevel.Public, isExportable: true, isModerationRelevant: true),
                CreateSessionDefinition(eventId, "tenant.session", "internal-session-facet", ExposureLevel.Internal, isExportable: true, isModerationRelevant: true)
            ]);

        var result = await _handler.Handle(new GetEventWithSessionsAggregateViewQuery(eventId, ExposureLevel.Public), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        var eventFacet = result.Id!.EventCustomProperties.Single();
        await Assert.That(eventFacet.Key).IsEqualTo("public-facet");
        await Assert.That(eventFacet.IsExportable).IsTrue();
        await Assert.That(eventFacet.IsModerationRelevant).IsTrue();
        await Assert.That(eventFacet.ExposureLevel).IsEqualTo(ExposureLevel.Public);

        var sessionFacet = result.Id.EventSessionCustomProperties.Single();
        await Assert.That(sessionFacet.Key).IsEqualTo("public-session-facet");
        await Assert.That(sessionFacet.IsExportable).IsTrue();
        await Assert.That(sessionFacet.IsModerationRelevant).IsTrue();
        await Assert.That(sessionFacet.ExposureLevel).IsEqualTo(ExposureLevel.Public);
    }

    [Test]
    public async Task Handle_TenantAdminOnlyCeiling_ReturnsPublicAndTenantAdminOnlyFacets()
    {
        var eventId = Guid.NewGuid();
        ConfigureViewAndDefinitions(
            eventId,
            [
                CreateEventDefinition(eventId, "tenant.custom", "public-facet", ExposureLevel.Public),
                CreateEventDefinition(eventId, "tenant.custom", "tenant-admin-facet", ExposureLevel.TenantAdminOnly),
                CreateEventDefinition(eventId, "tenant.custom", "organizer-facet", ExposureLevel.OrganizerOnly)
            ],
            []);
        _repository.GetByEventIdAsync(eventId, Arg.Any<CancellationToken>()).Returns(
            CreateView(
                eventId,
                eventFacetJson: "{\"tenant.custom/public-facet\":[\"public\"],\"tenant.custom/tenant-admin-facet\":[\"tenant-admin\"],\"tenant.custom/organizer-facet\":[\"organizer\"]}",
                sessionFacetJson: "{}"));

        var result = await _handler.Handle(new GetEventWithSessionsAggregateViewQuery(eventId, ExposureLevel.TenantAdminOnly), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.EventCustomProperties.Count).IsEqualTo(2);
        await Assert.That(result.Id.EventCustomProperties.Select(x => x.Key).ToArray())
            .IsEquivalentTo(["public-facet", "tenant-admin-facet"]);
    }

    [Test]
    public async Task Handle_InternalCeiling_ReturnsAllFacets()
    {
        var eventId = Guid.NewGuid();
        ConfigureViewAndDefinitions(
            eventId,
            CreateEventDefinitions(eventId, ExposureLevel.Public, ExposureLevel.Internal),
            CreateSessionDefinitions(eventId, ExposureLevel.Public, ExposureLevel.Internal));

        var result = await _handler.Handle(new GetEventWithSessionsAggregateViewQuery(eventId, ExposureLevel.Internal), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.EventCustomProperties.Count).IsEqualTo(2);
        await Assert.That(result.Id.EventSessionCustomProperties.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Handle_JsonDeserializationRoundTrip_PreservesTypedValues()
    {
        var eventId = Guid.NewGuid();
        var view = CreateView(
            eventId,
            eventFacetJson: "{\"tenant.custom/public-facet\":[\"text\",42,true,\"2026-04-24T10:00:00+00:00\"]}",
            sessionFacetJson: "{}");

        _repository.GetByEventIdAsync(eventId, Arg.Any<CancellationToken>()).Returns(view);
        _repository.GetEventDefinitionsByEventIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([CreateEventDefinition(eventId, "tenant.custom", "public-facet", ExposureLevel.Public)]);
        _repository.GetSessionDefinitionsForEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await _handler.Handle(new GetEventWithSessionsAggregateViewQuery(eventId, ExposureLevel.Internal), CancellationToken.None);

        var values = result.Id!.EventCustomProperties[0].Values;
        await Assert.That(values.Count).IsEqualTo(4);
        await Assert.That(values[0].GetString()).IsEqualTo("text");
        await Assert.That(values[1].GetInt32()).IsEqualTo(42);
        await Assert.That(values[2].GetBoolean()).IsTrue();
        await Assert.That(values[3].GetString()).IsEqualTo("2026-04-24T10:00:00+00:00");
    }

    [Test]
    public async Task Handle_WhenIslamicColumnsNull_LeavesNullableFieldsNull()
    {
        var eventId = Guid.NewGuid();
        var view = CreateView(eventId, eventFacetJson: "{}", sessionFacetJson: "{}", islamicTheme: null, isRamadan: null, targetAudience: null);

        _repository.GetByEventIdAsync(eventId, Arg.Any<CancellationToken>()).Returns(view);
        _repository.GetEventDefinitionsByEventIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _repository.GetSessionDefinitionsForEventAsync(eventId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await _handler.Handle(new GetEventWithSessionsAggregateViewQuery(eventId, ExposureLevel.Public), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.IslamicTheme).IsNull();
        await Assert.That(result.Id.IsRamadan).IsNull();
        await Assert.That(result.Id.TargetAudience).IsNull();
    }

    private void ConfigureViewAndDefinitions(
        Guid eventId,
        List<EventCustomPropertyDefinition> eventDefinitions,
        List<EventSessionCustomPropertyDefinition> sessionDefinitions)
    {
        _repository.GetByEventIdAsync(eventId, Arg.Any<CancellationToken>()).Returns(CreateView(eventId));
        _repository.GetEventDefinitionsByEventIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(eventDefinitions);
        _repository.GetSessionDefinitionsForEventAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(sessionDefinitions);
    }

    private static EventWithSessionsView CreateView(
        Guid eventId,
        string? eventFacetJson = null,
        string? sessionFacetJson = null,
        string? islamicTheme = "theme",
        bool? isRamadan = true,
        string? targetAudience = "audience")
    {
        return new EventWithSessionsView
        {
            EventId = eventId,
            TenantId = Guid.NewGuid(),
            Title = "Aggregate Event",
            Slug = "aggregate-event",
            Description = "desc",
            StartAt = new DateTimeOffset(2026, 4, 24, 8, 0, 0, TimeSpan.Zero),
            EndAt = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero),
            Status = "Published",
            Visibility = "Public",
            IsDeleted = false,
            CreatedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
            IslamicTheme = islamicTheme,
            Madhab = "hanafi",
            IsRamadan = isRamadan,
            PrayerAware = true,
            TechStack = "dotnet",
            DifficultyLevel = "Beginner",
            TargetAudience = targetAudience,
            SessionCount = 2,
            FirstSessionStartAt = new DateTimeOffset(2026, 4, 24, 8, 0, 0, TimeSpan.Zero),
            LastSessionEndAt = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero),
            HasInPersonSessions = true,
            HasVirtualSessions = true,
            AggregatedSessionIslamicThemes = null,
            EventCustomPropertyFacets = eventFacetJson ?? "{\"tenant.custom/public-facet\":[\"public\"],\"tenant.custom/internal-facet\":[\"internal\"]}",
            EventSessionCustomPropertyFacets = sessionFacetJson ?? "{\"tenant.session/public-session-facet\":[\"public-session\"],\"tenant.session/internal-session-facet\":[\"internal-session\"]}"
        };
    }

    private static List<EventCustomPropertyDefinition> CreateEventDefinitions(Guid eventId, ExposureLevel publicExposure, ExposureLevel internalExposure)
        =>
        [
            CreateEventDefinition(eventId, "tenant.custom", "public-facet", publicExposure),
            CreateEventDefinition(eventId, "tenant.custom", "internal-facet", internalExposure)
        ];

    private static EventCustomPropertyDefinition CreateEventDefinition(
        Guid eventId,
        string namespaceValue,
        string key,
        ExposureLevel exposureLevel,
        bool isExportable = true,
        bool isModerationRelevant = false)
        => new()
        {
            Id = Guid.NewGuid(),
            ConcurrencyStamp = Guid.NewGuid(),
            EventId = eventId,
            TenantId = Guid.NewGuid(),
            Namespace = namespaceValue,
            Key = key,
            DisplayName = key,
            PropertyType = PropertyType.Text,
            IsRequired = false,
            IsMulti = true,
            IsActive = true,
            SortOrder = 1,
            ExposureLevel = exposureLevel,
            IsSearchable = true,
            IsFilterable = true,
            IsExportable = isExportable,
            IsModerationRelevant = isModerationRelevant,
            IsAnalyticsRelevant = false,
            IsSystemOwned = false,
            InstantiatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

    private static List<EventSessionCustomPropertyDefinition> CreateSessionDefinitions(Guid eventId, ExposureLevel publicExposure, ExposureLevel internalExposure)
        =>
        [
            CreateSessionDefinition(eventId, "tenant.session", "public-session-facet", publicExposure),
            CreateSessionDefinition(eventId, "tenant.session", "internal-session-facet", internalExposure)
        ];

    private static EventSessionCustomPropertyDefinition CreateSessionDefinition(
        Guid eventId,
        string namespaceValue,
        string key,
        ExposureLevel exposureLevel,
        bool isExportable = true,
        bool isModerationRelevant = false)
        => new()
        {
            Id = Guid.NewGuid(),
            ConcurrencyStamp = Guid.NewGuid(),
            EventSessionId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Namespace = namespaceValue,
            Key = key,
            DisplayName = key,
            PropertyType = PropertyType.Text,
            IsRequired = false,
            IsMulti = true,
            IsActive = true,
            SortOrder = 1,
            ExposureLevel = exposureLevel,
            IsSearchable = true,
            IsFilterable = true,
            IsExportable = isExportable,
            IsModerationRelevant = isModerationRelevant,
            IsAnalyticsRelevant = false,
            IsSystemOwned = false,
            InstantiatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

    private sealed class TestHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => factory(state, cancellationToken);

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
