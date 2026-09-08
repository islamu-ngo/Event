using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffPreferenceAntiforgeryTests : IAsyncDisposable
{
    private readonly BlazorBffWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public BffPreferenceAntiforgeryTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Test]
    public async Task ThemePreference_PostWithoutAntiforgeryHeader_ReturnsBadRequest()
    {
        await IssueAntiforgeryCookieAsync();

        using var response = await _client.PostAsync("/bff/theme?theme=dark", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Antiforgery validation failed");
    }

    [Test]
    public async Task ThemePreference_PostWithInvalidAntiforgeryHeader_ReturnsBadRequest()
    {
        await IssueAntiforgeryCookieAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/theme?theme=dark");
        request.Headers.Add("X-CSRF-TOKEN", "invalid-token");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Antiforgery validation failed");
    }

    [Test]
    public async Task ThemePreference_PostWithValidAntiforgeryHeader_ReturnsOk()
    {
        var token = await IssueAntiforgeryCookieAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/theme?theme=dark");
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task ThemePreference_AnonymousWithValidAntiforgery_PersistsThemeCookieAndReturnsUpdatedPreference()
    {
        var token = await IssueAntiforgeryCookieAsync();

        using var request = CreateMutationRequest(HttpMethod.Post, "/bff/theme?theme=dark", token);
        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        await Assert.That(json.RootElement.GetProperty("themeMode").GetString()).IsEqualTo("dark");
        await Assert.That(await GetSetCookieHeaders(response)).Contains(cookie => cookie.StartsWith("theme=dark", StringComparison.Ordinal));
    }

    [Test]
    public async Task LanguagePreference_AnonymousWithValidAntiforgery_PersistsLangAndAspNetCultureCookies()
    {
        var token = await IssueAntiforgeryCookieAsync();

        using var request = CreateMutationRequest(HttpMethod.Post, "/bff/language?lang=fr", token);
        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        await Assert.That(json.RootElement.GetProperty("language").GetString()).IsEqualTo("fr");
        var cookies = await GetSetCookieHeaders(response);
        await Assert.That(cookies).Contains(cookie => cookie.StartsWith("lang=fr", StringComparison.Ordinal));
        await Assert.That(cookies).Contains(cookie => cookie.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal));
    }

    [Test]
    public async Task DirectionPreference_AnonymousAuto_DeletesDirectionCookie()
    {
        var token = await IssueAntiforgeryCookieAsync();

        using (var setRtlRequest = CreateMutationRequest(HttpMethod.Post, "/bff/direction?dir=rtl", token))
        using (var setRtlResponse = await _client.SendAsync(setRtlRequest))
        {
            await Assert.That(setRtlResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await GetSetCookieHeaders(setRtlResponse)).Contains(cookie => cookie.StartsWith("direction=rtl", StringComparison.Ordinal));
        }

        using var autoRequest = CreateMutationRequest(HttpMethod.Post, "/bff/direction?dir=auto", token);
        using var autoResponse = await _client.SendAsync(autoRequest);

        await Assert.That(autoResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await ReadJsonAsync(autoResponse);
        await Assert.That(json.RootElement.GetProperty("direction").GetString()).IsEqualTo("auto");
        await Assert.That(await GetSetCookieHeaders(autoResponse)).Contains(cookie =>
            cookie.StartsWith("direction=", StringComparison.Ordinal) &&
            cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task ClonePreset_AnonymousWithValidAntiforgery_ReturnsUnauthorized()
    {
        var token = await IssueAntiforgeryCookieAsync();
        var presetId = Guid.NewGuid();

        using var request = CreateMutationRequest(HttpMethod.Post, $"/bff/appearance/profiles/from-preset/{presetId}", token);
        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateProfile_UsesPatchAndRejectsObsoletePut()
    {
        var token = await IssueAntiforgeryCookieAsync();
        var profileId = Guid.NewGuid();
        var body = new UpdateAppearanceProfileRequestDto
        {
            Metadata = new UpdateAppearanceProfileMetadataDto { Name = "Updated" }
        };

        using var patchRequest = CreateMutationRequest(HttpMethod.Patch, $"/bff/appearance/profiles/{profileId}", token);
        patchRequest.Content = JsonContent.Create(body);
        using var patchResponse = await _client.SendAsync(patchRequest);

        await Assert.That(patchResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        using var putRequest = CreateMutationRequest(HttpMethod.Put, $"/bff/appearance/profiles/{profileId}", token);
        putRequest.Content = JsonContent.Create(body);
        using var putResponse = await _client.SendAsync(putRequest);

        await Assert.That(putResponse.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<string> IssueAntiforgeryCookieAsync()
    {
        using var response = await _client.GetAsync("/auth/status");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var values)).IsTrue();

        var token = values!
            .Select(ReadXsrfToken)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        await Assert.That(string.IsNullOrWhiteSpace(token)).IsFalse()
            .Because("GET requests should issue the readable XSRF-TOKEN cookie");
        return token!;
    }

    private static string? ReadXsrfToken(string setCookie)
    {
        const string prefix = "XSRF-TOKEN=";
        if (!setCookie.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = setCookie.IndexOf(';', prefix.Length);
        var rawValue = end < 0 ? setCookie[prefix.Length..] : setCookie[prefix.Length..end];
        return Uri.UnescapeDataString(rawValue);
    }

    private static HttpRequestMessage CreateMutationRequest(HttpMethod method, string requestUri, string token)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Add("X-CSRF-TOKEN", token);
        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    private static async Task<IReadOnlyCollection<string>> GetSetCookieHeaders(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var values)).IsTrue();
        return values!.ToArray();
    }
}
