using System.Text.Json;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.DTOs.EventSeries.Validators;
using Explore.Application.Mappings;
using Explore.Application.Models.Common;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventSeriesMapperTests
{
    [Test]
    public async Task Projection_SnapshotsSeriesContentWithoutInventingUnloadedNavigations()
    {
        var series = new EventSeries
        {
            Id = Guid.CreateVersion7(), Title = "Summer Series", Description = "Weekly workshops",
            Slug = "summer-series", ActorId = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(),
            IsPublished = true, VisibilityTypeId = 1, VisibilityType = null!
        };
        var detail = EventMapper.ToDetail(series);
        var list = EventMapper.ToListItem(series);
        series.Description = "Changed";
        await Assert.That(detail.Description).IsEqualTo("Weekly workshops");
        await Assert.That(detail.ActorId).IsEqualTo(series.ActorId);
        await Assert.That(detail.TenantId).IsEqualTo(series.TenantId);
        await Assert.That(detail.ActorDisplayName).IsNull();
        await Assert.That(detail.FeaturedImageUri).IsNull();
        await Assert.That(detail.Events).IsEmpty();
        await Assert.That(list.EventCount).IsEqualTo(0);
        await Assert.That(list.ActorDisplayName).IsNull();
    }

    [Test]
    public async Task Patch_RecordCopiesAndJsonPreserveOmissionClearAndReplacement()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var omitted = JsonSerializer.Deserialize<UpdateEventSeriesDto>("{\"title\":{\"value\":\"Summer\"}}", options)!;
        var clear = omitted with
        {
            Description = new() { Value = OptionalUpdate<string?>.Set(null) }
        };
        var replacement = clear with
        {
            Description = new() { Value = OptionalUpdate<string?>.Set("Workshops") }
        };
        var roundTrip = JsonSerializer.Deserialize<UpdateEventSeriesDto>(JsonSerializer.Serialize(clear, options), options);
        await Assert.That(omitted.Description).IsNull();
        await Assert.That(clear.Description!.Value.HasValue).IsTrue();
        await Assert.That(clear.Description.Value.Value).IsNull();
        await Assert.That(roundTrip).IsEqualTo(clear);
        await Assert.That(replacement.Description!.Value.Value).IsEqualTo("Workshops");
        var validator = new UpdateEventSeriesDtoValidator();
        await Assert.That(validator.Validate(omitted).IsValid).IsTrue();
        await Assert.That(validator.Validate(clear).IsValid).IsTrue();
        await Assert.That(validator.Validate(replacement).IsValid).IsTrue();
        await Assert.That(validator.Validate(new UpdateEventSeriesDto()).IsValid).IsFalse();
        await Assert.That(validator.Validate(new UpdateEventSeriesDto { Description = new() }).IsValid).IsFalse();
    }
}
