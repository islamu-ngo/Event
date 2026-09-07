using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Hateoas.Policies;
using Explore.Application.Constants;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class SupportAccessApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly MediaTypeHeaderValue HalJsonMediaType = new("application/hal+json");
    private static readonly MediaTypeWithQualityHeaderValue HalJsonAcceptHeader = new("application/hal+json");

    [Test]
    public async Task StopLink_UsesCanonicalPriorityAndFailsClosedForPurposeBoundPrincipal()
    {
        Guid subject = Guid.CreateVersion7();
        Guid internalUser = Guid.CreateVersion7();
        var dto = new SupportAccessSessionDto
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = subject,
            TargetTenantId = Guid.CreateVersion7(),
            IsActive = true
        };
        var policy = new SupportAccessSessionDetailLinkPolicy();
        var conflicting = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject.ToString("D")),
            new Claim("internal_user_id", internalUser.ToString("D"))
        ], "interactive"));
        var purposeBound = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject.ToString("D"))
        ], ApiAuthenticationSchemeNames.ApiKey));

        await Assert.That(policy.GetLinks(dto, conflicting).Any(link => link.Rel == "stop")).IsTrue();
        await Assert.That(policy.GetLinks(dto, purposeBound).Any(link => link.Rel == "stop")).IsFalse();
    }

    [Test]
    public async Task SessionList_WithConflictingGuidClaims_UsesCanonicalSubjectForStopLink()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(enableSupportAccess: true);
        Guid actorUserId = await host.SeedActorUserAsync();
        Guid sessionId = await StartReadOnlySessionAsync(host, actorUserId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/support-access/tenants/{PlatformDefaults.DefaultTenantId:D}/sessions");
        request.Headers.Accept.Add(HalJsonAcceptHeader);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, EncodeClaims(
            new Claim("sub", actorUserId.ToString("D")),
            new Claim("internal_user_id", Guid.CreateVersion7().ToString("D"))));

        using var response = await host.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = await ReadJsonDocumentAsync(response);
        JsonElement session = json.RootElement.GetProperty("_embedded").GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == sessionId);
        await Assert.That(session.GetProperty("_links").TryGetProperty("stop", out _)).IsTrue();
    }

    [Test]
    public async Task Start_WhenSupportAccessDisabled_ReturnsProblemCode()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(enableSupportAccess: false);
        var actorUserId = await host.SeedActorUserAsync();

        using var request = CreateStartRequest(actorUserId, CreateStartDto());
        using var response = await host.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, SupportAccessFailureCodes.Disabled);
    }

    [Test]
    public async Task Start_WhenTicketReferenceRequired_ReturnsProblemCode()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(
            enableSupportAccess: true,
            requireTicketReference: true);
        var actorUserId = await host.SeedActorUserAsync();
        var dto = CreateStartDto(ticketReference: null);

        using var request = CreateStartRequest(actorUserId, dto);
        using var response = await host.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, SupportAccessFailureCodes.TicketReferenceRequired);
    }

    [Test]
    public async Task Start_WhenReadDurationExceedsPolicy_ReturnsProblemCode()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(
            enableSupportAccess: true,
            maxReadOnlyMinutes: 30);
        var actorUserId = await host.SeedActorUserAsync();
        var dto = CreateStartDto(durationMinutes: 31);

        using var request = CreateStartRequest(actorUserId, dto);
        using var response = await host.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, SupportAccessFailureCodes.DurationExceedsPolicy);
    }

    [Test]
    public async Task Start_WhenWriteModeDisabled_ReturnsProblemCode()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(
            enableSupportAccess: true,
            allowWriteMode: false);
        var actorUserId = await host.SeedActorUserAsync();
        var dto = CreateStartDto(mode: SupportAccessModeEnum.Write, durationMinutes: 5);

        using var request = CreateStartRequest(actorUserId, dto);
        using var response = await host.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, SupportAccessFailureCodes.WriteModeDisabled);
    }

    [Test]
    public async Task Start_WhenActorAlreadyHasActiveSession_ReturnsProblemCode()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(enableSupportAccess: true);
        var actorUserId = await host.SeedActorUserAsync();
        await StartReadOnlySessionAsync(host, actorUserId);

        using var request = CreateStartRequest(actorUserId, CreateStartDto(ticketReference: "SUP-SECOND"));
        using var response = await host.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertProblemCodeAsync(response, SupportAccessFailureCodes.ActiveSessionExists);
    }

    [Test]
    public async Task StartReadOnlySession_ExposesCurrentSessionHalLinksAndAuditHistory()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(enableSupportAccess: true);
        var actorUserId = await host.SeedActorUserAsync();

        using var startRequest = CreateStartRequest(actorUserId, CreateStartDto());
        using var startResponse = await host.Client.SendAsync(startRequest);

        await Assert.That(startResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var startJson = await ReadJsonDocumentAsync(startResponse);
        var sessionId = startJson.RootElement.GetProperty("id").GetGuid();
        await Assert.That(sessionId).IsNotEqualTo(Guid.Empty);
        await Assert.That(startJson.RootElement.GetProperty("actorUserId").GetGuid()).IsEqualTo(actorUserId);
        await Assert.That(startJson.RootElement.GetProperty("targetTenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(startJson.RootElement.GetProperty("modeName").GetString()).IsEqualTo(nameof(SupportAccessModeEnum.ReadOnly));
        await Assert.That(startJson.RootElement.GetProperty("allowsWrites").GetBoolean()).IsFalse();
        await Assert.That(startJson.RootElement.GetProperty("isActive").GetBoolean()).IsTrue();

        var detailLinks = startJson.RootElement.GetProperty("_links");
        await Assert.That(detailLinks.TryGetProperty("audit-events", out _)).IsTrue();
        await Assert.That(detailLinks.TryGetProperty("stop", out _)).IsTrue();
        await Assert.That(detailLinks.TryGetProperty("force-stop", out _)).IsTrue();

        using var currentRequest = CreateAuthenticatedRequest(HttpMethod.Get, "/api/support-access/current", actorUserId);
        using var currentResponse = await host.Client.SendAsync(currentRequest);
        await Assert.That(currentResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var currentJson = await ReadJsonDocumentAsync(currentResponse);
        await Assert.That(currentJson.RootElement.GetProperty("isActive").GetBoolean()).IsTrue();
        await Assert.That(currentJson.RootElement.GetProperty("session").GetProperty("id").GetGuid()).IsEqualTo(sessionId);

        using var listRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/support-access/tenants/{PlatformDefaults.DefaultTenantId:D}/sessions", actorUserId);
        using var listResponse = await host.Client.SendAsync(listRequest);
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var listJson = await ReadJsonDocumentAsync(listResponse);
        await Assert.That(listJson.RootElement.GetProperty("_links").TryGetProperty("start", out _)).IsTrue();
        var sessionItems = listJson.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
        await Assert.That(sessionItems.Any(item => item.GetProperty("id").GetGuid() == sessionId)).IsTrue();

        using var auditRequest = CreateAuthenticatedRequest(HttpMethod.Get, $"/api/support-access/tenants/{PlatformDefaults.DefaultTenantId:D}/sessions/{sessionId:D}/audit-events", actorUserId);
        using var auditResponse = await host.Client.SendAsync(auditRequest);
        await Assert.That(auditResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var auditJson = await ReadJsonDocumentAsync(auditResponse);
        var auditItems = auditJson.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
        await Assert.That(auditItems.Any(item =>
            item.GetProperty("supportAccessSessionId").GetGuid() == sessionId &&
            item.GetProperty("eventTypeName").GetString() == nameof(SupportAccessAuditEventTypeEnum.Started))).IsTrue();
    }

    [Test]
    public async Task Stop_WhenOwnedByActor_ReturnsStoppedSessionAndAuditEvent()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(enableSupportAccess: true);
        var actorUserId = await host.SeedActorUserAsync();
        var sessionId = await StartReadOnlySessionAsync(host, actorUserId);

        using var stopRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/support-access/sessions/{sessionId:D}/stop",
            actorUserId,
            new StopSupportAccessSessionRequestDto { EndReasonText = "Issue resolved." });
        using var stopResponse = await host.Client.SendAsync(stopRequest);

        await Assert.That(stopResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var stopJson = await ReadJsonDocumentAsync(stopResponse);
        await Assert.That(stopJson.RootElement.GetProperty("id").GetGuid()).IsEqualTo(sessionId);
        await Assert.That(stopJson.RootElement.GetProperty("statusName").GetString()).IsEqualTo(nameof(SupportAccessSessionStatusEnum.Stopped));
        await Assert.That(stopJson.RootElement.GetProperty("endReasonName").GetString()).IsEqualTo(nameof(SupportAccessEndReasonEnum.UserStopped));
        await Assert.That(stopJson.RootElement.GetProperty("isActive").GetBoolean()).IsFalse();

        await AssertAuditContainsAsync(host, actorUserId, sessionId, SupportAccessAuditEventTypeEnum.Stopped);
    }

    [Test]
    public async Task ForceStop_WhenActive_ReturnsRevokedSessionAndAuditEvent()
    {
        await using var host = await SupportAccessApiHost.CreateAsync(enableSupportAccess: true);
        var actorUserId = await host.SeedActorUserAsync();
        var operatorUserId = await host.SeedActorUserAsync();
        var sessionId = await StartReadOnlySessionAsync(host, actorUserId);

        using var forceStopRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/support-access/sessions/{sessionId:D}/force-stop",
            operatorUserId,
            new ForceStopSupportAccessSessionRequestDto { EndReasonText = "Emergency revocation." });
        using var forceStopResponse = await host.Client.SendAsync(forceStopRequest);

        await Assert.That(forceStopResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var forceStopJson = await ReadJsonDocumentAsync(forceStopResponse);
        await Assert.That(forceStopJson.RootElement.GetProperty("id").GetGuid()).IsEqualTo(sessionId);
        await Assert.That(forceStopJson.RootElement.GetProperty("statusName").GetString()).IsEqualTo(nameof(SupportAccessSessionStatusEnum.Revoked));
        await Assert.That(forceStopJson.RootElement.GetProperty("endReasonName").GetString()).IsEqualTo(nameof(SupportAccessEndReasonEnum.ForceStopped));
        await Assert.That(forceStopJson.RootElement.GetProperty("isActive").GetBoolean()).IsFalse();

        await AssertAuditContainsAsync(host, operatorUserId, sessionId, SupportAccessAuditEventTypeEnum.Revoked, operatorUserId);
    }

    private static StartSupportAccessSessionRequestDto CreateStartDto(
        SupportAccessModeEnum mode = SupportAccessModeEnum.ReadOnly,
        int durationMinutes = 15,
        string? ticketReference = "SUP-12345")
    {
        return new StartSupportAccessSessionRequestDto
        {
            TargetTenantId = PlatformDefaults.DefaultTenantId,
            Mode = mode,
            DurationMinutes = durationMinutes,
            ReasonCode = "customer-support",
            ReasonText = "Investigating a reported tenant issue.",
            TicketReference = ticketReference
        };
    }

    private static HttpRequestMessage CreateStartRequest(Guid actorUserId, StartSupportAccessSessionRequestDto dto)
    {
        return CreateAuthenticatedJsonRequest(HttpMethod.Post, "/api/support-access/sessions", actorUserId, dto);
    }

    private static HttpRequestMessage CreateAuthenticatedJsonRequest<T>(
        HttpMethod method,
        string requestUri,
        Guid actorUserId,
        T body)
    {
        var request = CreateAuthenticatedRequest(method, requestUri, actorUserId);
        request.Content = JsonContent.Create(body, mediaType: HalJsonMediaType, options: JsonOptions);
        return request;
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string requestUri, Guid actorUserId)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(HalJsonAcceptHeader);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, CreateInternalUserAuthHeader(actorUserId));
        return request;
    }

    private static string CreateInternalUserAuthHeader(Guid actorUserId)
    {
        return TestAuthHandler.CreateAuthHeaderValue(
            actorUserId,
            "Support Operator",
            ("internal_user_id", actorUserId.ToString("D")),
            ("explore:admin:instance", "true"));
    }

    private static string EncodeClaims(params Claim[] claims)
    {
        var values = claims.Select(claim => new TestAuthHandler.TestClaimDto(claim.Type, claim.Value));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)));
    }

    private static async Task<Guid> StartReadOnlySessionAsync(SupportAccessApiHost host, Guid actorUserId)
    {
        using var request = CreateStartRequest(actorUserId, CreateStartDto());
        using var response = await host.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var json = await ReadJsonDocumentAsync(response);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var json = await ReadJsonDocumentAsync(response);

        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)HttpStatusCode.BadRequest);
        await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo(expectedCode);
    }

    private static async Task AssertAuditContainsAsync(
        SupportAccessApiHost host,
        Guid actorUserId,
        Guid sessionId,
        SupportAccessAuditEventTypeEnum eventType,
        Guid? expectedAuditActorUserId = null)
    {
        using var auditRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/support-access/tenants/{PlatformDefaults.DefaultTenantId:D}/sessions/{sessionId:D}/audit-events",
            actorUserId);
        using var auditResponse = await host.Client.SendAsync(auditRequest);

        await Assert.That(auditResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var auditJson = await ReadJsonDocumentAsync(auditResponse);
        var auditItems = auditJson.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
        await Assert.That(auditItems.Any(item =>
            item.GetProperty("supportAccessSessionId").GetGuid() == sessionId &&
            item.GetProperty("eventTypeName").GetString() == eventType.ToString() &&
            (!expectedAuditActorUserId.HasValue || item.GetProperty("actorUserId").GetGuid() == expectedAuditActorUserId.Value))).IsTrue();
    }

    private static async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private sealed class SupportAccessApiHost : IAsyncDisposable
    {
        private SupportAccessApiHost(AuthenticatedWebApplicationFactory factory, HttpClient client)
        {
            Factory = factory;
            Client = client;
        }

        public AuthenticatedWebApplicationFactory Factory { get; }
        public HttpClient Client { get; }

        public static async Task<SupportAccessApiHost> CreateAsync(
            bool enableSupportAccess,
            bool requireTicketReference = true,
            bool allowWriteMode = false,
            int maxReadOnlyMinutes = 30,
            int maxWriteMinutes = 10)
        {
            var factory = new AuthenticatedWebApplicationFactory
            {
                AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
            };
            var client = factory.CreateClient();
            var host = new SupportAccessApiHost(factory, client);
            await host.ConfigureSupportAccessSettingsAsync(
                enableSupportAccess,
                requireTicketReference,
                allowWriteMode,
                maxReadOnlyMinutes,
                maxWriteMinutes);

            return host;
        }

        public async Task<Guid> SeedActorUserAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var seed = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            return seed.UserId;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }

        private async Task ConfigureSupportAccessSettingsAsync(
            bool enabled,
            bool requireTicketReference,
            bool allowWriteMode,
            int maxReadOnlyMinutes,
            int maxWriteMinutes)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

            await UpsertSystemSettingAsync(db, GovernanceSettingKeys.SupportAccess.Enabled, enabled ? "true" : "false", SettingValueType.Boolean);
            await UpsertSystemSettingAsync(db, GovernanceSettingKeys.SupportAccess.RequireTicketReference, requireTicketReference ? "true" : "false", SettingValueType.Boolean);
            await UpsertSystemSettingAsync(db, GovernanceSettingKeys.SupportAccess.AllowWriteMode, allowWriteMode ? "true" : "false", SettingValueType.Boolean);
            await UpsertSystemSettingAsync(db, GovernanceSettingKeys.SupportAccess.MaxReadOnlyMinutes, maxReadOnlyMinutes.ToString(), SettingValueType.Integer);
            await UpsertSystemSettingAsync(db, GovernanceSettingKeys.SupportAccess.MaxWriteMinutes, maxWriteMinutes.ToString(), SettingValueType.Integer);
            await UpsertSystemSettingAsync(db, GovernanceSettingKeys.SupportAccess.OneActiveSessionPerActor, "true", SettingValueType.Boolean);

            await db.SaveChangesAsync();

            if (scope.ServiceProvider.GetService<IMemoryCache>() is MemoryCache memoryCache)
            {
                memoryCache.Compact(1.0);
            }
        }

        private static async Task UpsertSystemSettingAsync(
            ExploreDbContext db,
            string key,
            string value,
            SettingValueType valueType)
        {
            var setting = await db.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null)
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = value,
                    ValueType = valueType,
                    IsLocked = true,
                    Category = "SupportAccess",
                    Description = "Support-access integration test setting.",
                    CreatedAt = DateTime.UtcNow
                });
                return;
            }

            setting.Value = value;
            setting.ValueType = valueType;
            setting.IsLocked = true;
            setting.UpdatedAt = DateTime.UtcNow;
        }
    }
}
