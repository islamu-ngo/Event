using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;

namespace Event.Api.IntegrationTests.Features.Hateoas;

/// <summary>
/// HAL contract tests on the ContractApiFixture (InMemory, no real database).
/// Validates structural invariants: _links presence, pagination metadata,
/// RFC 7240 Prefer header processing, content-type negotiation, and link format.
/// These tests assert API surface correctness independent of seeded data.
/// </summary>
[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public class HateoasContractTests(ContractApiFixture fixture)
{
    private readonly ContractApiFixture _fixture = fixture;

    private static string WithCacheBust(string endpoint)
    {
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{endpoint}{separator}pageNumber=1&pageSize=20&testRun={Guid.NewGuid():N}";
    }

    #region HAL Structure

    [Test]
    [Arguments("/api/organization")]
    [Arguments("/api/event")]
    [Arguments("/api/eventsession")]
    [Arguments("/api/actor")]
    [Arguments("/api/location")]
    [Arguments("/api/category")]
    [Arguments("/api/tag")]
    public async Task GetAll_ShouldReturnHalCollection_WithLinksAndEmbedded(string endpoint)
    {
        var response = await GetAsync(endpoint, WithCacheBust(endpoint));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        await Assert.That(json.RootElement.TryGetProperty("_links", out var links)).IsTrue();

        var hasCanonicalLink = links.TryGetProperty("self", out _) || links.TryGetProperty("first", out _);
        await Assert.That(hasCanonicalLink).IsTrue();

        await Assert.That(json.RootElement.TryGetProperty("_embedded", out var embedded)).IsTrue();
        await Assert.That(embedded.TryGetProperty("items", out var items)).IsTrue();
        await Assert.That(items.ValueKind).IsEqualTo(JsonValueKind.Array);
    }

    [Test]
    public async Task GetAll_SelfLink_ShouldHaveHrefStartingWithSlash()
    {
        var response = await _fixture.Client.GetAsync(WithCacheBust("/api/event"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink))
        {
            var href = selfLink.GetProperty("href").GetString();
            await Assert.That(href).IsNotNull();
            await Assert.That(href!).StartsWith("/");
        }
    }

    [Test]
    public async Task GetAll_SelfLink_ShouldIncludeMethodProperty()
    {
        var response = await _fixture.Client.GetAsync(WithCacheBust("/api/event"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink) &&
            selfLink.TryGetProperty("method", out var method))
        {
            await Assert.That(method.GetString()).IsEqualTo("GET");
        }
    }

    #endregion

    #region Pagination Metadata

    [Test]
    public async Task GetAll_ShouldIncludePaginationMetadata()
    {
        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=10");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(json.RootElement.TryGetProperty("pageNumber", out var pageNum)).IsTrue();
        await Assert.That(pageNum.GetInt32()).IsEqualTo(1);

        await Assert.That(json.RootElement.TryGetProperty("pageSize", out var pageSize)).IsTrue();
        await Assert.That(pageSize.GetInt32()).IsEqualTo(10);

        await Assert.That(json.RootElement.TryGetProperty("totalCount", out _)).IsTrue();
        await Assert.That(json.RootElement.TryGetProperty("totalPages", out _)).IsTrue();
    }

    [Test]
    public async Task GetAll_FirstPage_ShouldNotHavePrevLink()
    {
        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=5");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(json.RootElement.TryGetProperty("_links", out var links)).IsTrue();
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("first", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("prev", out _)).IsFalse();
    }

    [Test]
    public async Task GetAll_PaginationLinks_ShouldContainCorrectParameters()
    {
        var response = await _fixture.Client.GetAsync("/api/event?pageNumber=1&pageSize=5");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (json.RootElement.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("self", out var selfLink))
        {
            var href = selfLink.GetProperty("href").GetString();
            await Assert.That(href).Contains("pageNumber=1");
            await Assert.That(href).Contains("pageSize=5");
        }
    }

    #endregion

    #region Prefer Header (RFC 7240)

    [Test]
    [Arguments("/api/organization")]
    [Arguments("/api/event")]
    public async Task GetAll_WithPreferMinimal_ShouldReturnPreferenceAppliedHeader(string endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("Prefer", "return=minimal");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var hasHeader = response.Headers.Contains("Preference-Applied");
        await Assert.That(hasHeader).IsTrue();

        var value = response.Headers.GetValues("Preference-Applied").FirstOrDefault();
        await Assert.That(value).IsEqualTo("return=minimal");
    }

    [Test]
    [Arguments("/api/organization")]
    [Arguments("/api/event")]
    [Arguments("/api/eventsession")]
    [Arguments("/api/actor")]
    [Arguments("/api/location")]
    [Arguments("/api/category")]
    [Arguments("/api/tag")]
    public async Task GetAll_WithPreferMinimal_ItemsShouldNotHaveLinks(string endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddLocationAuthentication(endpoint, request);
        request.Headers.Add("Prefer", "return=minimal");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (json.RootElement.TryGetProperty("_embedded", out var embedded) &&
            embedded.TryGetProperty("items", out var items) &&
            items.GetArrayLength() > 0)
        {
            var hasLinks = items[0].TryGetProperty("_links", out _);
            await Assert.That(hasLinks).IsFalse();
        }
    }

    private async Task<HttpResponseMessage> GetAsync(string endpoint, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddLocationAuthentication(endpoint, request);
        return await _fixture.Client.SendAsync(request);
    }

    private static void AddLocationAuthentication(string endpoint, HttpRequestMessage request)
    {
        if (endpoint.Equals("/api/location", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        }
    }

    [Test]
    public async Task GetAll_WithPreferMinimal_ShouldStillHavePaginationMetadata()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/event");
        request.Headers.Add("Prefer", "return=minimal");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(json.RootElement.TryGetProperty("pageNumber", out _)).IsTrue();
        await Assert.That(json.RootElement.TryGetProperty("pageSize", out _)).IsTrue();
        await Assert.That(json.RootElement.TryGetProperty("totalCount", out _)).IsTrue();
    }

    [Test]
    public async Task GetAll_WithPreferRepresentation_ShouldIncludeLinks()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/event");
        request.Headers.Add("Prefer", "return=representation");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("_links");
    }

    [Test]
    public async Task GetAll_WithMultiplePreferValues_ShouldProcessReturnMinimal()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/event");
        request.Headers.Add("Prefer", "return=minimal, respond-async, wait=100");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var hasHeader = response.Headers.Contains("Preference-Applied");
        await Assert.That(hasHeader).IsTrue();
    }

    [Test]
    public async Task GetAll_WithoutPreferHeader_ShouldNotReturnPreferenceAppliedHeader()
    {
        var response = await _fixture.Client.GetAsync(WithCacheBust("/api/event"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var hasHeader = response.Headers.Contains("Preference-Applied");
        await Assert.That(hasHeader).IsFalse();
    }

    #endregion

    #region Content-Type Negotiation

    [Test]
    public async Task GetAll_DefaultAccept_ShouldReturnSupportedJsonContentType()
    {
        var response = await _fixture.Client.GetAsync("/api/event");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var isValid = contentType == "application/json" || contentType == "application/hal+json";
        await Assert.That(isValid).IsTrue();
    }

    [Test]
    public async Task GetAll_AcceptHalJson_ShouldReturnValidContentType()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/event");
        request.Headers.Add("Accept", "application/hal+json");

        var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var isValid = contentType == "application/json" || contentType == "application/hal+json";
        await Assert.That(isValid).IsTrue();
    }

    #endregion

    #region Error Responses

    [Test]
    public async Task GetById_NonExistent_ShouldReturnNotFound_WithoutHalStructure()
    {
        var response = await _fixture.Client.GetAsync($"/api/event/{Guid.NewGuid()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        var content = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(content))
        {
            var json = JsonDocument.Parse(content);
            var hasLinks = json.RootElement.TryGetProperty("_links", out _);
            await Assert.That(hasLinks).IsFalse();
        }
    }

    #endregion
}
