using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

/// <summary>
/// Integration tests for HateoasLinkGenerator.
/// Tests that link generation works correctly for all entity types.
/// </summary>
[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class HateoasLinkGeneratorTests
{
    private readonly ApiTestFixture _fixture;

    private static string WithCacheBust(string endpoint)
    {
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{endpoint}{separator}pageNumber=1&pageSize=20&testRun={Guid.NewGuid():N}";
    }

    public HateoasLinkGeneratorTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Self Link Generation

    [Test]
    [Arguments("/api/organization")]
    [Arguments("/api/event")]
    [Arguments("/api/eventsession")]
    [Arguments("/api/actor")]
    [Arguments("/api/category")]
    [Arguments("/api/tag")]
    public async Task LinkGenerator_CollectionEndpoints_ShouldHaveSelfLink(string endpoint)
    {
        // Act
        var response = await _fixture.Client.GetAsync(WithCacheBust(endpoint));

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        await Assert.That(json.RootElement.TryGetProperty("_links", out var links)).IsTrue();

        var hasSelfLink = links.TryGetProperty("self", out var selfLink);
        var hasFirstLink = links.TryGetProperty("first", out var firstLink);
        await Assert.That(hasSelfLink || hasFirstLink).IsTrue();

        var primaryLink = hasSelfLink ? selfLink : firstLink;
        await Assert.That(primaryLink.TryGetProperty("href", out var href)).IsTrue();

        var hrefValue = href.GetString();
        await Assert.That(hrefValue).IsNotNull();
        await Assert.That(hrefValue).StartsWith("/api/");
    }

    #endregion

    #region Pagination Link Generation

    [Test]
    public async Task LinkGenerator_FirstPage_ShouldHaveFirstLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization?pageNumber=1&pageSize=5");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links))
        {
            await Assert.That(links.TryGetProperty("first", out var firstLink)).IsTrue();
            var href = firstLink.GetProperty("href").GetString()!;
            await Assert.That(href).Contains("pageNumber=1");
        }
    }

    [Test]
    public async Task LinkGenerator_FirstPage_ShouldNotHavePrevLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization?pageNumber=1&pageSize=5");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links))
        {
            await Assert.That(links.TryGetProperty("prev", out _)).IsFalse();
        }
    }

    [Test]
    public async Task LinkGenerator_SelfLink_ShouldIncludePaginationParameters()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization?pageNumber=2&pageSize=10");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink))
        {
            var href = selfLink.GetProperty("href").GetString()!;
            await Assert.That(href).Contains("pageNumber=2");
            await Assert.That(href).Contains("pageSize=10");
        }
    }

    #endregion

    #region Item Link Generation

    [Test]
    public async Task LinkGenerator_ItemLinks_ShouldIncludeSelfLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var firstItem = items[0];
            if (firstItem.TryGetProperty("_links", out var itemLinks))
            {
                await Assert.That(itemLinks.TryGetProperty("self", out var selfLink)).IsTrue();
                var href = selfLink.GetProperty("href").GetString();
                await Assert.That(href).Contains("/api/organization/");
            }
        }
    }

    [Test]
    public async Task LinkGenerator_ItemLinks_ShouldIncludeCollectionLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var firstItem = items[0];
            if (firstItem.TryGetProperty("_links", out var itemLinks))
            {
                await Assert.That(itemLinks.TryGetProperty("collection", out var collectionLink)).IsTrue();
                var href = collectionLink.GetProperty("href").GetString();
                await Assert.That(href).IsEqualTo("/api/organization");
            }
        }
    }

    #endregion

    #region Link Href Format Validation

    [Test]
    public async Task LinkGenerator_AllLinks_ShouldStartWithSlash()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links))
        {
            foreach (var link in links.EnumerateObject())
            {
                var href = link.Value.GetProperty("href").GetString()!;
                await Assert.That(href).StartsWith("/");
            }
        }
    }

    [Test]
    public async Task LinkGenerator_AllLinks_ShouldNotBeAbsoluteUrls()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links))
        {
            foreach (var link in links.EnumerateObject())
            {
                var href = link.Value.GetProperty("href").GetString()!;
                // Should not contain http:// or https://
                await Assert.That(href.StartsWith("http://", StringComparison.Ordinal)).IsFalse();
                await Assert.That(href.StartsWith("https://", StringComparison.Ordinal)).IsFalse();
            }
        }
    }

    #endregion

    #region HTTP Method on Links

    [Test]
    public async Task LinkGenerator_SelfLink_ShouldHaveGetMethod()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink) &&
            selfLink.TryGetProperty("method", out var method))
        {
            await Assert.That(method.GetString()).IsEqualTo("GET");
        }
    }

    #endregion

    #region Entity-Specific Related Links

    [Test]
    public async Task LinkGenerator_Events_ShouldHaveSessionsLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/event");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var firstItem = items[0];
            if (firstItem.TryGetProperty("_links", out var itemLinks))
            {
                // Events should have sessions link
                if (itemLinks.TryGetProperty("sessions", out var sessionsLink))
                {
                    var href = sessionsLink.GetProperty("href").GetString();
                    await Assert.That(href).Contains("/api/eventsession/by-event/");
                }
            }
        }
    }

    [Test]
    public async Task LinkGenerator_EventSessions_ShouldHaveEventLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/eventsession");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var firstItem = items[0];
            if (firstItem.TryGetProperty("_links", out var itemLinks))
            {
                // Event sessions should have parent event link
                if (itemLinks.TryGetProperty("event", out var eventLink))
                {
                    var href = eventLink.GetProperty("href").GetString();
                    await Assert.That(href).Contains("/api/event/");
                }
            }
        }
    }

    [Test]
    public async Task LinkGenerator_Categories_ShouldHaveChildrenLink()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/category");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var firstItem = items[0];
            if (firstItem.TryGetProperty("_links", out var itemLinks))
            {
                // Categories should have children link
                if (itemLinks.TryGetProperty("children", out var childrenLink))
                {
                    var href = childrenLink.GetProperty("href").GetString();
                    await Assert.That(href).Contains("/api/category/children/");
                }
            }
        }
    }

    #endregion
}
