using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Helpers;

public sealed class NotificationNavigationHelperTests
{
    [Test]
    public async Task ProtectedNoticePointerReturnsOnlyTheRecipientPortalRoute()
    {
        var id = Guid.Parse("01910000-0000-7000-8000-000000000001");
        var notification = CreateNotification("ModerationPreFindingNotice", id.ToString("D"));

        await Assert.That(NotificationNavigationHelper.GetEntityUrl(notification))
            .IsEqualTo($"/moderation/pre-finding/notices/{id:D}");
    }

    [Test]
    [Arguments("https://example.test/private")]
    [Arguments("01910000-0000-7000-8000-000000000001?reviewResponse=true")]
    [Arguments("00000000-0000-0000-0000-000000000000")]
    [Arguments("01910000-0000-4000-8000-000000000001")]
    public async Task ProtectedNoticePointerRejectsNonCanonicalNoticeIdentifiers(string entityId)
    {
        var notification = CreateNotification("ModerationPreFindingNotice", entityId);
        await Assert.That(NotificationNavigationHelper.GetEntityUrl(notification)).IsNull();
    }

    [Test]
    public async Task GetEntityUrl_WithEventNotification_ReturnsEventDetailRoute()
    {
        var eventId = Guid.NewGuid().ToString();
        var notification = CreateNotification("Event", eventId);

        var url = NotificationNavigationHelper.GetEntityUrl(notification);

        await Assert.That(url).IsEqualTo($"/events/{eventId}");
    }

    [Test]
    public async Task GetEntityUrl_WithOrganizationNotification_ReturnsOrganizationProfileRoute()
    {
        var actorId = Guid.NewGuid().ToString();
        var notification = CreateNotification("Organization", actorId);

        var url = NotificationNavigationHelper.GetEntityUrl(notification);

        await Assert.That(url).IsEqualTo($"/organization/profile/{actorId}");
    }

    [Test]
    public async Task GetEntityUrl_WithGroupNotification_ReturnsGroupProfileRoute()
    {
        var actorId = Guid.NewGuid().ToString();
        var notification = CreateNotification("Group", actorId);

        var url = NotificationNavigationHelper.GetEntityUrl(notification);

        await Assert.That(url).IsEqualTo($"/group/profile/{actorId}");
    }

    [Test]
    public async Task GetEntityUrl_WithUnsupportedNotification_ReturnsNull()
    {
        var notification = CreateNotification("EventSession", Guid.NewGuid().ToString());

        var url = NotificationNavigationHelper.GetEntityUrl(notification);

        await Assert.That(url).IsNull();
    }

    private static NotificationListDto CreateNotification(string entityTypeName, string entityId)
    {
        return new NotificationListDto
        {
            Title = "Notification",
            NotificationEntityTypeName = entityTypeName,
            EntityId = entityId
        };
    }
}
