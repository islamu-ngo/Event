using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Profiles;

public sealed class LocationMapperTests
{
    private static readonly Guid LocationId = Guid.Parse("01900000-0000-7000-8000-000000000081");
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000082");
    private static readonly Guid Stamp = Guid.Parse("01900000-0000-7000-8000-000000000083");
    private static readonly Guid OwnerId = Guid.Parse("01900000-0000-7000-8000-000000000084");

    [Test]
    [Arguments(false, "provider")]
    [Arguments(false, "zero")]
    [Arguments(false, "manual")]
    [Arguments(false, "absent")]
    [Arguments(false, "erased")]
    [Arguments(true, "provider")]
    [Arguments(true, "zero")]
    [Arguments(true, "manual")]
    [Arguments(true, "absent")]
    [Arguments(true, "erased")]
    public async Task Projection_PreservesAddressStateWithoutExposingPrivateGraphs(bool list, string state)
    {
        var source = Source(state);
        var actual = list
            ? JsonSerializer.SerializeToNode(LocationMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(LocationMapper.ToDetail(source), JsonSerializerOptions.Web);
        await Assert.That(JsonNode.DeepEquals(actual, Expected(list, state))).IsTrue();
        if (list)
            await Assert.That(LocationMapper.ToListItem(source).TenantId).IsEqualTo(TenantId);
    }

    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    public async Task Projection_PreservesNullAndEmptyTimezone(bool list, string? timezone)
    {
        var source = Source("provider");
        source.Timezone = timezone;
        var expected = Expected(list, "provider");
        expected["timezone"] = timezone;
        var actual = list
            ? JsonSerializer.SerializeToNode(LocationMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(LocationMapper.ToDetail(source), JsonSerializerOptions.Web);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLocation_RemainsNull() => await Assert.That(LocationMapper.ToDetail(null)).IsNull();

    private static JsonNode Expected(bool list, string state)
    {
        var expected = JsonNode.Parse("""
            {"id":"01900000-0000-7000-8000-000000000081","fullName":"Home venue",
             "address":"Rue Test 10","country":"BE","city":"Brussels","timezone":"Europe/Brussels",
             "concurrencyStamp":"01900000-0000-7000-8000-000000000083"}
            """)!;
        if (!list)
        {
            expected["postcode"] = "1000";
            expected["tenantId"] = "01900000-0000-7000-8000-000000000082";
            expected["locationKindId"] = 5;
            expected["latitude"] = state == "zero" ? 0 : state is "manual" or "absent" or "erased" ? null : JsonValue.Create(50.85);
            expected["longitude"] = state == "zero" ? 0 : state is "manual" or "absent" or "erased" ? null : JsonValue.Create(4.35);
        }
        if (state is "absent" or "erased")
        {
            expected["address"] = null;
            if (!list)
                expected["postcode"] = null;
        }
        if (state == "erased")
        {
            expected["fullName"] = "Private venue";
            expected["city"] = "";
        }
        return expected;
    }

    private static Location Source(string state)
    {
        var source = new Location
        {
            Id = LocationId, TenantId = TenantId, FullName = "Home venue", Country = "BE", City = "Brussels",
            Timezone = "Europe/Brussels", CreatedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), CreatedBy = OwnerId
        };
        source.ClassifyAsPrivateHome(OwnerId);
        if (state == "manual")
            source.SetManualAddress("Rue Test 10", "1000");
        else if (state != "absent")
            source.SetProviderAddress("Rue Test 10", "1000", GeoCoordinate.Create(state == "zero" ? 0 : 50.85, state == "zero" ? 0 : 4.35));
        source.Rooms.Add(new LocationRoom
        {
            Id = Guid.Parse("01900000-0000-7000-8000-000000000085"), LocationId = LocationId, Location = source,
            Name = "Private room", Description = "Private room details", TenantId = TenantId, Tenant = null!
        });
        if (state == "erased")
            source.EraseOwnedPii(new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc), LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        source.ConcurrencyStamp = Stamp;
        return source;
    }
}
