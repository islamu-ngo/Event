// ABOUTME: Relational model tests for EventLocation privacy mappings that do not require a live database.
// ABOUTME: Proves mapped audit columns remain PII-free and tenant/concurrency filters are present.

using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[Category("EventLocationPrivacy")]
public sealed class EventLocationPrivacyModelTests
{
    [Test]
    public async Task PrivacyAuditModel_ContainsOnlyPiiFreeColumnsAndTenantSafeRelationships()
    {
        await using var context = CreateContext();
        string[] forbiddenTokens =
        [
            "address", "postcode", "latitude", "longitude", "coordinate", "instruction", "door", "code",
            "location_name", "room_name", "city", "country"
        ];
        Type[] evidenceTypes =
        [
            typeof(EventLocationDisclosureAudit),
            typeof(EventLocationExactReadAudit),
            typeof(PrivacyErasureReplayCheckpoint)
        ];

        foreach (Type evidenceType in evidenceTypes)
        {
            var entityType = context.Model.FindEntityType(evidenceType)
                ?? throw new InvalidOperationException($"{evidenceType.Name} is not mapped.");
            string[] columnNames = entityType.GetProperties()
                .Select(property => property.GetColumnName())
                .OfType<string>()
                .ToArray();

            await Assert.That(entityType.GetProperties().Any(property => property.ClrType == typeof(string))).IsFalse();
            await Assert.That(columnNames.Any(column =>
                !column.Equals("reason_code", StringComparison.OrdinalIgnoreCase)
                && forbiddenTokens.Any(token => column.Contains(token, StringComparison.OrdinalIgnoreCase)))).IsFalse();
        }

        var eventLocationType = context.Model.FindEntityType(typeof(EventLocation))
            ?? throw new InvalidOperationException("EventLocation is not mapped.");
        await Assert.That(eventLocationType.GetDeclaredQueryFilters().Count()).IsEqualTo(2);
        await Assert.That(eventLocationType.FindProperty(nameof(EventLocation.ConcurrencyStamp))!.IsConcurrencyToken)
            .IsTrue();

        Type[] carrierTypes =
        [
            typeof(EventSession),
            typeof(EventSessionGroup),
            typeof(EventAgendaItem),
            typeof(EventSessionAgendaItem)
        ];
        foreach (Type carrierType in carrierTypes)
        {
            var entityType = context.Model.FindEntityType(carrierType)
                ?? throw new InvalidOperationException($"{carrierType.Name} is not mapped.");
            await Assert.That(entityType.FindAnnotation("EventLocationPrivacy:ConsistencyTrigger")?.Value)
                .IsNotNull();
            string[] consistencyProperties =
            [
                "TenantId",
                carrierType == typeof(EventSessionAgendaItem) ? "EventSessionId" : "EventId",
                "EventLocationId",
                "LocationId"
            ];
            IIndex consistencyIndex = entityType.GetIndexes().Single(index =>
                index.Properties.Select(property => property.Name).SequenceEqual(consistencyProperties));
            await Assert.That(consistencyIndex.GetFilter()).IsNull();
        }
    }

    private static ExploreDbContext CreateContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=event_location_privacy_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }
}
