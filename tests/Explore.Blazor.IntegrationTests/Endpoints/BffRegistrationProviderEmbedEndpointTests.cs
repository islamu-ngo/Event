using System.Text.RegularExpressions;
using System.Text.Json;
using System.Net.Http.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration.ProviderLaunch;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public interface ITestRegistrationOrderProviderLaunchClient : IAuthenticatedRegistrationOrderClient, IGuestRegistrationOrderClient
{
}

public sealed partial class BffRegistrationProviderEmbedEndpointTests : IAsyncDisposable
{
    private static readonly Guid EventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly Guid OrderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000201");
    private static readonly Guid RequirementId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000301");
    private static readonly Guid ChannelId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000401");
    private static readonly Guid BindingId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000501");
    private static readonly Guid FormId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000601");
    private static readonly Guid FormVersionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000701");

    private readonly ITestRegistrationOrderProviderLaunchClient _apiClient = Substitute.For<ITestRegistrationOrderProviderLaunchClient>();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public BffRegistrationProviderEmbedEndpointTests()
    {
        _factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthenticatedRegistrationOrderClient>();
                services.RemoveAll<IGuestRegistrationOrderClient>();
                services.AddSingleton<IAuthenticatedRegistrationOrderClient>(_apiClient);
                services.AddSingleton<IGuestRegistrationOrderClient>(_apiClient);
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Test]
    public async Task Launch_WithoutAntiforgery_ReturnsBadRequest()
    {
        GivenDescriptor(Descriptor());
        using var response = await _client.PostAsJsonAsync(Route(), LaunchRequest());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await _apiClient.DidNotReceiveWithAnyArgs().LaunchAuthenticatedRegistrationProviderAttemptAsync(
            default, default, default!, null, null, default);
    }

    [Test]
    public async Task EmbedHost_ValidDescriptor_UsesRouteCspAndSameOriginFramePolicy()
    {
        GivenDescriptor(Descriptor(url: "https://forms.example.test/embed/form-1?token=server-only", title: "Provider registration"));

        using var response = await SendAuthenticatedAsync(Route());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues("Content-Security-Policy").Single()).IsEqualTo("default-src 'none'; frame-src https://forms.example.test; frame-ancestors 'self'; base-uri 'none'; form-action 'none'; object-src 'none'; script-src 'none'; style-src 'none'");
        await Assert.That(response.Headers.GetValues("X-Frame-Options").Single()).IsEqualTo("SAMEORIGIN");
        var cacheControl = response.Headers.GetValues("Cache-Control").Single();
        await Assert.That(cacheControl).Contains("private");
        await Assert.That(cacheControl).Contains("no-store");
        await Assert.That(response.Headers.GetValues("Pragma")).Contains("no-cache");
        await Assert.That(GetRawHeader(response, "Expires")).IsEqualTo("0");
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("<iframe");
        await Assert.That(body).Contains("sandbox=\"allow-forms allow-same-origin allow-scripts\"");
    }

    [Test]
    public async Task EmbedHost_DefaultHttpsPort_UsesCanonicalCspOrigin()
    {
        GivenDescriptor(Descriptor(url: "https://forms.example.test:443/embed/form-1"));

        using var response = await SendAuthenticatedAsync(Route());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues("Content-Security-Policy").Single()).Contains("frame-src https://forms.example.test; ");
    }

    [Test]
    public async Task EmbedHost_MaliciousDescriptorValues_EncodesHtmlAndNeverAcceptsClientUrl()
    {
        GivenDescriptor(Descriptor(
            url: "https://forms.example.test/embed?next=\"&label=<script>alert(1)</script>",
            title: "Bad <img src=x onerror=alert(1)> title"));

        using var response = await SendAuthenticatedAsync(Route());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        await Assert.That(html).Contains("Bad &lt;img src=x onerror=alert(1)&gt; title");
        await Assert.That(html).DoesNotContain("<script>");
        await Assert.That(html).DoesNotContain("onload=");
        await Assert.That(DangerousAttributeBreakout().IsMatch(html)).IsFalse();

        using var rejected = await SendAuthenticatedAsync(Route() + "?url=https://attacker.example/embed");
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task EmbedHost_UnavailableNonEmbedOrTamperedLineage_ReturnsNotFound()
    {
        GivenDescriptor(Descriptor(available: false));
        using var unavailable = await SendAuthenticatedAsync(Route());
        await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        GivenDescriptor(Descriptor(mode: "redirect"));
        using var nonEmbed = await SendAuthenticatedAsync(Route());
        await Assert.That(nonEmbed.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        GivenDescriptor(Descriptor(bindingId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000502")));
        using var tampered = await SendAuthenticatedAsync(Route());
        await Assert.That(tampered.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EmbedHost_TamperedFormOrHttpDescriptor_ReturnsNotFound()
    {
        GivenDescriptor(Descriptor(formId: Guid.Parse("018e4e5c-7f00-7000-8000-000000000602")));
        using var tamperedForm = await SendAuthenticatedAsync(Route());
        await Assert.That(tamperedForm.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        GivenDescriptor(Descriptor(url: "http://forms.example.test/embed"));
        using var httpDescriptor = await SendAuthenticatedAsync(Route());
        await Assert.That(httpDescriptor.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EmbedHost_UserInfoOrBlockedLiteralHost_ReturnsNotFound()
    {
        GivenDescriptor(Descriptor(url: "https://user:pass@forms.example.test/embed"));
        using var userInfo = await SendAuthenticatedAsync(Route());
        await Assert.That(userInfo.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        GivenDescriptor(Descriptor(url: "https://127.0.0.1/embed"));
        using var loopback = await SendAuthenticatedAsync(Route());
        await Assert.That(loopback.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        GivenDescriptor(Descriptor(url: "https://[fc00::1]/embed"));
        using var uniqueLocal = await SendAuthenticatedAsync(Route());
        await Assert.That(uniqueLocal.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        GivenDescriptor(Descriptor(url: "https://[::ffff:127.0.0.1]/embed"));
        using var mappedLoopback = await SendAuthenticatedAsync(Route());
        await Assert.That(mappedLoopback.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        GivenDescriptor(Descriptor(url: "https://[::ffff:192.168.1.1]/embed"));
        using var mappedPrivate = await SendAuthenticatedAsync(Route());
        await Assert.That(mappedPrivate.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task NormalBffResponse_RetainsGlobalXFrameOptionsDeny()
    {
        using var response = await _client.GetAsync("/bff/theme");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues("X-Frame-Options").Single()).IsEqualTo("DENY");
    }

    [Test]
    public async Task EmbedHost_ValidRequest_CallsOnlyGeneratedFirstPartyApiClient()
    {
        GivenDescriptor(Descriptor());

        using var response = await SendAuthenticatedAsync(Route());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await _apiClient.Received(1).LaunchAuthenticatedRegistrationProviderAttemptAsync(
            EventId,
            OrderId,
            Arg.Any<string>(),
            Arg.Is<LaunchRegistrationProviderAttemptRequest>(request =>
                request.RequirementId == RequirementId && request.ChannelId == ChannelId &&
                request.BindingId == BindingId && request.FormId == FormId && request.FormVersionId == FormVersionId),
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GuestLaunch_ForwardsCapabilityWithoutExposingItInOpaqueEmbedUrl()
    {
        const string capability = "guest-secret-capability";
        _apiClient.LaunchGuestRegistrationProviderAttemptAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<LaunchRegistrationProviderAttemptRequest>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Descriptor()));
        string token = await IssueAntiforgeryCookieAsync(null);
        using var request = new HttpRequestMessage(HttpMethod.Post, Route())
        {
            Content = JsonContent.Create(LaunchRequest() with { GuestCapability = capability })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        using HttpResponseMessage response = await _client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        RegistrationProviderBffTicket? launch = await response.Content.ReadFromJsonAsync<RegistrationProviderBffTicket>();
        await Assert.That(launch!.EmbedUrl).StartsWith("/bff/registration-provider-embed/launches/");
        await Assert.That(launch.EmbedUrl).DoesNotContain(capability);
        await _apiClient.Received(1).LaunchGuestRegistrationProviderAttemptAsync(
            EventId, OrderId, Arg.Any<string>(), Arg.Any<LaunchRegistrationProviderAttemptRequest>(), capability,
            null, null, Arg.Any<CancellationToken>());
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(string path)
    {
        string authentication = TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid());
        string token = await IssueAntiforgeryCookieAsync(authentication);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(LaunchRequest())
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);
        request.Headers.Add("X-CSRF-TOKEN", token);
        HttpResponseMessage launch = await _client.SendAsync(request);
        if (!launch.IsSuccessStatusCode)
        {
            return launch;
        }

        RegistrationProviderBffTicket? result = await launch.Content.ReadFromJsonAsync<RegistrationProviderBffTicket>();
        launch.Dispose();
        return await _client.GetAsync(result!.EmbedUrl);
    }

    private void GivenDescriptor(HalResourceOfNativeRegistrationProviderLaunchDescriptorDto descriptor)
    {
        _apiClient.LaunchAuthenticatedRegistrationProviderAttemptAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<LaunchRegistrationProviderAttemptRequest>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(descriptor));
    }

    private static string GetRawHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.NonValidated.TryGetValues(name, out var values) ||
            response.Content.Headers.NonValidated.TryGetValues(name, out values))
        {
            return values.Single();
        }

        return string.Empty;
    }

    private static HalResourceOfNativeRegistrationProviderLaunchDescriptorDto Descriptor(
        Guid? requirementId = null,
        Guid? channelId = null,
        Guid? bindingId = null,
        Guid? formId = null,
        Guid? formVersionId = null,
        string mode = "embed",
        bool available = true,
        string url = "https://forms.example.test/embed/form-1",
        string title = "Provider registration") => new()
        {
            AdditionalProperties =
            {
                ["requirementId"] = requirementId ?? RequirementId,
                ["channelId"] = channelId ?? ChannelId,
                ["bindingId"] = bindingId ?? BindingId,
                ["formId"] = formId ?? FormId,
                ["formVersionId"] = formVersionId ?? FormVersionId,
                ["mode"] = mode,
                ["available"] = available,
                ["url"] = url,
                ["title"] = title
            }
        };

    private static RegistrationProviderBffLaunch LaunchRequest() => new(
        EventId, OrderId, RequirementId, ChannelId, BindingId, FormId, FormVersionId, null);

    private static string Route() => "/bff/registration-provider-embed/launches";

    private async Task<string> IssueAntiforgeryCookieAsync(string? authentication)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/status");
        if (authentication is not null)
        {
            request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);
        }
        using var response = await _client.SendAsync(request);
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var values)).IsTrue();
        string token = values!.Select(ReadXsrfToken).First(value => !string.IsNullOrWhiteSpace(value))!;
        return token;
    }

    private static string? ReadXsrfToken(string setCookie)
    {
        const string prefix = "XSRF-TOKEN=";
        if (!setCookie.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        int end = setCookie.IndexOf(';', prefix.Length);
        return Uri.UnescapeDataString(end < 0 ? setCookie[prefix.Length..] : setCookie[prefix.Length..end]);
    }

    [GeneratedRegex("src=\\\"[^\\\"]*\\\"[^>]*<", RegexOptions.CultureInvariant)]
    private static partial Regex DangerousAttributeBreakout();
}
