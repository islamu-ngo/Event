using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class LocationRoomMapperTests
{
    private static readonly Guid RoomId = Guid.Parse("01900000-0000-7000-8000-000000000091");
    private static readonly Guid LocationId = Guid.Parse("01900000-0000-7000-8000-000000000093");
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000094");
    private static readonly Guid Stamp = Guid.Parse("01900000-0000-7000-8000-000000000095");

    [Test]
    [Arguments(false, true)]
    [Arguments(false, false)]
    [Arguments(true, true)]
    [Arguments(true, false)]
    public async Task Projection_PreservesRoomContractWithoutParentPiiOrAuditGraphs(bool list, bool loaded)
    {
        var source = Source();
        if (!loaded)
            source.Location = null!;
        await AssertContract(source, list, Expected(list, loaded));
    }

    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    public async Task Projection_PreservesOptionalTextAndListOmissions(bool list, string? text)
    {
        var source = Source();
        source.Slug = text;
        source.Description = text;
        var expected = Expected(list, true);
        if (!list)
        {
            expected["slug"] = text;
            expected["description"] = text;
        }
        await AssertContract(source, list, expected);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Projection_PreservesUnknownCapacity(bool list)
    {
        var source = Source();
        source.Capacity = null;
        var expected = Expected(list, true);
        expected["capacity"] = null;
        await AssertContract(source, list, expected);
    }

    [Test]
    public async Task MissingRoom_RemainsNull() => await Assert.That(LocationRoomMapper.ToDetail(null)).IsNull();

    [Test]
    public async Task Create_UsesTrustedParentTenantAndOnlyBusinessFields()
    {
        var result = LocationRoomMapper.Create(new CreateLocationRoomDto
        {
            LocationId = LocationId, Name = "Main hall", Slug = "main-hall", Description = "Room notes", Capacity = 120, SortOrder = 7
        }, TenantId);
        await Assert.That(result.TenantId).IsEqualTo(TenantId);
        await Assert.That(result.LocationId).IsEqualTo(LocationId);
        await Assert.That(result.Name).IsEqualTo("Main hall");
        await Assert.That(result.Slug).IsEqualTo("main-hall");
        await Assert.That(result.Description).IsEqualTo("Room notes");
        await Assert.That(result.Capacity).IsEqualTo(120);
        await Assert.That(result.SortOrder).IsEqualTo(7);
        await Assert.That(result.Id).IsEqualTo(Guid.Empty);
        await Assert.That(result.ConcurrencyStamp).IsEqualTo(Guid.Empty);
        await Assert.That(result.CreatedBy).IsNull();
        await Assert.That(result.UpdatedBy).IsNull();
        await Assert.That(result.IsDeleted).IsFalse();
        await Assert.That(result.Location).IsNull();
        await Assert.That(result.Tenant).IsNull();
    }

    private static async Task AssertContract(LocationRoom source, bool list, JsonNode expected)
    {
        var actual = list
            ? JsonSerializer.SerializeToNode(LocationRoomMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(LocationRoomMapper.ToDetail(source), JsonSerializerOptions.Web);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    private static JsonNode Expected(bool list, bool loaded)
    {
        var expected = JsonNode.Parse("""
            {"id":"01900000-0000-7000-8000-000000000091","locationId":"01900000-0000-7000-8000-000000000093",
             "name":"Main hall","capacity":120,"sortOrder":7,"concurrencyStamp":"01900000-0000-7000-8000-000000000095"}
            """)!;
        if (!list)
        {
            expected["locationFullName"] = loaded ? "Venue" : null;
            expected["slug"] = "main-hall";
            expected["description"] = "Room notes";
            expected["tenantId"] = "01900000-0000-7000-8000-000000000094";
        }
        return expected;
    }

    private static LocationRoom Source()
    {
        var parent = new Location { Id = LocationId, TenantId = TenantId, FullName = "Venue", Country = "BE", City = "Brussels" };
        parent.SetManualAddress("private-parent-address", "1000");
        var source = new LocationRoom
        {
            Id = RoomId, LocationId = LocationId, Location = parent, TenantId = TenantId, Tenant = null!, Name = "Main hall",
            Slug = "main-hall", Description = "Room notes", Capacity = 120, SortOrder = 7, ConcurrencyStamp = Stamp,
            CreatedBy = Stamp, UpdatedBy = Stamp, DeletedBy = Stamp, IsDeleted = true,
            CreatedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
            DeletedAt = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc)
        };
        parent.Rooms.Add(source);
        return source;
    }
}
