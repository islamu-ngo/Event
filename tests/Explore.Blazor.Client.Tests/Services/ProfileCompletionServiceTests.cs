using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class ProfileCompletionServiceTests
{
    [Test]
    public async Task NameAndImageRequestsCannotShareCollectionNominationsOrPayloads()
    {
        var transport = new ProfileTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example.test") };
        var service = new UserProfileCompletionService(new UserClient(http),
            new UserProfileCompletionClient(http), new UserProfileImageClient(http));
        var option = JsonSerializer.Deserialize<ModerationProfileCollectionOption>(
            "{\"assessment\":{\"sourceId\":\"" + Guid.CreateVersion7() + "\"},\"validUntil\":\"2026-10-19T00:00:00Z\"}")!;
        var user = Guid.CreateVersion7();
        var stamp = Guid.CreateVersion7();
        await service.UpdateNamesAsync(user, stamp, new UpdateUserNamesDto { FirstName = "Chosen", LastName = "Name" }, option);
        await service.UpdateImageAsync(user, stamp, new UpdateUserProfileImageDto { ProfilePictureId = Guid.CreateVersion7() });

        await Assert.That(transport.Requests.Count).IsEqualTo(2);
        var names = transport.Requests[0];
        var image = transport.Requests[1];
        using var nomination = JsonDocument.Parse(names.Nomination!);
        await Assert.That(nomination.RootElement.TryGetProperty("assessment", out _)).IsTrue();
        await Assert.That(nomination.RootElement.TryGetProperty("validUntil", out _)).IsFalse();
        await Assert.That(names.Body).Contains("Chosen");
        await Assert.That(names.Body).DoesNotContain("profilePictureId");
        await Assert.That(image.Path).Contains("/profile-image");
        await Assert.That(image.Nomination).IsNull();
        await Assert.That(image.Body).DoesNotContain("names");
    }

    [Test]
    public async Task BirthDateTransportPreservesTheCalendarDateWithoutAnInstantOrOffset()
    {
        var transport = new ProfileTransport();
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://event.example.test") };
        var service = new UserProfileCompletionService(new UserClient(http),
            new UserProfileCompletionClient(http), new UserProfileImageClient(http));
        var option = JsonSerializer.Deserialize<ModerationProfileCollectionOption>(
            "{\"assessment\":{\"sourceId\":\"" + Guid.CreateVersion7() + "\"},\"validUntil\":\"2026-10-19T00:00:00Z\"}")!;

        await service.RecordBirthDateAsync(Guid.CreateVersion7(), new DateOnly(2000, 2, 29), 7, option);

        var request = transport.Requests.Single();
        using var body = JsonDocument.Parse(request.Body);
        await Assert.That(body.RootElement.GetProperty("birthDate").GetString()).IsEqualTo("2000-02-29");
        await Assert.That(body.RootElement.GetProperty("expectedRevision").GetString()).IsEqualTo("7");
        await Assert.That(request.Nomination).IsNotNull();
        await Assert.That(request.Path).Contains("/private-birth-date");
    }

    private sealed class ProfileTransport : HttpMessageHandler
    {
        public List<(string Path, string? Nomination, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add((request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-Profile-Collection", out var values) ? values.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(token)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json") };
        }
    }
}
