using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.ConfigurationImport;
using Explore.Application.Authorization;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeConfigurationManifestHttpTests
{
    private const string InstanceExportPath = "/api/control-plane/configuration-manifest/export";
    private static readonly Guid TenantId = PlatformDefaults.DefaultTenantId;
    private static readonly string ExportPath = $"/api/tenants/{TenantId:D}/configuration-package/export";
    private static readonly string SessionsPath = $"/api/tenants/{TenantId:D}/configuration-import/sessions";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public async Task TenantAdministrator_ExportsPreviewsAndApplies_WithoutInstanceExportAuthority()
    {
        await using var factory = new ManifestFactory();
        using HttpClient client = factory.CreateClient();
        await factory.SeedAndAuthenticateAsync(client);

        await AssertInstanceExportDeniedAsync(client);
        JsonNode package = await ExportAsync(client);
        await Assert.That(package["kind"]!.GetValue<string>())
            .IsEqualTo(TenantConfigurationPackageContractMetadata.Kind);
        await Assert.That(package["metadata"]!["source"]!["tenantName"]!.GetValue<string>())
            .IsEqualTo("default-test");
        await Assert.That(package["spec"]!.AsObject().ContainsKey("instance")).IsFalse();
        await Assert.That(package["spec"]!.AsObject().ContainsKey("tenants")).IsFalse();

        // Display name belongs to tenant.settings, so this proves an actual mutation
        // without depending on relational bulk-setting writers in the InMemory host.
        package["spec"]!["displayName"] = "Imported Tenant";
        ConfigurationImportSessionCreatedResult session = await UploadAsync(client, package);
        await Assert.That(session.TargetScope).IsEqualTo(ConfigurationImportScope.Tenant);
        await Assert.That(session.TargetTenantId).IsEqualTo(TenantId);
        await Assert.That(session.AvailableSectionKeys).Contains("tenant.settings");
        ConfigurationImportPreviewRequest selection = TenantSettingsSelection();

        using HttpResponseMessage previewResponse = await PostSessionAsync(
            client, session, "preview", selection);
        await Assert.That(previewResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        ConfigurationImportPreviewResult preview =
            (await previewResponse.Content.ReadFromJsonAsync<ConfigurationImportPreviewResult>(WebJson))!;
        await Assert.That(preview.IsApplyReady).IsTrue();
        await Assert.That(preview.TargetTenantId).IsEqualTo(TenantId);
        await Assert.That(preview.State).IsEqualTo(ConfigurationImportSessionState.PreviewReady);
        await Assert.That(preview.Items.Single(item => item.SectionKey == "tenant.settings").Category)
            .IsEqualTo(ConfigurationImportPreviewCategory.Changed);

        var applyRequest = new ConfigurationImportApplyRequest { Preview = selection };
        using HttpResponseMessage applyResponse = await PostSessionAsync(
            client, session, "apply", applyRequest);
        await Assert.That(applyResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        ConfigurationImportOperationResult operation =
            (await applyResponse.Content.ReadFromJsonAsync<ConfigurationImportOperationResult>(WebJson))!;
        await Assert.That(operation.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        await Assert.That(operation.SessionId).IsEqualTo(session.SessionId);
        await Assert.That(operation.TargetTenantId).IsEqualTo(TenantId);
        await Assert.That(operation.FidelityVerified).IsTrue();
        await Assert.That(operation.SnapshotAvailable).IsTrue();
        await Assert.That(operation.EffectStatus).IsEqualTo(ConfigurationImportEffectStatus.Pending);

        JsonNode exportedAfterApply = await ExportAsync(client);
        await Assert.That(exportedAfterApply["spec"]!["displayName"]!.GetValue<string>())
            .IsEqualTo("Imported Tenant");
        using HttpResponseMessage receiptResponse = await client.GetAsync(
            $"{SessionsPath}/operations/{operation.OperationId:D}");
        await Assert.That(receiptResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        ConfigurationImportOperationResult receipt =
            (await receiptResponse.Content.ReadFromJsonAsync<ConfigurationImportOperationResult>(WebJson))!;
        await Assert.That(receipt.OperationId).IsEqualTo(operation.OperationId);
        await Assert.That(receipt.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        await Assert.That(receipt.TargetTenantId).IsEqualTo(TenantId);

        using HttpResponseMessage replay = await PostSessionAsync(client, session, "apply", applyRequest);
        await AssertProblemAsync(replay, HttpStatusCode.Conflict, ConfigurationImportFailureCodes.Replayed);
        await AssertInstanceExportDeniedAsync(client);
    }

    [Test]
    public async Task TenantImport_RejectsAnotherSessionsTokenAndAnotherTenantsRoutes()
    {
        await using var factory = new ManifestFactory();
        using HttpClient client = factory.CreateClient();
        Guid otherTenantId = await factory.SeedAndAuthenticateAsync(client);
        JsonNode package = await ExportAsync(client);
        ConfigurationImportSessionCreatedResult session = await UploadAsync(client, package);
        ConfigurationImportSessionCreatedResult otherSession = await UploadAsync(client, package);
        ConfigurationImportPreviewRequest selection = TenantSettingsSelection();

        using HttpResponseMessage wrongToken = await PostSessionAsync(
            client, session with { AccessToken = otherSession.AccessToken }, "preview", selection);
        await AssertProblemAsync(
            wrongToken, HttpStatusCode.NotFound, ConfigurationImportFailureCodes.ArtifactMissing);
        using HttpResponseMessage wrongApplyToken = await PostSessionAsync(
            client, session with { AccessToken = otherSession.AccessToken }, "apply",
            new ConfigurationImportApplyRequest { Preview = selection });
        await AssertProblemAsync(
            wrongApplyToken, HttpStatusCode.NotFound, ConfigurationImportFailureCodes.ArtifactMissing);

        using HttpResponseMessage otherExport = await client.GetAsync(
            $"/api/tenants/{otherTenantId:D}/configuration-package/export");
        await Assert.That(otherExport.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using HttpResponseMessage otherPreview = await PostSessionAsync(
            client, session, "preview", selection, otherTenantId);
        await Assert.That(otherPreview.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using HttpResponseMessage otherApply = await PostSessionAsync(
            client, session, "apply", new ConfigurationImportApplyRequest { Preview = selection }, otherTenantId);
        await Assert.That(otherApply.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        // Rejected requests must not consume or corrupt the correctly bound session.
        using HttpResponseMessage validPreview = await PostSessionAsync(client, session, "preview", selection);
        await Assert.That(validPreview.StatusCode).IsEqualTo(HttpStatusCode.OK);
        ConfigurationImportPreviewResult preview =
            (await validPreview.Content.ReadFromJsonAsync<ConfigurationImportPreviewResult>(WebJson))!;
        await Assert.That(preview.IsApplyReady).IsTrue();
        await Assert.That(preview.Items.Single(item => item.SectionKey == "tenant.settings").Category)
            .IsEqualTo(ConfigurationImportPreviewCategory.Unchanged);
    }

    private static ConfigurationImportPreviewRequest TenantSettingsSelection() => new()
    {
        SelectedSectionKeys = ["tenant.settings"],
        Mappings = new Dictionary<string, string>(),
        ApplyMode = ConfigurationImportApplyMode.ApplySelected,
        GrantedApprovalCodes = []
    };

    private static async Task<JsonNode> ExportAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync($"{ExportPath}?view=Overrides");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo(TenantConfigurationPackageContractMetadata.MediaType);
        return JsonNode.Parse(await response.Content.ReadAsByteArrayAsync())!;
    }

    private static async Task<ConfigurationImportSessionCreatedResult> UploadAsync(
        HttpClient client, JsonNode package)
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(package.ToJsonString()));
        content.Headers.ContentType = new MediaTypeHeaderValue(TenantConfigurationPackageContractMetadata.MediaType);
        using HttpResponseMessage response = await client.PostAsync(SessionsPath, content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ConfigurationImportSessionCreatedResult>(WebJson))!;
    }

    private static async Task<HttpResponseMessage> PostSessionAsync(
        HttpClient client,
        ConfigurationImportSessionCreatedResult session,
        string action,
        object body,
        Guid? targetTenantId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/tenants/{targetTenantId ?? TenantId:D}/configuration-import/sessions/{session.SessionId:D}/{action}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(ConfigurationImportApiBoundary.AccessTokenHeader, session.AccessToken);
        return await client.SendAsync(request);
    }

    private static async Task AssertInstanceExportDeniedAsync(HttpClient client)
    {
        foreach (string view in new[] { "Overrides", "Portable" })
        {
            using HttpResponseMessage response = await client.GetAsync($"{InstanceExportPath}?view={view}");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(403);
        }
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
    }

    private sealed class ManifestFactory : AuthenticatedWebApplicationFactory
    {
        public ManifestFactory()
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider
            {
                AllowAll = false,
                CheckPredicate = request => request.ResourceKind != ResourceKinds.InstanceSetting
                    && request.ResourceKind == ResourceKinds.TenantSetting
                    && request.Action is AuthorizationActions.TenantSettings.View or AuthorizationActions.TenantSettings.Update
                    && request.Facts is TenantSettingAuthorizationFacts facts
                    && facts.TenantId == TenantId
                    && facts.DocumentKey == request.ResourceId
            };
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConfigurationImportEffectOutboxRepository>();
                services.AddScoped<IConfigurationImportEffectOutboxRepository, DeferredImportOutbox>();
            });
        }

        public async Task<Guid> SeedAndAuthenticateAsync(HttpClient client)
        {
            using IServiceScope scope = Services.CreateScope();
            ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            TenantScenarioSeed.TenantScenarioResult tenant =
                await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            TenantScenarioSeed.TenantScenarioResult other =
                await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
            if (!await db.PaidEventPolicyVersions.AnyAsync(policy => policy.TenantId == null && policy.IsActive))
            {
                db.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
                await db.SaveChangesAsync();
            }
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateTenantAdminHeaderValue(tenant.UserId, tenant.TenantId));
            return other.TenantId;
        }
    }

    /// <summary>
    /// Retains real durable outbox writes and reads but leaves effects pending: EF
    /// InMemory cannot execute the relational lease-acquisition update. Transaction
    /// isolation and asynchronous effect delivery are outside this authorization slice.
    /// </summary>
    private sealed class DeferredImportOutbox(ExploreDbContext db)
        : OutboxRepository(db), IConfigurationImportEffectOutboxRepository
    {
        Task<DateTime?> IConfigurationImportEffectOutboxRepository.TryClaimForProcessing(
            Guid id, DateTime claimedAt, CancellationToken cancellationToken) =>
            Task.FromResult<DateTime?>(null);
    }
}
