using System.Net;
using System.Globalization;
using Event.Standalone.IntegrationTests.Fixtures;
using Explore.API.Configuration;
using Explore.API.Hosting;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.ServiceDefaults.Configuration;
using Explore.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Event.Standalone.IntegrationTests;

[NotInParallel]
public sealed class EffectiveHostSecurityTests
{
    [Test]
    [Arguments(true, "203.0.113.7", "https", "events.example.test", HttpStatusCode.OK)]
    [Arguments(false, "203.0.113.7", "https", "events.example.test", HttpStatusCode.BadRequest)]
    [Arguments(true, "invalid-peer", "https", "events.example.test", HttpStatusCode.BadRequest)]
    [Arguments(true, "203.0.113.7", "javascript", "events.example.test", HttpStatusCode.BadRequest)]
    [Arguments(true, "203.0.113.7", "https", "attacker.example.test", HttpStatusCode.BadRequest)]
    [Arguments(true, "203.0.113.7, 203.0.113.8", "https", "events.example.test", HttpStatusCode.BadRequest)]
    public async Task EffectiveAuthorityPrecedesHstsAndRedirect(
        bool trusted, string peer, string scheme, string host, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Staging" });
        builder.WebHost.UseTestServer();
        builder.Services.AddEarlyRequestAdmission(builder.Configuration, builder.Environment);
        builder.Services.Configure<EffectiveHostAuthorityOptions>(options =>
            options.CanonicalOrigin = "https://events.example.test");
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
            new ForwardedHeadersTrustOptions { TrustLoopbackProxy = true }.ApplyTo(options,
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto));
        builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
        await using var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(trusted ? "127.0.0.1" : "192.0.2.1");
            return next(context);
        });
        app.UseEffectiveHostAuthority();
        app.UseHsts();
        app.UseHttpsRedirection();
        app.MapGet("/", (HttpContext context) => Results.Text(
            $"{context.Request.Scheme}|{context.Request.Host}|{context.Connection.RemoteIpAddress}"));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://backend.invalid/");
        request.Headers.Add("X-Forwarded-For", peer);
        request.Headers.Add("X-Forwarded-Proto", scheme);
        request.Headers.Add("X-Forwarded-Host", host);
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        if (expected == HttpStatusCode.OK)
        {
            await Assert.That(await response.Content.ReadAsStringAsync())
                .IsEqualTo("https|events.example.test|203.0.113.7");
            await Assert.That(response.Headers.Contains("Strict-Transport-Security")).IsTrue();
        }
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    [Arguments("attacker.example.test")]
    [Arguments("events.example.test.attacker.test")]
    public async Task UnknownHostsNeverReachRedirectOrEndpoint(string host)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddEarlyRequestAdmission(builder.Configuration, builder.Environment);
        builder.Services.Configure<EffectiveHostAuthorityOptions>(options =>
            options.CanonicalOrigin = "https://events.example.test");
        builder.Services.Configure<ForwardedHeadersOptions>(_ => { });
        await using var app = builder.Build();
        app.UseEffectiveHostAuthority();
        app.Run(context => throw new InvalidOperationException("Unapproved host reached the application."));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync($"http://{host}/");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    public async Task OwnershipAttestationsExpireAndBindExactTenantAndHost()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var tenant = Guid.CreateVersion7();
        var proof = new TenantDomainOwnershipAttestation
        {
            Host = "community.example.test",
            TenantId = tenant,
            VerifiedAt = now,
            ExpiresAt = now.AddHours(1),
            EvidenceReference = "operator-proof-42"
        };
        await Assert.That(proof.IsCurrent("community.example.test", tenant, now)).IsTrue();
        await Assert.That(proof.IsCurrent("community.example.test", Guid.CreateVersion7(), now)).IsFalse();
        await Assert.That(proof.IsCurrent("other.example.test", tenant, now)).IsFalse();
        await Assert.That(proof.IsCurrent("community.example.test", tenant, now.AddHours(1))).IsFalse();
        proof.ExpiresAt = now.AddDays(2);
        await Assert.That(proof.IsCurrent("community.example.test", tenant, now)).IsFalse();
        proof.ExpiresAt = now.AddHours(1);
        proof.EvidenceReference = "";
        await Assert.That(proof.IsCurrent("community.example.test", tenant, now)).IsFalse();
    }

    [Test]
    public async Task OperationalReportsRequireTheActualPrivateListenerWhileLivenessHasNoBody()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OperationalEndpoints:Address"] = "127.0.0.1",
            ["OperationalEndpoints:Port"] = "9091"
        });
        builder.AddServiceDefaults();
        builder.Services.AddHealthChecks().AddCheck("diagnostic-canary",
            () => HealthCheckResult.Healthy("tenant-diagnostic-canary"), ["ready"]);
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        foreach (string path in new[] { "/health", "/metrics" })
        {
            var denied = await app.GetTestServer().SendAsync(context =>
            {
                context.Request.Path = path;
                context.Request.Host = new HostString("localhost", 9091);
                context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
                context.Connection.LocalIpAddress = IPAddress.Loopback;
                context.Connection.LocalPort = 8080;
            });
            await Assert.That(denied.Response.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
            using var deniedBody = new StreamReader(denied.Response.Body);
            await Assert.That(await deniedBody.ReadToEndAsync()).IsEmpty();
        }
        var privateReadiness = await app.GetTestServer().SendAsync(context =>
        {
            context.Request.Path = "/health";
            context.Connection.LocalIpAddress = IPAddress.Loopback;
            context.Connection.LocalPort = 9091;
        });
        await Assert.That(privateReadiness.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        using var privateBody = new StreamReader(privateReadiness.Response.Body);
        await Assert.That(await privateBody.ReadToEndAsync()).Contains("tenant-diagnostic-canary");
        using var client = app.GetTestClient();
        using var alive = await client.GetAsync("/alive");
        await Assert.That(alive.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await alive.Content.ReadAsStringAsync()).IsEmpty();
    }

    [Test]
    public async Task NativeStagingCombinedHostEnforcesCurrentDomainOwnershipAndRevocation()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        var now = TimeProvider.System.GetUtcNow();
        var clock = new FixedTime(now);
        deployment.SetEnvironment("EffectiveHostAuthority__CanonicalOrigin", "https://events.example.test");
        deployment.SetEnvironment("EffectiveHostAuthority__SubdomainBaseDomains__0", "events.example.test");
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__Host", "community.example.test");
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__TenantId", PlatformDefaults.DefaultTenantId.ToString("D"));
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__VerifiedAt", now.AddHours(-1).ToString("O", CultureInfo.InvariantCulture));
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__ExpiresAt", now.AddHours(1).ToString("O", CultureInfo.InvariantCulture));
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__EvidenceReference", "operator-proof-42");
        deployment.SetEnvironment("ForwardedHeadersTrust__TrustLoopbackProxy", "true");
        await using var native = deployment.CreateHost();
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(clock)));
        using var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://events.example.test"),
            AllowAutoRedirect = false
        });
        await Assert.That(host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName).IsEqualTo("Staging");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var tenant = await db.Tenants.SingleAsync(row => row.Id == PlatformDefaults.DefaultTenantId);
            tenant.TenantStatusId = (int)TenantStatusEnum.Active;
            db.TenantSettingOverrides.AddRange(
                new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    Tenant = tenant,
                    SettingKey = GovernanceSettingKeys.Domains.TenantCustomDomain,
                    Value = SettingValueSerializer.Serialize("community.example.test")
                },
                new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    Tenant = tenant,
                    SettingKey = GovernanceSettingKeys.Domains.TenantSubdomain,
                    Value = SettingValueSerializer.Serialize("community")
                });
            foreach (var key in new[] { GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled,
                         GovernanceSettingKeys.Routing.ResolverSubdomainEnabled, GovernanceSettingKeys.Domains.AllowTenantCustomDomain })
            {
                var setting = await db.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null)
                    db.SystemSettings.Add(new SystemSetting
                    {
                        Id = Guid.CreateVersion7(),
                        SettingKey = key,
                        Value = "true",
                        ValueType = SettingValueType.Boolean
                    });
                else setting.Value = "true";
            }
            var baseSetting = await db.SystemSettings.SingleOrDefaultAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Domains.InstanceBaseDomain);
            if (baseSetting is null)
                db.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = GovernanceSettingKeys.Domains.InstanceBaseDomain,
                    Value = SettingValueSerializer.Serialize("events.example.test"),
                    ValueType = SettingValueType.String
                });
            else baseSetting.Value = SettingValueSerializer.Serialize("events.example.test");
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IResolverConfigService>().InvalidateCache();
        }
        foreach (var domain in new[] { "community.example.test", "community.events.example.test" })
        {
            using var accepted = await client.GetAsync($"https://{domain}/alive");
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(accepted.Headers.Contains("Strict-Transport-Security")).IsTrue();
            await Assert.That(await accepted.Content.ReadAsStringAsync()).IsEmpty();
        }
        using (var unknown = await client.GetAsync("https://unreserved.events.example.test/alive"))
            await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var nested = await client.GetAsync("https://community.extra.events.example.test/alive"))
            await Assert.That(nested.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        foreach (var path in new[] { "/health", "/metrics", "/health/webhooks/local", "/health/webhooks/svix", "/health/webhooks/coop-effects" })
        {
            using var denied = await client.GetAsync(path);
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        clock.UtcNow = now.AddHours(1);
        using (var expired = await client.GetAsync("https://community.example.test/alive"))
            await Assert.That(expired.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        clock.UtcNow = now;

        // A successful earlier request must not create positive-cache authority.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var tenant = await db.Tenants.SingleAsync(row => row.Id == PlatformDefaults.DefaultTenantId);
            tenant.TenantStatusId = (int)TenantStatusEnum.Provisioning;
            await db.SaveChangesAsync();
        }
        using var revoked = await client.GetAsync("https://community.example.test/alive");
        await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
