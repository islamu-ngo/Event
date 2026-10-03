namespace Event.SetupAssistant.Tests;

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using ISLAMU.Event.SetupAssistant.SetupLive;
using Wire = ISLAMU.Wire.Contracts.SetupLive;

public sealed class SetupLiveAdapterApplyTests
{
    private static readonly Uri Target = new("https://setup.example/");
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid EnrollmentId = Guid.CreateVersion7();
    private static readonly Guid SessionId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static string BasePath =>
        $"/api/tenants/{TenantId:D}/setup/enrollments/{EnrollmentId:D}/configuration-import/sessions";

    [Test]
    public async Task GeneratedTransportUploadsPreviewsAndAppliesTheExactPreviewedIntent()
    {
        using var scenario = new Scenario();
        scenario.Handler.Enqueue(() => Stage(scenario.ImportToken));
        scenario.Handler.Enqueue(() => Preview(BasePath + $"/{SessionId:D}/apply"));
        scenario.Handler.Enqueue(() => Json(new
        {
            operationId = Guid.CreateVersion7(),
            sessionId = SessionId,
            targetScope = "Tenant",
            targetTenantId = TenantId,
            kind = "Apply",
            status = "Applied",
            selectedSectionKeys = new[] { "tenant.settings" },
            snapshotAvailable = true,
            effectStatus = "Completed",
            effectRetryCount = 0,
            fidelityVerified = true,
            fidelityDigest = new string('a', 64),
            omittedSectionKeys = new[] { "excluded.secrets" },
            completedAt = Now
        }));
        await scenario.Enroll();
        using var artifact = new MemoryStream("{\"artifact\":\"bounded\"}"u8.ToArray());
        var staged = await scenario.Adapter.UploadConfigurationAsync(artifact);
        await Assert.That(staged.SessionId).IsEqualTo(SessionId);
        await Assert.That(staged.ToString()).DoesNotContain(scenario.ImportToken);
        await Assert.That(scenario.Adapter.CanApplyConfiguration).IsFalse();
        var options = Options();
        var preview = await scenario.Adapter.PreviewConfigurationAsync(options);
        await Assert.That(preview.Items).IsEquivalentTo(new[]
        {
            new SetupLiveConfigurationDifference("tenant.settings",
                ConfigurationImportPreviewCategory.Changed,
                "configuration_import_section_changed", null, null),
            new SetupLiveConfigurationDifference("excluded.secrets",
                ConfigurationImportPreviewCategory.Omitted,
                "configuration_import_nonportable_section_omitted", null, null)
        });
        await Assert.That(scenario.Adapter.CanApplyConfiguration).IsTrue();
        options.SelectedSectionKeys.Clear();
        options.Mappings["tampered"] = "target";
        var applied = await scenario.Adapter.ApplyConfigurationAsync();
        await Assert.That(applied.FidelityVerified).IsTrue();
        await Assert.That(scenario.Adapter.CanApplyConfiguration).IsFalse();
        await Assert.That(scenario.Handler.Requests[2].Body)
            .IsEqualTo(scenario.Handler.Requests[3].Body);
        await Assert.That(scenario.Handler.Requests[1].Capability)
            .IsEqualTo(scenario.Capability);
        await Assert.That(scenario.Handler.Requests[1].ImportToken).IsNull();
        await Assert.That(scenario.Handler.Requests[2].ImportToken)
            .IsEqualTo(scenario.ImportToken);
        await Assert.That(scenario.Handler.Requests[3].ImportToken)
            .IsEqualTo(scenario.ImportToken);
        await Assert.That(scenario.Handler.Requests[1].Uri)
            .IsEqualTo(new Uri(Target, BasePath));
        await Assert.That(scenario.Handler.Requests[1].ContentType)
            .IsEqualTo(ISLAMU.Wire.Contracts.ConfigurationPortability
                .TenantConfigurationPackageContractMetadata.MediaType);
    }

    [Test]
    [Arguments(null, "POST")]
    [Arguments("https://attacker.example/apply", "POST")]
    [Arguments("http://setup.example/apply", "POST")]
    [Arguments("/api/other-tenant/apply", "POST")]
    [Arguments("?redirect=elsewhere", "POST")]
    [Arguments("valid", "GET")]
    public async Task ApplyRequiresExactServerIssuedMethodAndScopedHttpsHref(string? href, string method)
    {
        using var scenario = new Scenario();
        scenario.Handler.Enqueue(() => Stage(scenario.ImportToken));
        scenario.Handler.Enqueue(() => Preview(href == "valid"
            ? BasePath + $"/{SessionId:D}/apply" : href, method));
        await scenario.Enroll();
        using var artifact = new MemoryStream("{}"u8.ToArray());
        await scenario.Adapter.UploadConfigurationAsync(artifact);
        await scenario.Adapter.PreviewConfigurationAsync(Options());
        await Assert.That(scenario.Adapter.CanApplyConfiguration).IsFalse();
        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(
            () => scenario.Adapter.ApplyConfigurationAsync());
        await Assert.That(scenario.Handler.Requests.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ConflictIsValueFreeAndConsumesLocalApplyAffordance()
    {
        using var scenario = new Scenario();
        scenario.Handler.Enqueue(() => Stage(scenario.ImportToken));
        scenario.Handler.Enqueue(() => Preview(BasePath + $"/{SessionId:D}/apply"));
        scenario.Handler.Enqueue(() => Json(new
        {
            status = 409,
            type = "/problems/conflict",
            title = "Conflict",
            detail = scenario.ImportToken
        }, HttpStatusCode.Conflict));
        await scenario.Enroll();
        using var artifact = new MemoryStream("{}"u8.ToArray());
        await scenario.Adapter.UploadConfigurationAsync(artifact);
        await scenario.Adapter.PreviewConfigurationAsync(Options());
        var failure = await Assert.ThrowsAsync<SetupLiveConfigurationConflictException>(
            () => scenario.Adapter.ApplyConfigurationAsync())
            ?? throw new InvalidOperationException("Expected configuration import conflict.");
        await Assert.That(failure.ToString()).DoesNotContain(scenario.ImportToken);
        await Assert.That(scenario.Adapter.CanApplyConfiguration).IsFalse();
        await Assert.That(scenario.Handler.Requests.Count).IsEqualTo(4);
    }

    [Test]
    public async Task UploadRejectsEmptyAndOversizedStreamsBeforeTransport()
    {
        using var scenario = new Scenario();
        await scenario.Enroll();
        using var empty = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(
            () => scenario.Adapter.UploadConfigurationAsync(empty));
        using var oversized = new MemoryStream(new byte[4 * 1024 * 1024 + 1]);
        await Assert.ThrowsAsync<ArgumentException>(
            () => scenario.Adapter.UploadConfigurationAsync(oversized));
        await Assert.That(scenario.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExpiredImportDiscardsApplyAuthorityBeforeDispatch()
    {
        using var scenario = new Scenario();
        scenario.Handler.Enqueue(() => Stage(scenario.ImportToken));
        scenario.Handler.Enqueue(() => Preview(BasePath + $"/{SessionId:D}/apply"));
        await scenario.Enroll();
        using var artifact = new MemoryStream("{}"u8.ToArray());
        await scenario.Adapter.UploadConfigurationAsync(artifact);
        await scenario.Adapter.PreviewConfigurationAsync(Options());
        scenario.Time.Current = Now.AddMinutes(6);
        await Assert.That(scenario.Adapter.CanApplyConfiguration).IsFalse();
        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(
            () => scenario.Adapter.ApplyConfigurationAsync());
        await Assert.That(scenario.Handler.Requests.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("https://attacker.example/upload", "POST")]
    [Arguments("valid", "GET")]
    public async Task UploadCannotUseAnUntrustedEnrollmentAffordance(string href, string method)
    {
        using var scenario = new Scenario(href == "valid" ? BasePath : href, method);
        await scenario.Enroll();
        using var artifact = new MemoryStream("{}"u8.ToArray());
        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(
            () => scenario.Adapter.UploadConfigurationAsync(artifact));
        await Assert.That(artifact.Position).IsEqualTo(0);
        await Assert.That(scenario.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AffordanceMustMatchTheGeneratedClientsBasePath()
    {
        using var scenario = new Scenario(target: new Uri("https://setup.example/deployment/"));
        await scenario.Enroll();
        using var artifact = new MemoryStream("{}"u8.ToArray());
        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(
            () => scenario.Adapter.UploadConfigurationAsync(artifact));
        await Assert.That(scenario.Handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ClearingAuthorityDiscardsImportTokenAndApplyLink()
    {
        using var scenario = new Scenario();
        scenario.Handler.Enqueue(() => Stage(scenario.ImportToken));
        scenario.Handler.Enqueue(() => Preview(BasePath + $"/{SessionId:D}/apply"));
        await scenario.Enroll();
        using var artifact = new MemoryStream("{}"u8.ToArray());
        await scenario.Adapter.UploadConfigurationAsync(artifact);
        await scenario.Adapter.PreviewConfigurationAsync(Options());
        scenario.Adapter.ClearAuthority();
        await Assert.That(scenario.Adapter.CanApplyConfiguration).IsFalse();
        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
            () => scenario.Adapter.ApplyConfigurationAsync());
        await Assert.That(scenario.Handler.Requests.Count).IsEqualTo(3);
    }

    private static ConfigurationImportPreviewRequest Options() => new()
    {
        SelectedSectionKeys = ["tenant.settings"],
        Mappings = new Dictionary<string, string>(),
        ApplyMode = ConfigurationImportApplyMode.ApplySelected,
        GrantedApprovalCodes = []
    };

    private static HttpResponseMessage Stage(string token) => Json(new
    {
        sessionId = SessionId,
        accessToken = token,
        targetScope = "Tenant",
        targetTenantId = TenantId,
        state = "Uploaded",
        expiresAt = Now.AddMinutes(5),
        artifactByteLength = 2,
        availableSectionKeys = new[] { "tenant.settings" },
        _links = new Dictionary<string, object>
        {
            ["preview-configuration-import"] = new { href = BasePath + $"/{SessionId:D}/preview", method = "POST" }
        }
    }, HttpStatusCode.Created);

    private static HttpResponseMessage Preview(string? applyHref, string method = "POST") => Json(new
    {
        sessionId = SessionId,
        targetScope = "Tenant",
        targetTenantId = TenantId,
        state = "PreviewReady",
        expiresAt = Now.AddMinutes(5),
        isApplyReady = true,
        items = new[]
        {
            new
            {
                sectionKey = "tenant.settings",
                category = "Changed",
                reasonCode = "configuration_import_section_changed",
                sourceMappingIdentity = (string?)null,
                targetMappingIdentity = (string?)null
            },
            new
            {
                sectionKey = "excluded.secrets",
                category = "Omitted",
                reasonCode = "configuration_import_nonportable_section_omitted",
                sourceMappingIdentity = (string?)null,
                targetMappingIdentity = (string?)null
            }
        },
        _links = applyHref is null ? new Dictionary<string, object>() : new Dictionary<string, object>
        {
            ["apply-configuration-import"] = new { href = applyHref, method }
        }
    });

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8, (int)status < 400 ? "application/hal+json" : "application/problem+json")
        };

    private sealed class Scenario : IDisposable
    {
        private readonly HttpClient _client;
        public string Capability { get; } = Token();
        public string ImportToken { get; } = Token();
        public Handler Handler { get; } = new();
        public Clock Time { get; } = new();
        public SetupLiveAdapter Adapter { get; }

        public Scenario(string? uploadHref = null, string uploadMethod = "POST", Uri? target = null)
        {
            Handler.Enqueue(() =>
            {
                var enrollment = Json(new
                {
                    enrollmentId = EnrollmentId,
                    state = "active",
                    generation = 1,
                    expiresAt = Now.AddMinutes(10),
                    issuance = "issued",
                    scopes = new[] { "target.read", "secret_binding.readiness", "secret_binding.write", "configuration.import" },
                    _links = new Dictionary<string, object>
                    {
                        ["create-configuration-import-session"] = new { href = uploadHref ?? BasePath, method = uploadMethod }
                    }
                }, HttpStatusCode.Created);
                enrollment.Headers.Add(Wire.SetupLiveContractMetadata.CapabilityHeader, Capability);
                return enrollment;
            });
            _client = new HttpClient(Handler) { BaseAddress = target ?? Target };
            Adapter = new SetupLiveAdapter(target ?? Target, TenantId, _client,
                _ => ValueTask.FromResult<SetupLiveAccessToken?>(
                    SetupLiveAccessToken.Create(Token(), Now.AddMinutes(20))),
                Time);
        }

        public Task<SetupLiveEnrollmentSnapshot> Enroll() => Adapter.EnrollAsync(
            Wire.SetupClientChallenge.FromBytes(RandomNumberGenerator.GetBytes(32)),
            Enum.GetValues<Wire.SetupEnrollmentScope>());

        public void Dispose()
        {
            Adapter.Dispose();
            _client.Dispose();
        }

        private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed record Captured(Uri Uri, string Body, string? Capability, string? ImportToken, string? ContentType);

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public List<Captured> Requests { get; } = [];
        public void Enqueue(Func<HttpResponseMessage> response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Captured(request.RequestUri!,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues(Wire.SetupLiveContractMetadata.CapabilityHeader, out var capability)
                    ? capability.Single() : null,
                request.Headers.TryGetValues("X-Configuration-Import-Token", out var token) ? token.Single() : null,
                request.Content?.Headers.ContentType?.MediaType));
            return _responses.Dequeue()();
        }
    }
}
