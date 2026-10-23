using System.Globalization;
using System.Net;
using System.Threading.Channels;
using Event.Standalone.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Tenants;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Services;
using Explore.ServiceDefaults;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Standalone.IntegrationTests;

[NotInParallel]
public sealed class EarlyAdmissionTenantLookupTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Test]
    public async Task SaturatedNativeStagingHostDoesNotAcquireAnotherPersistedDomainLookup()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        var now = TimeProvider.System.GetUtcNow();
        var signal = new LookupSignal();
        deployment.SetEnvironment("EffectiveHostAuthority__CanonicalOrigin", "https://events.example.test");
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__Host", "community.example.test");
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__TenantId", PlatformDefaults.DefaultTenantId.ToString("D"));
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__VerifiedAt", now.AddHours(-1).ToString("O", CultureInfo.InvariantCulture));
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__ExpiresAt", now.AddHours(1).ToString("O", CultureInfo.InvariantCulture));
        deployment.SetEnvironment("EffectiveHostAuthority__TenantDomains__0__EvidenceReference", "native-ownership-proof");
        await using var native = deployment.CreateHost();
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new PinnedClock(now));
            services.RemoveAll<ITenantLookupSource>();
            services.AddScoped<ITenantLookupSource>(provider => new ObservedLookup(
                ActivatorUtilities.CreateInstance<TenantLookupSource>(provider), signal));
        }));
        using var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://community.example.test"),
            AllowAutoRedirect = false
        });
        await Assert.That(host.Services.GetRequiredService<EarlyRequestAdmission>().IsEnabled).IsTrue();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var tenant = await db.Tenants.SingleAsync(row => row.Id == PlatformDefaults.DefaultTenantId);
            tenant.TenantStatusId = (int)TenantStatusEnum.Active;
            db.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                SettingKey = GovernanceSettingKeys.Domains.TenantCustomDomain,
                Value = SettingValueSerializer.Serialize("community.example.test")
            });
            foreach (var key in new[] { GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled,
                         GovernanceSettingKeys.Domains.AllowTenantCustomDomain })
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
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IResolverConfigService>().InvalidateCache();
        }

        Task<HttpResponseMessage>[] admitted = Enumerable.Range(0, 32)
            .Select(_ => client.GetAsync("/alive")).ToArray();
        try
        {
            for (int i = 0; i < admitted.Length; i++)
                await signal.Entered.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
            foreach (bool malformedForwarding in new[] { false, true })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/alive");
                request.Headers.Authorization = new("Bearer", "invalid-token-canary");
                if (malformedForwarding) request.Headers.Add("X-Forwarded-For", "invalid-peer-canary");
                using var rejected = await client.SendAsync(request).WaitAsync(Deadline);
                await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
                await Assert.That(Volatile.Read(ref signal.Count)).IsEqualTo(32);
            }
        }
        finally
        {
            signal.Release.TrySetResult();
            foreach (var response in await Task.WhenAll(admitted).WaitAsync(Deadline))
            {
                using (response)
                    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            }
        }
        using var control = await client.GetAsync("/alive").WaitAsync(Deadline);
        await Assert.That(control.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(Volatile.Read(ref signal.Count)).IsEqualTo(33);
    }

    private sealed class LookupSignal
    {
        public readonly Channel<int> Entered = Channel.CreateBounded<int>(64);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count;
    }

    private sealed class ObservedLookup(TenantLookupSource inner, LookupSignal signal) : ITenantLookupSource
    {
        public Task<IReadOnlyList<TenantLookupRecord>> GetTenantLookupsAsync(CancellationToken cancellationToken = default) =>
            inner.GetTenantLookupsAsync(cancellationToken);

        public async Task<Guid?> GetCurrentDomainTenantAsync(
            string domain, bool customDomain, CancellationToken cancellationToken = default)
        {
            int count = Interlocked.Increment(ref signal.Count);
            await signal.Entered.Writer.WriteAsync(count, cancellationToken);
            await signal.Release.Task.WaitAsync(cancellationToken);
            return await inner.GetCurrentDomainTenantAsync(domain, customDomain, cancellationToken);
        }
    }

    private sealed class PinnedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
