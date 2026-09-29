namespace Event.Api.IntegrationTests.Features;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.ConfigurationImport;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using TUnit.Core;

[ClassDataSource<SetupLiveAuthorityRedFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class SetupLiveConfigurationImportTests(SetupLiveAuthorityRedFixture fixture)
{
    private const string CapabilityHeader = "X-Setup-Enrollment-Capability";

    [Test]
    public async Task UploadPreviewAndConcurrentApplyUseTheExistingImportStateMachine()
    {
        await fixture.ResetAsync();
        string originalName = await TenantName();
        var enrollment = await Enroll(["target.read", "configuration.import"]);
        await Assert.That(enrollment.Links.GetProperty("create-configuration-import-session")
            .GetProperty("method").GetString()).IsEqualTo("POST");
        var session = await Stage(enrollment);
        await Assert.That(await TenantName()).IsEqualTo(originalName);

        using var previewRequest = SessionRequest(enrollment, session, "preview");
        using var previewResponse = await fixture.Client.SendAsync(previewRequest);
        await Assert.That(previewResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        JsonElement items = preview.RootElement.GetProperty("items");
        JsonElement changed = items.EnumerateArray().Single(item =>
            item.GetProperty("sectionKey").GetString() == "tenant.settings");
        await Assert.That(changed.GetProperty("category").GetString()).IsEqualTo("Changed");
        await Assert.That(items.EnumerateArray().Any(item =>
            item.GetProperty("category").GetString() == "Omitted")).IsTrue();
        await Assert.That(preview.RootElement.GetProperty("_links")
            .GetProperty("apply-configuration-import").GetProperty("method").GetString())
            .IsEqualTo("POST");
        await Assert.That(await TenantName()).IsEqualTo(originalName);

        using var barrier = fixture.Coordinator.ArmBeforeAcquireBarrier();
        using var first = SessionRequest(enrollment, session, "apply");
        Task<HttpResponseMessage> pending = fixture.Client.SendAsync(first);
        await barrier.Started.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var second = SessionRequest(enrollment, session, "apply");
            using var applied = await fixture.Client.SendAsync(second);
            await Assert.That(applied.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var receipt = JsonDocument.Parse(await applied.Content.ReadAsStringAsync());
            await Assert.That(receipt.RootElement.GetProperty("fidelityVerified").GetBoolean())
                .IsTrue();
        }
        finally
        {
            barrier.Release();
        }
        using var conflict = await pending;
        await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(conflict.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(409);
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo(ConfigurationImportFailureCodes.Replayed);
        await Assert.That(await TenantName()).IsEqualTo("Setup Imported Community");
    }

    [Test]
    public async Task ApplyRechecksPermissionAndRejectsUnpreviewedOrChangedIntent()
    {
        await fixture.ResetAsync();
        string originalName = await TenantName();
        var enrollment = await Enroll(["target.read", "configuration.import"]);
        var session = await Stage(enrollment);
        using (var direct = SessionRequest(enrollment, session, "apply"))
        using (var response = await fixture.Client.SendAsync(direct))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);

        using (var preview = SessionRequest(enrollment, session, "preview"))
        using (var response = await fixture.Client.SendAsync(preview))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var changed = SessionRequest(enrollment, session, "apply"))
        {
            changed.Content = PreviewContent(["tenant.documents"]);
            using var response = await fixture.Client.SendAsync(changed);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }

        fixture.Authorization.CheckPredicate = request =>
            request.ResourceKind != ResourceKinds.TenantSetting;
        using var forbidden = SessionRequest(enrollment, session, "apply");
        using var denied = await fixture.Client.SendAsync(forbidden);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await TenantName()).IsEqualTo(originalName);
    }

    [Test]
    public async Task RevocationWhileApplyWaitsAtAdmissionPreventsMutation()
    {
        await fixture.ResetAsync();
        string originalName = await TenantName();
        var enrollment = await Enroll(["target.read", "configuration.import"]);
        var session = await Stage(enrollment);
        using (var preview = SessionRequest(enrollment, session, "preview"))
        using (var response = await fixture.Client.SendAsync(preview))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var barrier = fixture.Coordinator.ArmBeforeAcquireBarrier();
        using var apply = SessionRequest(enrollment, session, "apply");
        Task<HttpResponseMessage> pending = fixture.Client.SendAsync(apply);
        await barrier.Started.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var revoke = new HttpRequestMessage(HttpMethod.Delete,
                $"/api/tenants/{fixture.Primary.TenantId:D}/setup/enrollments/{enrollment.Id:D}");
            Authenticate(revoke);
            revoke.Headers.Add(CapabilityHeader, enrollment.Capability);
            revoke.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("D"));
            using var response = await fixture.Client.SendAsync(revoke);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally
        {
            barrier.Release();
        }
        using var denied = await pending;
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var ordinaryApply = new HttpRequestMessage(HttpMethod.Post,
            $"/api/tenants/{fixture.Primary.TenantId:D}/configuration-import/sessions/{session.Id:D}/apply");
        Authenticate(ordinaryApply);
        ordinaryApply.Headers.Add(ConfigurationImportApiBoundary.AccessTokenHeader, session.Token);
        ordinaryApply.Content = JsonContent.Create(new
        {
            preview = new
            {
                selectedSectionKeys = new[] { "tenant.settings" },
                mappings = new Dictionary<string, string>(),
                applyMode = ConfigurationImportApplyMode.ApplySelected,
                grantedApprovalCodes = Array.Empty<string>()
            },
            rollbackOfOperationId = (Guid?)null,
            managedScheduleId = (Guid?)null
        });
        using var ordinaryDenied = await fixture.Client.SendAsync(ordinaryApply);
        await Assert.That(ordinaryDenied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await TenantName()).IsEqualTo(originalName);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OversizedArtifactIsRejectedWithProblemDetails(bool unknownLength)
    {
        await fixture.ResetAsync();
        var enrollment = await Enroll(["target.read", "configuration.import"]);
        using var upload = Request(enrollment, "");
        byte[] bytes = new byte[ConfigurationImportApiBoundary.MaximumUploadBytes + 1];
        upload.Content = unknownLength
            ? new UnknownLengthArtifact(bytes)
            : new ByteArrayContent(bytes);
        upload.Content.Headers.ContentType =
            MediaTypeHeaderValue.Parse(TenantConfigurationPackageContractMetadata.MediaType);
        using var response = await fixture.Client.SendAsync(upload);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
    }

    [Test]
    [Arguments("expired")]
    [Arguments("rotated")]
    [Arguments("wrong-user")]
    [Arguments("wrong-session-token")]
    public async Task PreviewAuthorityCannotBeReusedAfterIdentityOrCapabilityChanges(string change)
    {
        await fixture.ResetAsync();
        string originalName = await TenantName();
        var enrollment = await Enroll(["target.read", "configuration.import"]);
        var session = await Stage(enrollment);
        using (var preview = SessionRequest(enrollment, session, "preview"))
        using (var response = await fixture.Client.SendAsync(preview))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        if (change == "expired")
            fixture.Clock.SetUtcNow(fixture.Clock.GetUtcNow().AddMinutes(16));
        if (change == "rotated")
        {
            using var rotate = new HttpRequestMessage(HttpMethod.Post,
                $"/api/tenants/{fixture.Primary.TenantId:D}/setup/enrollments/{enrollment.Id:D}/capability-rotations");
            Authenticate(rotate);
            rotate.Headers.Add(CapabilityHeader, enrollment.Capability);
            rotate.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("D"));
            using var response = await fixture.Client.SendAsync(rotate);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        using var apply = SessionRequest(enrollment, session, "apply");
        if (change == "wrong-user")
        {
            apply.Headers.Remove(TestAuthHandler.AuthHeaderName);
            apply.Headers.Add(TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateTenantAdminHeaderValue(
                    fixture.Secondary.UserId, fixture.Primary.TenantId));
        }
        if (change == "wrong-session-token")
        {
            apply.Headers.Remove(ConfigurationImportApiBoundary.AccessTokenHeader);
            apply.Headers.Add(ConfigurationImportApiBoundary.AccessTokenHeader, Token());
        }
        using var denied = await fixture.Client.SendAsync(apply);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await TenantName()).IsEqualTo(originalName);
    }

    [Test]
    public async Task AnonymousUploadAndMissingApprovalFailClosed()
    {
        await fixture.ResetAsync();
        using (var anonymous = new HttpRequestMessage(HttpMethod.Post, Route(Guid.CreateVersion7())))
        {
            anonymous.Content = Artifact();
            using var response = await fixture.Client.SendAsync(anonymous);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        var enrollment = await Enroll(["target.read", "configuration.import"]);
        var session = await Stage(enrollment);
        using var preview = SessionRequest(enrollment, session, "preview");
        preview.Content = PreviewContent(["tenant.settings", "tenant.legal_documents"]);
        using var previewResponse = await fixture.Client.SendAsync(preview);
        await Assert.That(previewResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("isApplyReady").GetBoolean()).IsFalse();
        await Assert.That(json.RootElement.GetProperty("_links")
            .TryGetProperty("apply-configuration-import", out _)).IsFalse();
        using var apply = SessionRequest(enrollment, session, "apply");
        apply.Content = PreviewContent(["tenant.settings", "tenant.legal_documents"]);
        using var rejected = await fixture.Client.SendAsync(apply);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task ImportScopeIsExplicitAndUploadRejectsInvalidAuthorityBeforeBodyRead()
    {
        await fixture.ResetAsync();
        var enrollment = await Enroll(["target.read"]);
        await Assert.That(enrollment.Links.TryGetProperty(
            "create-configuration-import-session", out _)).IsFalse();
        fixture.BodyProbe.Reset();
        using var request = Request(enrollment, "");
        request.Headers.Add(UnreadBodyProbe.HeaderName, "1");
        request.Content = Artifact();
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(fixture.BodyProbe.ReadCount).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidCapabilityAndCrossTenantCannotStageConfiguration()
    {
        await fixture.ResetAsync();
        var enrollment = await Enroll(["target.read", "configuration.import"]);
        foreach (bool crossTenant in new[] { false, true })
        {
            fixture.BodyProbe.Reset();
            using var request = Request(enrollment, "");
            request.Headers.Add(UnreadBodyProbe.HeaderName, "1");
            request.Content = Artifact();
            if (crossTenant)
                request.RequestUri = new Uri(Route(enrollment.Id).Replace(
                    fixture.Primary.TenantId.ToString("D"),
                    Guid.CreateVersion7().ToString("D"),
                    StringComparison.Ordinal), UriKind.Relative);
            else
            {
                request.Headers.Remove(CapabilityHeader);
                request.Headers.Add(CapabilityHeader, Token());
            }
            using var response = await fixture.Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(fixture.BodyProbe.ReadCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ConfigurationPermissionIsRequiredEvenWithValidEnrollment()
    {
        await fixture.ResetAsync();
        var enrollment = await Enroll(["target.read", "configuration.import"]);
        fixture.Authorization.CheckPredicate = request =>
            request.ResourceKind != ResourceKinds.TenantSetting;
        fixture.BodyProbe.Reset();
        using var upload = Request(enrollment, "");
        upload.Headers.Add(UnreadBodyProbe.HeaderName, "1");
        upload.Content = Artifact();
        using var response = await fixture.Client.SendAsync(upload);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(fixture.BodyProbe.ReadCount).IsEqualTo(0);
    }

    private async Task<Enrollment> Enroll(string[] scopes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/tenants/{fixture.Primary.TenantId:D}/setup/enrollments")
        {
            Content = JsonContent.Create(new
            {
                clientChallenge = Token(),
                requestedScopes = scopes
            })
        };
        Authenticate(request);
        request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("D"));
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new Enrollment(
            body.RootElement.GetProperty("enrollmentId").GetGuid(),
            response.Headers.GetValues(CapabilityHeader).Single(),
            body.RootElement.GetProperty("_links").Clone());
    }

    private HttpRequestMessage Request(Enrollment enrollment, string suffix)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route(enrollment.Id) + suffix);
        Authenticate(request);
        request.Headers.Add(CapabilityHeader, enrollment.Capability);
        return request;
    }

    private void Authenticate(HttpRequestMessage request) =>
        request.Headers.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(
                fixture.Primary.UserId, fixture.Primary.TenantId));

    private string Route(Guid enrollmentId) =>
        $"/api/tenants/{fixture.Primary.TenantId:D}/setup/enrollments/{enrollmentId:D}/configuration-import/sessions";

    private async Task<Session> Stage(Enrollment enrollment)
    {
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            if (!await db.PaidEventPolicyVersions.AnyAsync(policy =>
                    policy.TenantId == null && policy.IsActive))
            {
                db.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
                await db.SaveChangesAsync();
            }
        }
        using var upload = Request(enrollment, "");
        upload.Content = Artifact();
        using var response = await fixture.Client.SendAsync(upload);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("_links")
            .TryGetProperty("apply-configuration-import", out _)).IsFalse();
        return new Session(
            json.RootElement.GetProperty("sessionId").GetGuid(),
            json.RootElement.GetProperty("accessToken").GetString()!);
    }

    private HttpRequestMessage SessionRequest(Enrollment enrollment, Session session, string action)
    {
        var request = Request(enrollment, $"/{session.Id:D}/{action}");
        request.Headers.Add(ConfigurationImportApiBoundary.AccessTokenHeader, session.Token);
        request.Content = PreviewContent(["tenant.settings"]);
        return request;
    }

    private static HttpContent PreviewContent(string[] selected) =>
        JsonContent.Create(new
        {
            selectedSectionKeys = selected,
            mappings = new Dictionary<string, string>(),
            applyMode = ConfigurationImportApplyMode.ApplySelected,
            grantedApprovalCodes = Array.Empty<string>()
        });

    private async Task<string> TenantName()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenant = await tenants.GetByIdAsNoTrackingAsync(fixture.Primary.TenantId);
        return tenant!.FullName;
    }

    private static HttpContent Artifact()
    {
        var package = new TenantConfigurationPackageV1Alpha2
        {
            Schema = TenantConfigurationPackageContractMetadata.SchemaId,
            ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
            Kind = TenantConfigurationPackageContractMetadata.Kind,
            Metadata = new TenantConfigurationPackageMetadataV1Alpha2
            {
                Name = "source-community",
                Source = new TenantConfigurationPackageSourceV1Alpha2
                {
                    TenantName = "source-community"
                }
            },
            Spec = new TenantConfigurationPackageSpecV1Alpha2
            {
                DisplayName = "Setup Imported Community",
                Settings = new Dictionary<string, JsonElement>(),
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>()
            }
        };
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(
            package, ConfigurationPortabilityJsonContext.Default.TenantConfigurationPackageV1Alpha2));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            TenantConfigurationPackageContractMetadata.MediaType);
        return content;
    }

    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record Enrollment(Guid Id, string Capability, JsonElement Links)
    {
        public override string ToString() => nameof(Enrollment);
    }

    private sealed record Session(Guid Id, string Token)
    {
        public override string ToString() => nameof(Session);
    }

    private sealed class UnknownLengthArtifact(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
