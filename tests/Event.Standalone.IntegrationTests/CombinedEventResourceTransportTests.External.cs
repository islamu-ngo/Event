using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Event.Standalone.IntegrationTests.Fixtures;
using Event.Web.BffHosting.Security;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Standalone.IntegrationTests;

public sealed partial class CombinedEventResourceTransportTests
{
    [Test]
    public async Task NativeCombinedExternalAccessOnlyReturnsLocationUntilBrowserNavigates()
    {
        string marker = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var externalArrival = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var landingArrival = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certificateRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        certificateRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(certificateRequest.PublicKey, false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("resources.example.org");
        certificateRequest.CertificateExtensions.Add(names.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));
        var externalBuilder = WebApplication.CreateBuilder();
        externalBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(certificate)));
        await using var external = externalBuilder.Build();
        external.MapGet($"/visit/{marker}", (HttpContext context) =>
        {
            externalArrival.TrySetResult(context.Request.Headers.ToDictionary(
                entry => entry.Key, entry => entry.Value.ToString(), StringComparer.OrdinalIgnoreCase));
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = $"/landed/{marker}";
            return Task.CompletedTask;
        });
        external.MapGet($"/landed/{marker}", (HttpContext context) =>
        {
            landingArrival.TrySetResult(context.Request.Headers.ToDictionary(
                entry => entry.Key, entry => entry.Value.ToString(), StringComparer.OrdinalIgnoreCase));
            return Results.Text(marker);
        });
        await external.StartAsync();
        string address = external.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First(value => value.StartsWith("https://", StringComparison.Ordinal));
        var origin = new Uri(address.Replace("127.0.0.1", "resources.example.org", StringComparison.Ordinal));
        string destination = new Uri(origin, $"/visit/{marker}?ticket={marker}").AbsoluteUri;

        using var deployment = new NativeEmailOptionalStandaloneFixture();
        await using var host = deployment.CreateHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        string password = NativeEmailOptionalStandaloneFixture.NewPassword();
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password = deployment.InitialPassword }))
        {
            login.EnsureSuccessStatusCode();
            using var challenge = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                challenge.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString());
        }
        using (var replace = await client.PostAsJsonAsync("/api/auth/local/credential-replacement", new { newPassword = password }))
            replace.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;
        string bearer;
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password }))
        {
            login.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            bearer = body.RootElement.GetProperty("token").GetString()!;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        await ActivateResourceDirectoryAsync(client);
        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7();
        string tenantSlug;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EnableTenantFilterBypass("Seed one native combined external destination for redirect transport.");
            tenantSlug = await db.Tenants.Where(row => row.Id == PlatformDefaults.DefaultTenantId)
                .Select(row => row.Slug).SingleAsync();
            var user = await db.Users.SingleAsync(row => row.Id == deployment.Subject);
            var actor = await db.Actors.SingleAsync(row => row.UserId == user.Id);
            if (!await db.TenantUsers.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == user.Id))
                db.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                    UserId = user.Id, User = user, ActorId = actor.Id,
                    StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
                });
            var policy = await db.SystemSettings.SingleOrDefaultAsync(row =>
                row.SettingKey == GovernanceSettingKeys.EventResources.ExternalOrigins);
            string origins = JsonSerializer.Serialize(new[] { origin.GetLeftPart(UriPartial.Authority) });
            if (policy is null)
                db.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.EventResources.ExternalOrigins, Value = origins,
                    ValueType = SettingValueType.Json, Category = "EventResources"
                });
            else policy.Value = origins;
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Combined external access", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(DateTime.UtcNow);
            db.Events.Add(parent);
            var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata { Title = "External resource", PublicTitle = "Visit",
                    Kind = EventResourceKindEnum.GeneralDocument, DisclosureMode = EventResourceDisclosureModeEnum.Public },
                EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                    EventResourceAudienceKindEnum.Public)], user.Id, DateTime.UtcNow);
            var protector = scope.ServiceProvider.GetRequiredService<IEventResourceDestinationProtector>();
            resource.SetExternalDestination(protector.Protect(destination, PlatformDefaults.DefaultTenantId,
                    resourceId, protector.CurrentVersion), protector.CurrentVersion,
                origin.GetLeftPart(UriPartial.Authority), resource.ConcurrencyStamp, user.Id, DateTime.UtcNow);
            resource.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                false, true, null, false, new(null, null, null, null)), true,
                resource.ConcurrencyStamp, user.Id, DateTime.UtcNow);
            db.EventResources.Add(resource);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = null;
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        void AcceptCookies(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (string value in values)
                {
                    string[] pair = value.Split(';', 2)[0].Split('=', 2);
                    cookies[pair[0]] = pair[1];
                }
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", Uri.UnescapeDataString(cookies["XSRF-TOKEN"]));
        }
        using (var csrf = await client.GetAsync("/bff/me")) AcceptCookies(csrf);
        using (var browserLogin = await client.PostAsJsonAsync("/bff/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password, returnUrl = "/" }))
        {
            browserLogin.EnsureSuccessStatusCode();
            AcceptCookies(browserLogin);
        }
        using (var csrf = await client.GetAsync("/bff/me"))
        {
            csrf.EnsureSuccessStatusCode();
            AcceptCookies(csrf);
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/eventresource/{resourceId:D}/access");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", marker);
        request.Headers.Add("Cookie", string.Join("; ", cookies.Select(pair => $"{pair.Key}={pair.Value}")) +
            $"; browser-{marker}=secret");
        request.Headers.Add(EventBffHeaderNames.TenantSlug, tenantSlug);
        request.Headers.Add(EventBffHeaderNames.TenantId, PlatformDefaults.DefaultTenantId.ToString("D"));
        request.Headers.Add(EventBffHeaderNames.SetupSecret, marker);
        request.Headers.Add(EventBffHeaderNames.SupportAccessMode, "Read");
        request.Headers.Add("X-CSRF-TOKEN", marker);
        request.Headers.Add("X-API-Key", marker);
        request.Headers.Add("X-Control-Plane-Key", marker);
        request.Headers.Add("X-Correlation-ID", marker);
        request.Headers.Add("X-Forwarded-Host", "localhost");
        request.Headers.Add("X-Forwarded-For", "203.0.113.42");
        using var redirect = await client.SendAsync(request).WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.That(redirect.StatusCode).IsEqualTo(HttpStatusCode.Redirect)
            .Because(await redirect.Content.ReadAsStringAsync());
        await Assert.That(redirect.Headers.Location!.AbsoluteUri).IsEqualTo(destination);
        await Assert.That(redirect.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(redirect.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
        await Assert.That(await redirect.Content.ReadAsStringAsync()).DoesNotContain(marker);
        await Assert.That(externalArrival.Task.IsCompleted).IsFalse();

        using var navigationHandler = new SocketsHttpHandler { AllowAutoRedirect = false };
        navigationHandler.SslOptions.RemoteCertificateValidationCallback = (_, presented, _, _) =>
            presented?.GetCertHashString() == certificate.Thumbprint;
        navigationHandler.ConnectCallback = async (context, cancellationToken) =>
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(IPAddress.Loopback, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
        using var navigation = new HttpClient(navigationHandler);
        using var firstHop = await navigation.GetAsync(redirect.Headers.Location).WaitAsync(TimeSpan.FromSeconds(10));
        var headers = await externalArrival.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(firstHop.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(firstHop.Headers.Location!.OriginalString).IsEqualTo($"/landed/{marker}");
        string[] forbiddenHeaders = ["Authorization", "Cookie", EventBffHeaderNames.TenantSlug,
            EventBffHeaderNames.TenantId, EventBffHeaderNames.SetupSecret,
            EventBffHeaderNames.SupportAccessMode, "X-CSRF-TOKEN", "X-API-Key",
            "X-Control-Plane-Key", "X-Correlation-ID", "X-Forwarded-Host", "X-Forwarded-For", "Forwarded"];
        foreach (string forbidden in forbiddenHeaders)
            await Assert.That(headers.ContainsKey(forbidden)).IsFalse();
        using var landed = await navigation.GetAsync(new Uri(origin, firstHop.Headers.Location))
            .WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(landed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await landed.Content.ReadAsStringAsync()).IsEqualTo(marker);
        var landingHeaders = await landingArrival.Task.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (string forbidden in forbiddenHeaders)
            await Assert.That(landingHeaders.ContainsKey(forbidden)).IsFalse();
    }
}
