using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Localization;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Localization;
using Explore.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeLocalizationHttpTests
{
    [Test]
    public async Task NativeCohort_PreservesAuthorityOfflineBundlesGovernanceAndTenantSecretStatus()
    {
        var directory = Directory.CreateTempSubdirectory("native-localization-");
        try
        {
            var liveProviderRequests = 0;
            var environment = Substitute.For<IWebHostEnvironment>();
            environment.ContentRootPath.Returns(directory.FullName);
            await using var baseFactory = new AuthenticatedWebApplicationFactory
            {
                AuthorizationProviderOverride = new StubAuthorizationProvider()
            };
            await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<OfflineTranslationProvider>();
                services.AddSingleton(provider => new OfflineTranslationProvider(
                    provider.GetRequiredService<ILogger<OfflineTranslationProvider>>(), environment));
                services.RemoveAll<IBundleFileWriter>();
                services.AddScoped<IBundleFileWriter>(provider => new BundleFileWriter(
                    environment, provider.GetRequiredService<ILogger<BundleFileWriter>>()));
                services.AddHttpClient("TolgeeClient").ConfigurePrimaryHttpMessageHandler(
                    () => new RejectLiveProvider(() => Interlocked.Increment(ref liveProviderRequests)));
                services.AddHttpClient("WeblateClient").ConfigurePrimaryHttpMessageHandler(
                    () => new RejectLiveProvider(() => Interlocked.Increment(ref liveProviderRequests)));
            }));
            var adminId = Guid.CreateVersion7();
            var memberId = Guid.CreateVersion7();
            var foreignTenantId = Guid.CreateVersion7();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                var admin = NewUser(adminId, "localization-admin");
                db.Users.AddRange(admin, NewUser(memberId, "localization-member"));
                var role = await db.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
                db.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = admin.Id,
                    User = admin,
                    RoleId = role.Id,
                    Role = role
                });
                db.SecretBindings.Add(Binding(SecretScope.Tenant, foreignTenantId));
                await db.SaveChangesAsync();
            }

            using var anonymous = factory.CreateClient();
            using var adminClient = Client(factory, TestAuthHandler.CreateAuthHeaderValue(adminId));
            using var forgedAdmin = Client(factory, TestAuthHandler.CreateInstanceAdminHeaderValue(memberId));
            using (var unauthorized = await anonymous.PostAsync("/api/admin/localization/test-connection", null))
                await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            using (var denied = await forgedAdmin.PostAsync("/api/admin/localization/test-connection", null))
                await ProblemAsync(denied);
            using (var denied = await forgedAdmin.PostAsJsonAsync("/api/admin/localization/bundle", Bundle("denied")))
                await ProblemAsync(denied);
            using (var denied = await forgedAdmin.PostAsync("/api/admin/localization/export-from-tms?languageCode=fr", null))
                await ProblemAsync(denied);
            using (var denied = await forgedAdmin.PatchAsJsonAsync("/api/admin/localization/governance", Governance()))
                await ProblemAsync(denied);
            await Assert.That(Directory.GetFiles(directory.FullName, "*", SearchOption.AllDirectories).Length).IsEqualTo(0);

            var before = await ConfigurationAsync(adminClient);
            await Assert.That(before.GetProperty("tmsApiKeyConfigured").GetBoolean()).IsFalse();
            await Assert.That(before.TryGetProperty("tmsApiKey", out _)).IsFalse();
            using (var invalid = await adminClient.PatchAsJsonAsync("/api/admin/localization/governance", new UpdateLocalizationGovernanceDto()))
                await ProblemAsync(invalid);
            using (var governance = await adminClient.PatchAsJsonAsync("/api/admin/localization/governance", Governance()))
                await SuccessAsync(governance);
            var configured = await ConfigurationAsync(adminClient);
            await Assert.That(configured.GetProperty("defaultLanguage").GetString()).IsEqualTo("fr");
            await Assert.That(configured.GetProperty("forceOfflineMode").GetBoolean()).IsTrue();
            await Assert.That(configured.GetProperty("clientPickerEnabled").GetBoolean()).IsFalse();
            await Assert.That(configured.GetProperty("tmsProvider").GetString()).IsEqualTo("None");

            using (var imported = await adminClient.PostAsJsonAsync("/api/admin/localization/bundle", Bundle("Bonjour")))
                await SuccessAsync(imported);
            var bundlePath = Path.Combine(directory.FullName, "App_Data", "Localization", "Bundles", "fr.json");
            var importedBytes = await File.ReadAllBytesAsync(bundlePath);
            using (var persisted = JsonDocument.Parse(importedBytes))
                await Assert.That(persisted.RootElement.GetProperty("ui.native_cohort.greeting").GetString()).IsEqualTo("Bonjour");
            using (var probe = await adminClient.PostAsync("/api/admin/localization/test-connection", null))
            {
                await SuccessAsync(probe);
                await Assert.That((await JsonAsync(probe)).GetProperty("id").GetGuid()).IsEqualTo(Guid.Empty);
            }
            await Assert.That((await File.ReadAllBytesAsync(bundlePath)).SequenceEqual(importedBytes)).IsTrue();
            using (var invalid = await adminClient.PostAsJsonAsync("/api/admin/localization/bundle", Bundle("invalid") with { LanguageCode = "zz" }))
                await ProblemAsync(invalid);
            await Assert.That((await File.ReadAllBytesAsync(bundlePath)).SequenceEqual(importedBytes)).IsTrue();
            using (var bundle = await adminClient.GetAsync("/api/admin/localization/bundle?languageCode=%20FR%20"))
            {
                await Assert.That(bundle.StatusCode).IsEqualTo(HttpStatusCode.OK);
                await Assert.That((await JsonAsync(bundle)).GetProperty("ui.native_cohort.greeting").GetString()).IsEqualTo("Bonjour");
            }
            using (var translations = await anonymous.GetAsync("/api/translation/%20FR%20"))
            {
                await Assert.That(translations.StatusCode).IsEqualTo(HttpStatusCode.OK);
                await Assert.That((await JsonAsync(translations)).GetProperty("ui.native_cohort.greeting").GetString()).IsEqualTo("Bonjour");
            }
            using (var languages = await anonymous.GetAsync("/api/translation/languages"))
            {
                await Assert.That(languages.StatusCode).IsEqualTo(HttpStatusCode.OK);
                await Assert.That((await languages.Content.ReadFromJsonAsync<List<string>>())!).Contains("fr");
            }
            using (var export = await adminClient.PostAsync("/api/admin/localization/export-from-tms?languageCode=fr", null))
                await SuccessAsync(export);
            using (var persisted = JsonDocument.Parse(await File.ReadAllBytesAsync(bundlePath)))
                await Assert.That(persisted.RootElement.GetProperty("ui.native_cohort.greeting").GetString()).IsEqualTo("Bonjour");

            // Use the real scoped resolver and native command together: invalidation must clear
            // the already-preloaded cache in the same scope, after persistence succeeds.
            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                services.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
                var accessor = services.GetRequiredService<IHttpContextAccessor>();
                var previous = accessor.HttpContext;
                accessor.HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", adminId.ToString())], "Test"))
                };
                try
                {
                    var resolver = services.GetRequiredService<ITranslationResolver>();
                    await Assert.That(await resolver.ResolveAsync("ui.native_cohort.greeting", "fr")).IsEqualTo("Bonjour");
                    var cache = services.GetRequiredService<IMemoryCache>();
                    var cacheKey = $"Translation:{PlatformDefaults.DefaultTenantId}:fr:offline:ui.native_cohort.greeting";
                    await Assert.That(cache.TryGetValue(cacheKey, out _)).IsTrue();
                    var command = services.GetRequiredService<ICommandHandler<ImportLocalizationBundleCommand, BaseCommandResponse<Guid>>>();
                    var result = await command.ExecuteAsync(new() { Dto = Bundle("Salut") }, default);
                    await Assert.That(result.IsSuccess).IsTrue();
                    await Assert.That(cache.TryGetValue(cacheKey, out _)).IsFalse();
                    var probe = services.GetRequiredService<IQueryHandler<TestTmsConnectionQuery, BaseCommandResponse<Guid>>>();
                    await Assert.That((await probe.QueryAsync(new(), default)).IsSuccess).IsTrue();
                }
                finally
                {
                    accessor.HttpContext = previous;
                }
            }

            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var tenant = services.GetRequiredService<ITenantContextAccessor>();
                var status = services.GetRequiredService<IQueryHandler<GetLocalizationTmsApiKeyConfiguredQuery, bool>>();
                tenant.SetTenant(foreignTenantId);
                await Assert.That(await status.QueryAsync(new(), default)).IsTrue();
                tenant.SetTenant(PlatformDefaults.DefaultTenantId);
                await Assert.That(await status.QueryAsync(new(), default)).IsFalse();
                var db = services.GetRequiredService<ExploreDbContext>();
                db.SecretBindings.Add(Binding(SecretScope.Instance, null));
                await db.SaveChangesAsync();
                await Assert.That(await status.QueryAsync(new(), default)).IsTrue();
            }
            var inherited = await ConfigurationAsync(adminClient);
            await Assert.That(inherited.GetProperty("tmsApiKeyConfigured").GetBoolean()).IsTrue();
            await Assert.That(inherited.TryGetProperty("tmsApiKey", out _)).IsFalse();
            await Assert.That(liveProviderRequests).IsEqualTo(0);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static User NewUser(Guid id, string name) => new()
    {
        Id = id,
        Pii = new() { Email = $"{name}@example.test", FirstName = name, LastName = "User" }
    };

    private static SecretBinding Binding(SecretScope scope, Guid? scopeId) => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = SecretDefinitionRegistry.Keys.Localization.TmsApiKey,
        Scope = scope,
        ScopeId = scopeId,
        SourceType = SecretSourceType.EnvironmentVariable,
        EnvironmentVariableName = "LOCALIZATION_TMS_API_KEY",
        LastValidationResult = SecretValidationResult.NotValidated
    };

    private static ImportLocalizationBundleDto Bundle(string value) => new()
    {
        LanguageCode = " FR ",
        Translations = new Dictionary<string, string> { ["ui.native_cohort.greeting"] = value }
    };

    private static UpdateLocalizationGovernanceDto Governance() => new()
    {
        Tms = new() { Provider = "none" },
        Languages = new() { DefaultLanguage = "FR", EnabledLanguages = ["EN", "FR"], FallbackLanguage = "EN" },
        Runtime = new() { ForceOfflineMode = true, ClientPickerEnabled = false }
    };

    private static HttpClient Client(WebApplicationFactory<Program> factory, string claims)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, claims);
        return client;
    }

    private static async Task<JsonElement> ConfigurationAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/admin/localization/configuration");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await JsonAsync(response);
    }

    private static async Task SuccessAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await JsonAsync(response)).GetProperty("success").GetBoolean()).IsTrue();
    }

    private static async Task ProblemAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        await Assert.That((await JsonAsync(response)).GetProperty("status").GetInt32()).IsEqualTo(400);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class RejectLiveProvider(Action recordRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            recordRequest();
            throw new InvalidOperationException("Live TMS traffic is forbidden in the offline cohort test.");
        }
    }
}
