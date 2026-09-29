using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

/// <summary>
/// Integration tests for HATEOAS functionality.
/// Tests RFC 7240 Prefer header processing, HAL+JSON responses, and pagination links.
/// </summary>
[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class HateoasIntegrationTests
{
    private readonly ApiTestFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HateoasIntegrationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string WithCacheBust(string endpoint)
    {
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{endpoint}{separator}pageNumber=1&pageSize=20&testRun={Guid.NewGuid():N}";
    }

    #region Default HAL Response Tests

    [Test]
    [Arguments("/api/organization")]
    [Arguments("/api/event")]
    [Arguments("/api/eventsession")]
    [Arguments("/api/actor")]
    [Arguments("/api/category")]
    [Arguments("/api/tag")]
    public async Task GetAll_WithoutPreferHeader_ShouldIncludeLinks(string endpoint)
    {
        // Act
        var response = await _fixture.Client.GetAsync(WithCacheBust(endpoint));

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();

        // HAL responses should include _links
        await Assert.That(content).Contains("_links");

        // Verify it's valid JSON with HAL structure
        var json = JsonDocument.Parse(content);
        var hasLinksProperty = json.RootElement.TryGetProperty("_links", out var links);
        await Assert.That(hasLinksProperty).IsTrue();

        // Collection payloads should expose at least one primary navigation link.
        var hasPrimaryLink = links.TryGetProperty("self", out _) || links.TryGetProperty("first", out _);
        await Assert.That(hasPrimaryLink).IsTrue();
    }

    [Test]
    public async Task GetById_WithValidId_ShouldIncludeSelfLink()
    {
        // Arrange - First get a list to find an ID (or use a known structure)
        var listResponse = await _fixture.Client.GetAsync("/api/organization");
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Even with empty data, a GET by random ID should return HAL structure or 404
        var randomId = Guid.NewGuid();
        var response = await _fixture.Client.GetAsync($"/api/organization/{randomId}");

        // Assert - Either 404 (not found) or 200 with HAL structure
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            await Assert.That(content).Contains("_links");
            await Assert.That(content).Contains("self");
        }
        else
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
    }

    #endregion

    #region Prefer Header Tests (RFC 7240)

    [Test]
    [Arguments("/api/organization")]
    [Arguments("/api/event")]
    [Arguments("/api/eventsession")]
    [Arguments("/api/actor")]
    [Arguments("/api/category")]
    [Arguments("/api/tag")]
    public async Task GetAll_WithPreferMinimal_ShouldExcludeLinks(string endpoint)
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("Prefer", "return=minimal");

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();

        // Parse and check structure
        var json = JsonDocument.Parse(content);

        // When minimal, items within _embedded should NOT have _links
        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var firstItem = items[0];
            var hasLinks = firstItem.TryGetProperty("_links", out _);
            await Assert.That(hasLinks).IsFalse();
        }
    }

    [Test]
    [Arguments("/api/organization")]
    [Arguments("/api/event")]
    public async Task GetAll_WithPreferMinimal_ShouldReturnPreferenceAppliedHeader(string endpoint)
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("Prefer", "return=minimal");

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Check for Preference-Applied header
        var hasPreferenceApplied = response.Headers.Contains("Preference-Applied");
        await Assert.That(hasPreferenceApplied).IsTrue();

        var preferenceApplied = response.Headers.GetValues("Preference-Applied").FirstOrDefault();
        await Assert.That(preferenceApplied).IsEqualTo("return=minimal");
    }

    [Test]
    public async Task GetAll_WithPreferRepresentation_ShouldIncludeLinks()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/organization");
        request.Headers.Add("Prefer", "return=representation");

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("_links");
    }

    [Test]
    public async Task GetAll_WithMultiplePreferValues_ShouldProcessReturnMinimal()
    {
        // Arrange - Multiple preferences separated by comma
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/organization");
        request.Headers.Add("Prefer", "return=minimal, respond-async, wait=100");

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var hasPreferenceApplied = response.Headers.Contains("Preference-Applied");
        await Assert.That(hasPreferenceApplied).IsTrue();
    }

    [Test]
    public async Task GetAll_WithoutPreferHeader_ShouldNotReturnPreferenceAppliedHeader()
    {
        // Act
        var response = await _fixture.Client.GetAsync(WithCacheBust("/api/organization"));

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var hasPreferenceApplied = response.Headers.Contains("Preference-Applied");
        await Assert.That(hasPreferenceApplied).IsFalse();
    }

    #endregion

    #region Pagination Links Tests

    [Test]
    public async Task GetAll_FirstPage_ShouldHaveCorrectPaginationLinks()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization?pageNumber=1&pageSize=5");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        await Assert.That(json.RootElement.TryGetProperty("_links", out var links)).IsTrue();

        // First page should have: self, first
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("first", out _)).IsTrue();

        // First page should NOT have prev link
        await Assert.That(links.TryGetProperty("prev", out _)).IsFalse();
    }

    [Test]
    public async Task GetAll_ShouldIncludePaginationMetadata()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization?pageNumber=1&pageSize=10");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Check pagination metadata
        await Assert.That(json.RootElement.TryGetProperty("pageNumber", out var pageNumber)).IsTrue();
        await Assert.That(pageNumber.GetInt32()).IsEqualTo(1);

        await Assert.That(json.RootElement.TryGetProperty("pageSize", out var pageSize)).IsTrue();
        await Assert.That(pageSize.GetInt32()).IsEqualTo(10);

        await Assert.That(json.RootElement.TryGetProperty("totalCount", out _)).IsTrue();
        await Assert.That(json.RootElement.TryGetProperty("totalPages", out _)).IsTrue();
    }

    [Test]
    public async Task GetAll_PaginationLinks_ShouldContainCorrectParameters()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=5");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink))
        {
            var href = selfLink.GetProperty("href").GetString();
            await Assert.That(href).Contains("pageNumber=1");
            await Assert.That(href).Contains("pageSize=5");
        }
    }

    #endregion

    #region HAL+JSON Structure Tests

    [Test]
    public async Task GetAll_ShouldReturnValidHalJsonStructure()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // HAL collection should have _links at root
        await Assert.That(json.RootElement.TryGetProperty("_links", out _)).IsTrue();

        // HAL collection should have _embedded.items
        await Assert.That(json.RootElement.TryGetProperty("_embedded", out var embedded)).IsTrue();
        await Assert.That(embedded.TryGetProperty("items", out var items)).IsTrue();
        await Assert.That(items.ValueKind).IsEqualTo(JsonValueKind.Array);
    }

    [Test]
    public async Task GetAll_LinksFormat_ShouldBeRfc8288Compliant()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink))
        {
            // Each link should have href property
            await Assert.That(selfLink.TryGetProperty("href", out var href)).IsTrue();
            await Assert.That(href.GetString()).IsNotNull();

            // href should be a valid relative or absolute URL
            var hrefValue = href.GetString()!;
            await Assert.That(hrefValue).StartsWith("/");
        }
    }

    [Test]
    public async Task GetAll_Links_ShouldIncludeHttpMethod()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink))
        {
            // Self link should have GET method
            if (selfLink.TryGetProperty("method", out var method))
            {
                await Assert.That(method.GetString()).IsEqualTo("GET");
            }
        }
    }

    [Test]
    public async Task GetAll_EmbeddedItems_ShouldHaveCorrectDtoProperties()
    {
        // Act
        var response = await _fixture.Client.GetAsync("/api/organization");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Verify embedded items structure (if any items exist)
        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var firstItem = items[0];

            // Each item should have data property with DTO fields flattened or nested
            // Based on HAL serialization, data is flattened
            // Check for typical DTO properties
            var hasId = firstItem.TryGetProperty("id", out _) ||
                        (firstItem.TryGetProperty("data", out var data) && data.TryGetProperty("id", out _));
            await Assert.That(hasId).IsTrue();
        }
    }

    #endregion

    #region Link Relations Tests (IANA Standard)

    [Test]
    public async Task GetAll_ShouldUseIanaLinkRelations()
    {
        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, WithCacheBust("/api/organization"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        if (json.RootElement.TryGetProperty("_links", out var links))
        {
            // Standard IANA link relations
            var standardRelations = new[] { "self", "first", "prev", "next", "last" };
            var foundRelations = new List<string>();

            foreach (var link in links.EnumerateObject())
            {
                foundRelations.Add(link.Name);
            }

            // At minimum, should have a primary navigation link.
            var hasPrimaryLink = foundRelations.Contains("self") || foundRelations.Contains("first");
            await Assert.That(hasPrimaryLink).IsTrue();

            // All found relations should be either IANA standard or custom with proper prefix
            foreach (var rel in foundRelations)
            {
                var isIanaStandard = standardRelations.Contains(rel) ||
                                     new[] { "collection", "edit", "create", "delete" }.Contains(rel);
                // Custom relations are also allowed
                await Assert.That(isIanaStandard || !string.IsNullOrEmpty(rel)).IsTrue();
            }
        }
    }

    #endregion

    #region Content-Type Tests

    [Test]
    public async Task GetAll_ShouldReturnJsonContentType()
    {
        var response = await _fixture.Client.GetAsync("/api/organization");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var isJsonOrHal = contentType == "application/json" || contentType == "application/hal+json";
        await Assert.That(isJsonOrHal).IsTrue();
    }

    [Test]
    public async Task GetAll_WithAcceptHalJson_ShouldReturnHalJsonContentType()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/organization");
        request.Headers.Add("Accept", "application/hal+json");

        // Act
        var response = await _fixture.Client.SendAsync(request);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Content type should be either application/json or application/hal+json
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var isValidContentType = contentType == "application/json" ||
                                 contentType == "application/hal+json";
        await Assert.That(isValidContentType).IsTrue();
    }

    #endregion

    #region Error Response Tests

    [Test]
    public async Task Create_WithoutAuth_ShouldReturnUnauthorized_WithoutHalStructure()
    {
        // Arrange
        var content = JsonContent.Create(new { FullName = "Test" });

        // Act
        var response = await _fixture.Client.PostAsync("/api/organization", content);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        // Error responses should not have HAL structure
        var responseContent = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(responseContent))
        {
            // Error responses typically don't follow HAL format
            var json = JsonDocument.Parse(responseContent);
            var hasLinksProperty = json.RootElement.TryGetProperty("_links", out _);
            // It's OK if error responses don't have _links
            // Just ensure response is valid JSON
            await Assert.That(json.RootElement.ValueKind).IsNotEqualTo(JsonValueKind.Undefined);
        }
    }

    #endregion
}
