using System.Text.Json;
using Explore.Application.Features.EventAggregateViews;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Views;
using Microsoft.Extensions.Logging;

namespace Event.Application.UnitTests.Features.EventAggregateViews;

public sealed class AggregateFacetDiagnosticConfidentialityTests
{
    [Test]
    public async Task MalformedFacetProjectionPreservesEmptyFallbackWithoutPrivateParserException()
    {
        var canary = $"{Guid.NewGuid():N}\r\nprivate-facet";
        var logger = new Collector();
        var view = View($"{{{JsonSerializer.Serialize(canary)}: [");

        var result = EventAggregateViewMapper.MapDetail(view, [], [], ExposureLevel.Public, logger);

        await Assert.That(result.EventId).IsEqualTo(view.EventId);
        await Assert.That(result.Title).IsEqualTo(view.Title);
        await Assert.That(result.EventCustomProperties.Count).IsEqualTo(0);
        await Assert.That(result.EventSessionCustomProperties.Count).IsEqualTo(0);
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await Assert.That(logger.Entries.Single().Exception).IsNull();
        await Assert.That(logger.Entries.Single().Message.Contains("aggregate_facet_json_invalid", StringComparison.Ordinal)).IsTrue();
        await Assert.That(JsonSerializer.Serialize(logger.Entries).Contains(canary[..32], StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ValidFacetRemainsBusinessDataWithoutDiagnosticDuplication()
    {
        var canary = Guid.NewGuid().ToString("N");
        var logger = new Collector();
        var view = View($"{{\"test/value\":[{JsonSerializer.Serialize(canary)}]}}");
        var definition = new EventCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventId = view.EventId,
            TenantId = view.TenantId,
            Namespace = "test",
            Key = "value",
            DisplayName = "Value",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public
        };

        var result = EventAggregateViewMapper.MapDetail(view, [definition], [], ExposureLevel.Public, logger);

        await Assert.That(result.EventCustomProperties.Count).IsEqualTo(1);
        await Assert.That(JsonSerializer.Serialize(result.EventCustomProperties).Contains(canary, StringComparison.Ordinal)).IsTrue();
        await Assert.That(logger.Entries.Count).IsEqualTo(0);
    }

    private static EventWithSessionsView View(string json) => new()
    {
        EventId = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Title = "Aggregate",
        Slug = "aggregate",
        Status = "Published",
        Visibility = "Public",
        EventCustomPropertyFacets = json,
        EventSessionCustomPropertyFacets = "{}"
    };

    private sealed class Collector : ILogger
    {
        public List<Export> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Export(formatter(state, exception), exception?.ToString(),
                state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToArray() : []));
    }

    private sealed record Export(string Message, string? Exception, KeyValuePair<string, object?>[] State);
}
