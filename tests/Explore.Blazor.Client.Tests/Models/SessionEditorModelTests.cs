using Explore.Blazor.Client.Pages.Events.Models;

namespace Explore.Blazor.Client.Tests.Models;

public class SessionEditorModelTests
{
    [Test]
    public async Task FromDto_ShouldMapFromSessionDto()
    {
        // Arrange
        var imageId = Guid.NewGuid();
        var dto = new EventSessionListDto
        {
            Id = Guid.NewGuid(),
            Title = "From API",
            StartTime = new DateTimeOffset(2025, 8, 10, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2025, 8, 10, 11, 0, 0, TimeSpan.Zero),
            LocationId = Guid.NewGuid(),
            MaxAudienceAttendees = 200,
            RegistrationModeId = 1,
            // FeaturedImageId = imageId,
            // FeaturedImageUri = "https://cdn.example.com/session-img.jpg"
        };

        // Act
        var model = SessionEditorModel.FromDto(dto);

        // Assert
        await Assert.That(model.Id).IsEqualTo(dto.Id);
        await Assert.That(model.Title).IsEqualTo("From API");
        await Assert.That(model.LocationId).IsEqualTo(dto.LocationId);
        await Assert.That(model.MaxAudienceAttendees).IsEqualTo(200);
        await Assert.That(model.RegistrationModeId).IsEqualTo(1);
        // await Assert.That(model.FeaturedImageId).IsEqualTo(imageId);
        // await Assert.That(model.FeaturedImagePreviewUrl).IsEqualTo("https://cdn.example.com/session-img.jpg");
        // await Assert.That(model.UseEventImage).IsFalse();
    }

    [Test]
    public async Task FromDto_WithNoImage_ShouldDefaultToUseEventImage()
    {
        // Arrange
        var dto = new EventSessionListDto
        {
            Id = Guid.NewGuid(),
            Title = "No Image Session",
            StartTime = new DateTimeOffset(2025, 8, 10, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2025, 8, 10, 11, 0, 0, TimeSpan.Zero),
            // FeaturedImageId = null,
            // FeaturedImageUri = null
        };

        // Act
        var model = SessionEditorModel.FromDto(dto);

        // Assert
        await Assert.That(model.FeaturedImageId).IsNull();
        await Assert.That(model.FeaturedImagePreviewUrl).IsNull();
        await Assert.That(model.UseEventImage).IsTrue();
    }
}
