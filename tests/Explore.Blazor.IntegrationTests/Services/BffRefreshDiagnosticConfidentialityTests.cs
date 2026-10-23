using System.Text.Json;
using Event.Web.BffHosting.Authentication;
using Explore.Blazor.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class BffRefreshDiagnosticConfidentialityTests
{
    [Test]
    [Arguments("private")]
    [Arguments("invalid_grant")]
    [Arguments("transport")]
    [Arguments("options")]
    [Arguments("shape")]
    [Arguments("oversize")]
    [Arguments("chunked")]
    [Arguments("empty_token")]
    [Arguments("success")]
    public async Task ActualRefreshKeepsPrivateMaterialOutOfConfiguredSinkAndRejection(string failure)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        string refreshToken = Guid.CreateVersion7().ToString("N");
        string replacementAccessToken = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var serilog = new LoggerConfiguration().MinimumLevel.Debug()
            .Enrich.FromLogContext().WithBffDiagnosticConfidentiality()
            .WriteTo.Sink(collector).CreateLogger();
        using var logs = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
        var session = Substitute.For<IEventBffCookieSessionHandler>();
        session.ValidatePrincipalAsync(Arg.Any<CookieValidatePrincipalContext>()).Returns(true);
        string? rejectedReason = null;
        session.OnTokenRefreshRejectedAsync(Arg.Any<CookieValidatePrincipalContext>(),
                Arg.Any<EventBffSessionRejectionCategory>(), Arg.Any<string>())
            .Returns(call =>
            {
                rejectedReason = call.ArgAt<string>(2);
                return Task.CompletedTask;
            });
        var monitor = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        monitor.Get(canary).Returns(_ => failure == "options"
            ? throw new PrivateCredentialFailure(canary)
            : new OpenIdConnectOptions
            {
                ClientId = canary,
                ClientSecret = canary,
                ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration { TokenEndpoint = $"https://provider.example.test/{canary}" })
            });
        using var transport = new RefreshTransport(failure, canary, replacementAccessToken);
        using var httpClient = new HttpClient(transport);
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(Arg.Any<string>()).Returns(httpClient);
        var authentication = Substitute.For<IAuthenticationService>();
        authentication.SignOutAsync(Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<AuthenticationProperties>())
            .Returns(_ => Task.FromException(new PrivateCredentialFailure(canary)));
        using var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = "GET";
        http.Request.Path = "/auth/status";
        http.Request.Headers.Accept = "text/html";
        var properties = new AuthenticationProperties();
        properties.Items[EventBffAuthenticationConstants.OidcSchemePropertyKey] = canary;
        // An unreadable token takes the real refresh branch without clock-dependent expiry waits.
        properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = canary },
            new AuthenticationToken { Name = "refresh_token", Value = refreshToken }
        ]);
        var context = new CookieValidatePrincipalContext(http,
            new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(),
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", canary)], "cookie")),
                properties, CookieAuthenticationDefaults.AuthenticationScheme));
        var events = new EventBffTokenRefreshCookieEvents(monitor, clients, session,
            logs.CreateLogger<EventBffTokenRefreshCookieEvents>());
        using var scope = logs.CreateLogger<BffRefreshDiagnosticConfidentialityTests>()
            .BeginScope(new Dictionary<string, object> { ["PrivateScope"] = canary, ["Reason"] = canary });

        await events.ValidatePrincipal(context);

        await Assert.That(transport.CallCount).IsEqualTo(failure == "options" ? 0 : 1);
        if (failure == "success")
        {
            await Assert.That(context.ShouldRenew).IsTrue();
            await Assert.That(context.Properties.GetTokenValue("access_token")).IsEqualTo(replacementAccessToken);
            await Assert.That(context.Properties.GetTokenValue("refresh_token")).IsEqualTo(refreshToken);
            await Assert.That(rejectedReason).IsNull();
        }
        else
        {
            await Assert.That(context.Principal).IsNull();
            await Assert.That(http.Items[EventBffAuthenticationConstants.TokenRefreshRejectedItemKey]).IsEqualTo(true);
            string expected = failure switch
            {
                "invalid_grant" => "invalid_grant",
                "options" => "oidc_options_error",
                "transport" or "oversize" or "chunked" => "refresh_exception",
                "empty_token" => "missing_access_token",
                _ => "token_endpoint_rejected"
            };
            await Assert.That(rejectedReason).IsEqualTo(expected);
            await Assert.That(http.Response.Headers.Location.ToString()).Contains("reason=" + expected);
        }

        await Assert.That(collector.Events.Count > 0).IsTrue();
        foreach (LogEvent exported in collector.Events)
        {
            await Assert.That(exported.Exception).IsNull();
            string serialized = exported.RenderMessage() + string.Join(' ', exported.Properties.Select(p => p.Key + p.Value));
            await Assert.That(serialized).DoesNotContain(canary);
            await Assert.That(serialized).DoesNotContain(refreshToken);
            await Assert.That(serialized).DoesNotContain(replacementAccessToken);
            await Assert.That(serialized).DoesNotContain(nameof(PrivateCredentialFailure));
            await Assert.That(exported.Properties.ContainsKey("PrivateScope")).IsFalse();
        }
    }

    [Test]
    [Arguments("Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectHandler")]
    [Arguments("Microsoft.IdentityModel.Tokens")]
    [Arguments("System.Net.Http.HttpClient.TokenRefresh")]
    [Arguments("Yarp.ReverseProxy.Forwarder")]
    [Arguments("Microsoft.Extensions.Hosting.Internal.Host")]
    public async Task SplitSinkExcludesFrameworkCopiesButRetainsNativeOutcome(string category)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var collector = new Collector();
        using var serilog = new LoggerConfiguration().Enrich.FromLogContext()
            .WithBffDiagnosticConfidentiality().WriteTo.Sink(collector).CreateLogger();
        serilog.ForContext("SourceContext", category).Error(new PrivateCredentialFailure(canary), "{PrivateValue}", canary);
        serilog.ForContext("SourceContext", typeof(EventBffTokenRefreshCookieEvents).FullName)
            .Warning("Refresh failed. FailureCategory={FailureCategory}", "refresh_exception");

        await Assert.That(collector.Events.Count).IsEqualTo(1);
        await Assert.That(collector.Events.Single().Exception).IsNull();
        await Assert.That(collector.Events.Single().RenderMessage()).DoesNotContain(canary);
    }

    private sealed class RefreshTransport(string failure, string canary, string replacement) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (failure == "transport")
                throw new PrivateCredentialFailure(canary);
            string body = failure switch
            {
                "success" => JsonSerializer.Serialize(new { access_token = replacement }),
                "empty_token" => "{\"access_token\":null}",
                "shape" => "{\"error\":{\"private\":\"" + canary + "\"}}",
                "oversize" => new string('x', 64 * 1024 + 1),
                _ => JsonSerializer.Serialize(new { error = failure == "invalid_grant" ? failure : canary })
            };
            return Task.FromResult(new HttpResponseMessage(failure is "success" or "empty_token" ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = failure == "chunked"
                    ? new UnknownLengthContent(new byte[64 * 1024 + 1])
                    : new StringContent(body)
            });
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private sealed class Collector : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class PrivateCredentialFailure(string canary) : HttpRequestException(canary);
}
