namespace Event.Api.IntegrationTests.Features;

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Attributes;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.SetupLive;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TUnit.Core;
using TUnit.Core.Interfaces;

[Category(TestCategories.Security)]
[ClassDataSource<SetupLiveAuthorityRedFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class SetupLiveAuthoritySecurityTests(SetupLiveAuthorityRedFixture fixture)
{
    private const string ControllerTypeName =
        "Explore.API.Controllers.SetupTargetEnrollmentsController";
    private const string BaseRouteTemplate =
        "api/tenants/{tenantId:guid}/setup/enrollments";
    private const string CapabilityHeader = "X-Setup-Enrollment-Capability";
    private const string IdempotencyHeader = "Idempotency-Key";
    private const string UnavailableProblemType =
        "/problems/setup-enrollment-unavailable";
    private const string UnavailableProblemTitle =
        "Setup enrollment unavailable";
    private const string UnavailableProblemCode =
        "setup_enrollment_unavailable";
    private const string UnavailableProblemDetail =
        "The requested setup enrollment is unavailable.";
    private const string EnrollmentEntityName =
        "Explore.Domain.SetupLive.SetupTargetEnrollment";
    private const string IssuanceClaimEntityName =
        "Explore.Domain.SetupLive.SetupEnrollmentIssuanceClaim";
    private const string OperationEntityName =
        "Explore.Domain.SetupLive.SetupSecretBindingOperation";
    private static readonly string[] RequestedScopes =
    [
        "target.read",
        "secret_binding.readiness",
        "secret_binding.write"
    ];

    [Test]
    public async Task EndpointFamilyHasExactProtectedMachineMetadata()
    {
        await fixture.ResetAsync();
        Type controller = RequireController();
        RouteAttribute route = controller.GetCustomAttribute<RouteAttribute>()
            ?? throw new InvalidOperationException("missing-setup-live-base-route");

        await Assert.That(route.Template).IsEqualTo(BaseRouteTemplate);
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>())
            .IsNotNull();
        await Assert.That(
                controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);

        await AssertEndpoint(
            controller, "CreateSetupTargetEnrollment", HttpMethods.Post, null,
            "SetupEnrollmentWrite", "SetupEnrollment", 16_384);
        await AssertEndpoint(
            controller, "GetSetupTargetEnrollment", HttpMethods.Get,
            "{enrollmentId:guid}", null, null, null);
        await AssertEndpoint(
            controller, "RevokeSetupTargetEnrollment", HttpMethods.Delete,
            "{enrollmentId:guid}", "SetupEnrollmentWrite", "SetupEnrollment", null);
        await AssertEndpoint(
            controller, "RotateSetupTargetEnrollmentCapability", HttpMethods.Post,
            "{enrollmentId:guid}/capability-rotations",
            "SetupEnrollmentWrite", "SetupEnrollment", null);
        await AssertEndpoint(
            controller, "GetSetupSecretBindingReadiness", HttpMethods.Get,
            "{enrollmentId:guid}/secret-bindings/readiness", null, null, null);
        await AssertEndpoint(
            controller, "WriteSetupSecretBinding", HttpMethods.Put,
            "{enrollmentId:guid}/secret-bindings/{bindingKey}",
            "SetupSecretBindingWrite", "SetupSecretBinding", 65_536);
        await AssertEndpoint(
            controller, "GetSetupSecretBindingOperation", HttpMethods.Get,
            "{enrollmentId:guid}/secret-binding-operations/{operationId:guid}",
            null, null, null);
    }

    [Test]
    public async Task ReadinessPortRemainsValueFreeAndSeparateFromWriter()
    {
        MethodInfo writer = typeof(ISetupSecretBindingWriter).GetMethods().Single();
        MethodInfo readiness = typeof(ISetupSecretBindingReadinessReader)
            .GetMethods().Single();

        await Assert.That(writer.Name).IsEqualTo("WriteAsync");
        await Assert.That(writer.GetParameters().Select(parameter => parameter.ParameterType))
            .IsEquivalentTo(
            [
                typeof(SetupSecretBindingWriteRequest),
                typeof(CancellationToken)
            ]);
        await Assert.That(readiness.Name).IsEqualTo("GetReadinessAsync");
        await Assert.That(readiness.ReturnType)
            .IsEqualTo(typeof(Task<SetupSecretBindingWriteOutcome>));
        await Assert.That(readiness.GetParameters().Select(parameter => parameter.ParameterType))
            .IsEquivalentTo([typeof(Guid), typeof(string), typeof(CancellationToken)]);
    }

    [Test]
    public async Task AnonymousCreateRequiresAuthenticationAndPrivateNoStore()
    {
        await fixture.ResetAsync();
        using HttpRequestMessage request = CreateEnrollmentRequest(
            fixture.Primary.TenantId, userId: null, Guid.CreateVersion7(), NewToken());
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
    }

    [Test]
    public async Task InvalidCapabilityIsRejectedBeforeSecretBodyRead()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        fixture.BodyProbe.Reset();
        using HttpRequestMessage request = SecretWriteRequest(
            enrollment, "setup.signing", NewLongToken(), Guid.CreateVersion7());
        request.Headers.Remove(CapabilityHeader);
        request.Headers.Add(CapabilityHeader, NewToken());
        request.Headers.Add(UnreadBodyProbe.HeaderName, "1");

        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        await AssertUnavailableProblem(response);
        await Assert.That(fixture.BodyProbe.ReadCount).IsEqualTo(0);
        await Assert.That(fixture.CommitmentAuthority.CallCount).IsEqualTo(0);
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UnresolvableAuthenticatedPrincipalReceivesPlatformProblemDetails()
    {
        await fixture.ResetAsync();
        using HttpRequestMessage create = CreateEnrollmentRequest(
            fixture.Primary.TenantId,
            userId: null,
            Guid.CreateVersion7(),
            NewToken());
        AuthenticateWithoutUserId(create);
        using HttpResponseMessage createResponse = await fixture.Client.SendAsync(create);
        await AssertAuthenticationRequiredProblem(createResponse);

        using var read = new HttpRequestMessage(
            HttpMethod.Get,
            EnrollmentRoute(fixture.Primary.TenantId, Guid.CreateVersion7()));
        AuthenticateWithoutUserId(read);
        read.Headers.Add(CapabilityHeader, NewToken());
        using HttpResponseMessage readResponse = await fixture.Client.SendAsync(read);
        await AssertAuthenticationRequiredProblem(readResponse);
    }

    [Test]
    public async Task IssuanceIsOneTimeDigestOnlyAndDuplicateIsValueFree()
    {
        await fixture.ResetAsync();
        string challenge = NewToken();
        Guid operationKey = Guid.CreateVersion7();

        EnrollmentResponse first = await CreateEnrollment(
            fixture.Primary, operationKey, challenge, HttpStatusCode.Created);
        EnrollmentResponse duplicate = await CreateEnrollment(
            fixture.Primary, operationKey, challenge, HttpStatusCode.OK);

        await Assert.That(first.Capability).IsNotNull();
        await Assert.That(first.Capability!.Length).IsEqualTo(43);
        await Assert.That(Base64UrlDecode(first.Capability).Length).IsEqualTo(32);
        await Assert.That(duplicate.Capability).IsNull();
        await Assert.That(duplicate.EnrollmentId).IsEqualTo(first.EnrollmentId);
        await Assert.That(duplicate.Generation).IsEqualTo(first.Generation);
        await Assert.That(duplicate.Issuance).IsEqualTo("already_issued");

        await AssertEntityCount(EnrollmentEntityName, 1);
        await AssertEntityCount(IssuanceClaimEntityName, 1);
        await AssertStoredRowsDoNotContain(
            [first.Capability, challenge], EnrollmentEntityName, IssuanceClaimEntityName);
        await AssertCapturedDoesNotContain(
        [
            first.Capability,
            challenge
        ]);
        await Assert.That(fixture.Capture.Snapshot())
            .Contains(SetupLiveCapture.SourceName);
        await Assert.That(fixture.Capture.Snapshot().Any(value => value.Contains(
            ":islamu.setup.live.operation.count:", StringComparison.Ordinal))).IsTrue();
        await Assert.That(fixture.Capture.Snapshot().Any(value => value.Contains(
            ":islamu.setup.live.operation.duration:", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task CreateReplayFailsClosedAfterRotationAndRevocation()
    {
        await fixture.ResetAsync();
        Guid operationKey = Guid.CreateVersion7();
        string challenge = NewToken();
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, operationKey, challenge, HttpStatusCode.Created);
        using HttpRequestMessage rotation = RotationRequest(
            enrollment,
            enrollment.Capability!,
            Guid.CreateVersion7());
        using HttpResponseMessage rotationResponse = await fixture.Client.SendAsync(rotation);
        await Assert.That(rotationResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string rotated = rotationResponse.Headers.GetValues(CapabilityHeader).Single();
        using JsonDocument rotationDocument = JsonDocument.Parse(
            await rotationResponse.Content.ReadAsStringAsync());
        long rotatedGeneration = rotationDocument.RootElement
            .GetProperty("generation").GetInt64();
        DateTimeOffset rotatedExpiresAt = rotationDocument.RootElement
            .GetProperty("expiresAt").GetDateTimeOffset();

        using (HttpRequestMessage replayAfterRotation = CreateEnrollmentRequest(
                   fixture.Primary.TenantId,
                   fixture.Primary.UserId,
                   operationKey,
                   challenge))
        using (HttpResponseMessage response =
               await fixture.Client.SendAsync(replayAfterRotation))
        {
            await AssertUnavailableProblem(response);
            await Assert.That(response.Headers.Contains(CapabilityHeader)).IsFalse();
        }
        await AssertEntityCount(EnrollmentEntityName, 1);
        await AssertEntityCount(IssuanceClaimEntityName, 2);
        await AssertClaimCount(operationKey, 1);
        await AssertEnrollmentState(
            enrollment.EnrollmentId,
            "Active",
            rotatedGeneration,
            revoked: false,
            expectedExpiresAt: rotatedExpiresAt);
        using (HttpRequestMessage read = EnrollmentRequest(
                   fixture.Primary,
                   enrollment.EnrollmentId,
                   fixture.Primary.UserId,
                   rotated))
        using (HttpResponseMessage response = await fixture.Client.SendAsync(read))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            await Assert.That(document.RootElement.GetProperty("state").GetString())
                .IsEqualTo("active");
            await Assert.That(document.RootElement.GetProperty("generation").GetInt64())
                .IsEqualTo(rotatedGeneration);
            await Assert.That(document.RootElement.GetProperty("expiresAt").GetDateTimeOffset())
                .IsEqualTo(rotatedExpiresAt);
        }

        using (HttpRequestMessage revoke = RevokeRequest(
                   enrollment with { Capability = rotated },
                   Guid.CreateVersion7()))
        using (HttpResponseMessage response = await fixture.Client.SendAsync(revoke))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        using (HttpRequestMessage replayAfterRevocation = CreateEnrollmentRequest(
                   fixture.Primary.TenantId,
                   fixture.Primary.UserId,
                   operationKey,
                   challenge))
        using (HttpResponseMessage response =
               await fixture.Client.SendAsync(replayAfterRevocation))
        {
            await AssertUnavailableProblem(response);
            await Assert.That(response.Headers.Contains(CapabilityHeader)).IsFalse();
        }
        await AssertEntityCount(EnrollmentEntityName, 1);
        await AssertEntityCount(IssuanceClaimEntityName, 3);
        await AssertClaimCount(operationKey, 1);
        await AssertEnrollmentState(
            enrollment.EnrollmentId,
            "Revoked",
            rotatedGeneration,
            revoked: true,
            expectedExpiresAt: rotatedExpiresAt);
    }

    [Test]
    public async Task RequestBytesAreMeasurementsNotMetricLabels()
    {
        await fixture.ResetAsync();
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        var telemetry = scope.ServiceProvider.GetRequiredService<
            Explore.Application.Telemetry.SetupLiveTelemetry>();

        using (Explore.Application.Telemetry.SetupLiveTelemetry.Operation operation =
               telemetry.Start("enrollment.create", 12_345))
        {
            operation.Complete("created");
        }

        string[] captured = fixture.Capture.Snapshot();
        await Assert.That(captured.Any(value => value.Contains(
            ":islamu.setup.live.operation.count:", StringComparison.Ordinal))).IsTrue();
        await Assert.That(captured.Any(value => value.Contains(
            ":islamu.setup.live.operation.duration:", StringComparison.Ordinal))).IsTrue();
        await Assert.That(captured.Any(value => value.StartsWith(
            "metric:request.bytes=", StringComparison.Ordinal))).IsFalse();
        await Assert.That(captured).Contains("activity:request.bytes=12345");
    }

    [Test]
    public async Task InvalidCapabilitiesAreFieldEquivalentAndProduceNoEffect()
    {
        await fixture.ResetAsync();
        EnrollmentResponse active = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        using HttpRequestMessage baselineRequest = EnrollmentRequest(
            fixture.Primary, active.EnrollmentId, fixture.Primary.UserId, capability: null);
        ProblemTuple baseline = await SendForProblem(baselineRequest);

        var requests = new List<HttpRequestMessage>
        {
            EnrollmentRequest(
                fixture.Primary, active.EnrollmentId, fixture.Primary.UserId, NewShortToken()),
            EnrollmentRequest(
                fixture.Primary, active.EnrollmentId, fixture.Primary.UserId, NewLongToken()),
            EnrollmentRequest(
                fixture.Primary, active.EnrollmentId, fixture.Primary.UserId, NewToken()),
            EnrollmentRequest(
                fixture.Primary, active.EnrollmentId, fixture.Secondary.UserId, active.Capability),
            EnrollmentRequest(
                new TenantScenarioSeed.TenantScenarioResult(
                    Guid.CreateVersion7(), fixture.Primary.UserId, fixture.Primary.ActorId),
                active.EnrollmentId, fixture.Primary.UserId, active.Capability)
        };

        try
        {
            foreach (HttpRequestMessage request in requests)
            {
                ProblemTuple current = await SendForProblem(request);
                await Assert.That(current).IsEqualTo(baseline);
            }
        }
        finally
        {
            foreach (HttpRequestMessage request in requests)
                request.Dispose();
        }

        fixture.Authorization.AllowAll = false;
        try
        {
            using HttpRequestMessage denied = EnrollmentRequest(
                fixture.Primary, active.EnrollmentId, fixture.Primary.UserId, active.Capability);
            ProblemTuple current = await SendForProblem(denied);
            await Assert.That(current).IsEqualTo(baseline);
        }
        finally
        {
            fixture.Authorization.AllowAll = true;
        }

        EnrollmentResponse revoked = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        await Revoke(revoked);
        using (HttpRequestMessage request = EnrollmentRequest(
                   fixture.Primary, revoked.EnrollmentId, fixture.Primary.UserId, revoked.Capability))
        {
            await Assert.That(await SendForProblem(request)).IsEqualTo(baseline);
        }

        EnrollmentResponse expired = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        fixture.Clock.SetUtcNow(expired.ExpiresAt.AddTicks(1));
        using (HttpRequestMessage request = EnrollmentRequest(
                   fixture.Primary, expired.EnrollmentId, fixture.Primary.UserId, expired.Capability))
        {
            await Assert.That(await SendForProblem(request)).IsEqualTo(baseline);
        }
        fixture.Clock.Reset();

        EnrollmentResponse stale = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string rotatedCapability = await Rotate(stale);
        await Assert.That(rotatedCapability).IsNotEqualTo(stale.Capability);
        using (HttpRequestMessage request = EnrollmentRequest(
                   fixture.Primary, stale.EnrollmentId, fixture.Primary.UserId, stale.Capability))
        {
            await Assert.That(await SendForProblem(request)).IsEqualTo(baseline);
        }

        await AssertEntityCount(OperationEntityName, 0);
    }

    [Test]
    public async Task BodyAuthoritySmugglingIsRejectedBeforePersistence()
    {
        await fixture.ResetAsync();
        Guid tenantId = fixture.Primary.TenantId;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, EnrollmentCollectionRoute(tenantId));
        Authenticate(request, fixture.Primary.UserId, tenantId);
        request.Headers.Add(IdempotencyHeader, Guid.CreateVersion7().ToString("D"));
        request.Content = JsonContent.Create(new
        {
            clientChallenge = NewToken(),
            requestedScopes = RequestedScopes,
            tenantId = Guid.CreateVersion7(),
            targetUrl = $"https://{NewToken()}.invalid",
            actorUserId = Guid.CreateVersion7(),
            provider = NewToken(),
            capability = NewToken()
        });

        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertEntityCount(EnrollmentEntityName, 0);
    }

    [Test]
    public async Task OperationKeysRequireUuidV7AndChangedFingerprintConflicts()
    {
        await fixture.ResetAsync();
        string challenge = NewToken();
        Guid validKey = Guid.CreateVersion7();
        EnrollmentResponse created = await CreateEnrollment(
            fixture.Primary, validKey, challenge, HttpStatusCode.Created);

        using HttpRequestMessage changed = CreateEnrollmentRequest(
            fixture.Primary.TenantId,
            fixture.Primary.UserId,
            validKey,
            NewToken());
        using HttpResponseMessage changedResponse = await fixture.Client.SendAsync(changed);
        await AssertConflictProblem(changedResponse);

        foreach (string? invalid in new[]
                 {
                     null,
                     Guid.NewGuid().ToString("D"),
                     NewToken()
                 })
        {
            using HttpRequestMessage request = CreateEnrollmentRequest(
                fixture.Primary.TenantId,
                fixture.Primary.UserId,
                idempotencyKey: null,
                NewToken());
            if (invalid is not null)
                request.Headers.Add(IdempotencyHeader, invalid);
            using HttpResponseMessage response = await fixture.Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }

        await AssertEntityCount(EnrollmentEntityName, 1);
        await Assert.That(created.EnrollmentId).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task HalAndReadinessSurfacesRemainValueAndCoordinateFree()
    {
        await fixture.ResetAsync();
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string canary = NewLongToken();

        await AssertHal(enrollment, canary);

        using HttpRequestMessage readiness = ReadinessRequest(enrollment);
        using HttpResponseMessage readinessResponse = await fixture.Client.SendAsync(readiness);
        string readinessBody = await readinessResponse.Content.ReadAsStringAsync();
        await Assert.That(readinessResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(readinessBody).DoesNotContain(canary);
        await Assert.That(readinessBody).DoesNotContain("provider", StringComparison.OrdinalIgnoreCase);
        await Assert.That(readinessBody).DoesNotContain("environment", StringComparison.OrdinalIgnoreCase);
        await Assert.That(readinessBody).DoesNotContain("path", StringComparison.OrdinalIgnoreCase);
        using JsonDocument readinessDocument = JsonDocument.Parse(readinessBody);
        JsonElement[] items = readinessDocument.RootElement.GetProperty("_embedded")
            .GetProperty("items").EnumerateArray().ToArray();
        await Assert.That(items.Select(item => item.GetProperty("bindingKey").GetString()))
            .IsEquivalentTo(["setup.signing", "setup.encryption"]);
        await Assert.That(items.Select(item => item.GetProperty("state").GetString()))
            .IsEquivalentTo(["unavailable", "unavailable"]);

        using var forbiddenRead = new HttpRequestMessage(
            HttpMethod.Get, SecretWriteRoute(enrollment, "setup.signing"));
        Authenticate(forbiddenRead, fixture.Primary.UserId, fixture.Primary.TenantId);
        forbiddenRead.Headers.Add(CapabilityHeader, enrollment.Capability);
        using HttpResponseMessage forbiddenReadResponse =
            await fixture.Client.SendAsync(forbiddenRead);
        await Assert.That(forbiddenReadResponse.StatusCode)
            .IsEqualTo(HttpStatusCode.MethodNotAllowed);

        await AssertEntityCount(OperationEntityName, 0);
        await AssertCapturedDoesNotContain([canary]);
    }

    [Test]
    public async Task ReadinessWriteAffordanceRequiresBindingAndCurrentUpdateAuthority()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);

        using HttpRequestMessage readiness = ReadinessRequest(enrollment);
        using HttpResponseMessage response = await fixture.Client.SendAsync(readiness);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement[] items = document.RootElement.GetProperty("_embedded")
            .GetProperty("items").EnumerateArray().ToArray();
        JsonElement signing = items.Single(item =>
            item.GetProperty("bindingKey").GetString() == "setup.signing");
        JsonElement encryption = items.Single(item =>
            item.GetProperty("bindingKey").GetString() == "setup.encryption");
        await Assert.That(signing.GetProperty("state").GetString()).IsEqualTo("ready");
        JsonElement signingLinks = signing.GetProperty("_links");
        await Assert.That(signingLinks.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["write-secret-binding"]);
        string signingAction = signingLinks.GetProperty("write-secret-binding").GetRawText();
        await Assert.That(signingAction).Contains(SecretWriteRoute(enrollment, "setup.signing"));
        await Assert.That(signingAction).Contains(HttpMethods.Put);
        await Assert.That(encryption.GetProperty("state").GetString())
            .IsEqualTo("unconfigured");
        await Assert.That(encryption.TryGetProperty("_links", out JsonElement encryptionLinks)
                && encryptionLinks.EnumerateObject().Any())
            .IsFalse();

        fixture.Authorization.CheckPredicate = request =>
            request.Action == AuthorizationActions.Tenants.View;
        using HttpRequestMessage deniedReadiness = ReadinessRequest(enrollment);
        using HttpResponseMessage deniedResponse =
            await fixture.Client.SendAsync(deniedReadiness);
        await Assert.That(deniedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument deniedDocument = JsonDocument.Parse(
            await deniedResponse.Content.ReadAsStringAsync());
        foreach (JsonElement item in deniedDocument.RootElement.GetProperty("_embedded")
                     .GetProperty("items").EnumerateArray())
        {
            await Assert.That(item.TryGetProperty("_links", out JsonElement links)
                    && links.EnumerateObject().Any())
                .IsFalse();
        }
    }

    [Test]
    [Arguments(SetupSecretBindingWriteOutcome.Unavailable, "unavailable")]
    [Arguments(SetupSecretBindingWriteOutcome.Invalid, "invalid")]
    [Arguments(SetupSecretBindingWriteOutcome.Unauthorized, "unauthorized")]
    public async Task ReadinessSuppressesImpossibleWriteAffordances(
        SetupSecretBindingWriteOutcome outcome,
        string expectedState)
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        fixture.Writer.ReadinessOutcome = outcome;
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);

        using HttpRequestMessage request = ReadinessRequest(enrollment);
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement signing = document.RootElement.GetProperty("_embedded")
            .GetProperty("items").EnumerateArray().Single(item =>
                item.GetProperty("bindingKey").GetString() == "setup.signing");
        await Assert.That(signing.GetProperty("state").GetString())
            .IsEqualTo(expectedState);
        await Assert.That(signing.TryGetProperty("_links", out JsonElement links)
                && links.EnumerateObject().Any())
            .IsFalse();
    }

    [Test]
    public async Task ReadinessScopeAloneNeverAdvertisesWriteAuthority()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary,
            Guid.CreateVersion7(),
            NewToken(),
            HttpStatusCode.Created,
            ["secret_binding.readiness"]);

        using HttpRequestMessage request = ReadinessRequest(enrollment);
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement signing = document.RootElement.GetProperty("_embedded")
            .GetProperty("items").EnumerateArray().Single(item =>
                item.GetProperty("bindingKey").GetString() == "setup.signing");
        await Assert.That(signing.GetProperty("state").GetString()).IsEqualTo("ready");
        await Assert.That(signing.TryGetProperty("_links", out JsonElement links)
                && links.EnumerateObject().Any())
            .IsFalse();
    }

    [Test]
    public async Task HalAffordancesRequireTheCorrespondingEnrollmentScope()
    {
        await fixture.ResetAsync();
        using HttpRequestMessage request = CreateEnrollmentRequest(
            fixture.Primary.TenantId,
            fixture.Primary.UserId,
            Guid.CreateVersion7(),
            NewToken(),
            ["target.read"]);
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("_links")
                .EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["self", "revoke", "rotate-capability"]);

        string capability = response.Headers.GetValues(CapabilityHeader).Single();
        var enrollment = new EnrollmentResponse(
            fixture.Primary,
            document.RootElement.GetProperty("enrollmentId").GetGuid(),
            document.RootElement.GetProperty("generation").GetInt64(),
            document.RootElement.GetProperty("expiresAt").GetDateTimeOffset(),
            document.RootElement.GetProperty("issuance").GetString()!,
            capability,
            document.RootElement.Clone());
        using HttpRequestMessage readiness = ReadinessRequest(enrollment);
        using HttpResponseMessage readinessResponse = await fixture.Client.SendAsync(readiness);
        await AssertUnavailableProblem(readinessResponse);
    }

    [Test]
    public async Task HalMutationAffordancesRequireCurrentUpdateAuthorization()
    {
        await fixture.ResetAsync();
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);

        using (HttpRequestMessage allowedRequest = EnrollmentRequest(
                   fixture.Primary,
                   enrollment.EnrollmentId,
                   fixture.Primary.UserId,
                   enrollment.Capability))
        using (HttpResponseMessage allowedResponse =
               await fixture.Client.SendAsync(allowedRequest))
        {
            await Assert.That(allowedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using JsonDocument allowedDocument = JsonDocument.Parse(
                await allowedResponse.Content.ReadAsStringAsync());
            await Assert.That(allowedDocument.RootElement.GetProperty("_links")
                    .EnumerateObject().Select(property => property.Name))
                .IsEquivalentTo(
                [
                    "self", "revoke", "rotate-capability",
                    "secret-binding-readiness"
                ]);
        }

        fixture.Authorization.CheckPredicate = request =>
            request.Action == AuthorizationActions.Tenants.View;

        using HttpRequestMessage request = EnrollmentRequest(
            fixture.Primary,
            enrollment.EnrollmentId,
            fixture.Primary.UserId,
            enrollment.Capability);
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("_links")
                .EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["self", "secret-binding-readiness"]);
    }

    [Test]
    public async Task RevokeIdempotencyKeyCannotMutateDifferentEnrollments()
    {
        await fixture.ResetAsync();
        EnrollmentResponse first = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        EnrollmentResponse second = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        Guid operationKey = Guid.CreateVersion7();

        using HttpRequestMessage firstRequest = RevokeRequest(first, operationKey);
        using HttpResponseMessage firstResponse = await fixture.Client.SendAsync(firstRequest);
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using HttpRequestMessage replayRequest = RevokeRequest(first, operationKey);
        using HttpResponseMessage replayResponse = await fixture.Client.SendAsync(replayRequest);
        await AssertUnavailableProblem(replayResponse);

        using HttpRequestMessage secondRequest = RevokeRequest(second, operationKey);
        using HttpResponseMessage secondResponse = await fixture.Client.SendAsync(secondRequest);
        await AssertConflictProblem(secondResponse);
        await AssertEnrollmentState(
            first.EnrollmentId, "Revoked", first.Generation, revoked: true);
        await AssertEnrollmentState(
            second.EnrollmentId, "Active", second.Generation, revoked: false);
        await AssertClaimCount(operationKey, 1);
    }

    [Test]
    public async Task RotationReplayIsValueFreeAndReauthorizesBeforeReceipt()
    {
        await fixture.ResetAsync();
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        Guid operationKey = Guid.CreateVersion7();

        using HttpRequestMessage firstRequest = RotationRequest(
            enrollment, enrollment.Capability!, operationKey);
        using HttpResponseMessage firstResponse = await fixture.Client.SendAsync(firstRequest);
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string rotated = firstResponse.Headers.GetValues(CapabilityHeader).Single();
        using JsonDocument firstDocument = JsonDocument.Parse(
            await firstResponse.Content.ReadAsStringAsync());
        long generation = firstDocument.RootElement.GetProperty("generation").GetInt64();

        using HttpRequestMessage duplicateRequest = RotationRequest(
            enrollment, enrollment.Capability!, operationKey);
        using HttpResponseMessage duplicateResponse =
            await fixture.Client.SendAsync(duplicateRequest);
        await Assert.That(duplicateResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(duplicateResponse.Headers.Contains(CapabilityHeader)).IsFalse();
        using JsonDocument duplicateDocument = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync());
        await Assert.That(duplicateDocument.RootElement.GetProperty("generation").GetInt64())
            .IsEqualTo(generation);

        fixture.Authorization.AllowAll = false;
        try
        {
            using HttpRequestMessage deniedRequest = RotationRequest(
                enrollment, enrollment.Capability!, operationKey);
            using HttpResponseMessage deniedResponse =
                await fixture.Client.SendAsync(deniedRequest);
            await AssertUnavailableProblem(deniedResponse);
            await Assert.That(deniedResponse.Headers.Contains(CapabilityHeader)).IsFalse();
        }
        finally
        {
            fixture.Authorization.AllowAll = true;
        }

        await AssertStoredRowsDoNotContain(
            [enrollment.Capability, rotated],
            EnrollmentEntityName,
            IssuanceClaimEntityName);
    }

    [Test]
    public async Task RotationReplayFailsClosedAfterEnrollmentRevocation()
    {
        await fixture.ResetAsync();
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        Guid operationKey = Guid.CreateVersion7();

        using HttpRequestMessage firstRequest = RotationRequest(
            enrollment, enrollment.Capability!, operationKey);
        using HttpResponseMessage firstResponse = await fixture.Client.SendAsync(firstRequest);
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string rotated = firstResponse.Headers.GetValues(CapabilityHeader).Single();
        using JsonDocument rotatedDocument = JsonDocument.Parse(
            await firstResponse.Content.ReadAsStringAsync());
        long generation = rotatedDocument.RootElement.GetProperty("generation").GetInt64();

        using HttpRequestMessage revoke = RevokeRequest(
            enrollment with { Capability = rotated },
            Guid.CreateVersion7());
        using HttpResponseMessage revokeResponse = await fixture.Client.SendAsync(revoke);
        await Assert.That(revokeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using HttpRequestMessage replay = RotationRequest(
            enrollment, enrollment.Capability!, operationKey);
        using HttpResponseMessage replayResponse = await fixture.Client.SendAsync(replay);
        await AssertUnavailableProblem(replayResponse);
        await Assert.That(replayResponse.Headers.Contains(CapabilityHeader)).IsFalse();
        await AssertEnrollmentState(
            enrollment.EnrollmentId, "Revoked", generation, revoked: true);
        await AssertClaimCount(operationKey, 1);
    }

    [Test]
    public async Task SecretBindingWriteRemainsValueAndCoordinateFree()
    {
        await fixture.ResetAsync();
        Guid bindingId = await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string secret = NewLongToken();
        Guid operationKey = Guid.CreateVersion7();
        using HttpRequestMessage write = SecretWriteRequest(
            enrollment, "setup.signing", secret, operationKey);
        using HttpResponseMessage response = await fixture.Client.SendAsync(write);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        OperationReceipt receipt = await AssertOperationReceipt(
            response,
            enrollment,
            expectedState: "succeeded",
            expectedOutcome: "ready");
        await Assert.That(body).DoesNotContain(secret);
        await Assert.That(body).DoesNotContain("provider", StringComparison.OrdinalIgnoreCase);
        await Assert.That(body).DoesNotContain("environment", StringComparison.OrdinalIgnoreCase);
        await Assert.That(body).DoesNotContain("path", StringComparison.OrdinalIgnoreCase);
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(1);
        await Assert.That(fixture.Writer.LastRequest!.BindingId).IsEqualTo(bindingId);
        await Assert.That(fixture.Writer.LastSecretDigest)
            .IsEqualTo(Digest(Encoding.UTF8.GetBytes(secret)));
        await Assert.That(fixture.Writer.LastBorrowedValue.Span.ToArray()
                .All(static value => value == 0))
            .IsTrue();
        await Assert.That(fixture.Writer.LastRequest!.TenantId)
            .IsEqualTo(enrollment.Scenario.TenantId);
        await Assert.That(fixture.Writer.LastRequest.EnrollmentId)
            .IsEqualTo(enrollment.EnrollmentId);
        await Assert.That(fixture.Writer.LastRequest.EnrollmentGeneration)
            .IsEqualTo(enrollment.Generation);
        await Assert.That(fixture.Writer.LastRequest.OperationId)
            .IsEqualTo(receipt.OperationId);
        await Assert.That(fixture.Writer.LastRequest.BindingId).IsEqualTo(bindingId);
        await Assert.That(fixture.Writer.LastRequest.BindingKey).IsEqualTo("setup.signing");
        await Assert.That(fixture.CommitmentAuthority.CallCount).IsEqualTo(1);
        await Assert.That(fixture.CommitmentAuthority.LastRequest!.TenantId)
            .IsEqualTo(enrollment.Scenario.TenantId);
        await Assert.That(fixture.CommitmentAuthority.LastRequest.ActorId)
            .IsEqualTo(fixture.Primary.ActorId);
        await Assert.That(fixture.CommitmentAuthority.LastRequest.EnrollmentId)
            .IsEqualTo(enrollment.EnrollmentId);
        await Assert.That(fixture.CommitmentAuthority.LastRequest.EnrollmentGeneration)
            .IsEqualTo(enrollment.Generation);
        await Assert.That(fixture.CommitmentAuthority.LastRequest.OperationKey)
            .IsEqualTo(operationKey);
        await Assert.That(fixture.CommitmentAuthority.LastRequest.BindingKey)
            .IsEqualTo("setup.signing");
        await Assert.That(fixture.CommitmentAuthority.LastSecretDigest)
            .IsEqualTo(Digest(Encoding.UTF8.GetBytes(secret)));
        await Assert.That(fixture.CommitmentAuthority.LastBorrowedValue.Span.ToArray()
                .All(static value => value == 0))
            .IsTrue();
        SetupSecretBindingOperation operation = await LoadOperation(operationKey);
        await AssertOperation(
            operation,
            enrollment,
            operationKey,
            "setup.signing",
            SetupSecretBindingOperationState.Succeeded,
            SetupSecretBindingOperationOutcome.Ready,
            fixture.CommitmentAuthority.LastCommitment!);
        await Assert.That(operation.Id).IsEqualTo(receipt.OperationId);
        await Assert.That(fixture.CommitBarrier.CallCount).IsEqualTo(1);
        await Assert.That(fixture.Coordinator.AcquireCount).IsEqualTo(1);
        await Assert.That(fixture.Coordinator.DisposeCount).IsEqualTo(1);
        SetupSecretBindingCoordinationRequest coordination =
            fixture.Coordinator.Snapshot().Single();
        await Assert.That(coordination.TenantId).IsEqualTo(enrollment.Scenario.TenantId);
        await Assert.That(coordination.EnrollmentId).IsEqualTo(enrollment.EnrollmentId);
        await Assert.That(coordination.EnrollmentGeneration).IsEqualTo(enrollment.Generation);
        await AssertSetupTelemetryDoesNotContain(
        [
            secret,
            enrollment.Capability,
            enrollment.Scenario.TenantId.ToString("D"),
            enrollment.Scenario.ActorId.ToString("D"),
            enrollment.EnrollmentId.ToString("D"),
            operationKey.ToString("D"),
            receipt.OperationId.ToString("D"),
            bindingId.ToString("D"),
            "setup.signing",
            "ISLAMU_SETUP_SIGNING",
            fixture.CommitmentAuthority.LastCommitment,
            "forbidden-field:"
        ]);

        using HttpRequestMessage read = OperationReadRequest(enrollment, receipt.OperationId);
        using HttpResponseMessage readResponse = await fixture.Client.SendAsync(read);
        await Assert.That(readResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        _ = await AssertOperationReceipt(
            readResponse,
            enrollment,
            expectedState: "succeeded",
            expectedOutcome: "ready",
            receipt.OperationId);

        using HttpRequestMessage deniedRead = OperationReadRequest(
            enrollment with { Capability = NewToken() },
            receipt.OperationId);
        using HttpResponseMessage deniedReadResponse = await fixture.Client.SendAsync(deniedRead);
        await AssertUnavailableProblem(deniedReadResponse);
        using HttpRequestMessage wrongActorRead = OperationReadRequest(
            enrollment with { Scenario = fixture.Secondary },
            receipt.OperationId);
        using HttpResponseMessage wrongActorReadResponse =
            await fixture.Client.SendAsync(wrongActorRead);
        await AssertUnavailableProblem(wrongActorReadResponse);
        await AssertStoredRowsDoNotContain([secret], OperationEntityName);
        await AssertCapturedDoesNotContain([secret]);
    }

    [Test]
    [Arguments(SetupSecretBindingWriteOutcome.Unavailable, "unavailable")]
    [Arguments(SetupSecretBindingWriteOutcome.Unauthorized, "unauthorized")]
    [Arguments(SetupSecretBindingWriteOutcome.Invalid, "invalid")]
    public async Task SecretWriteProviderFailureIsTerminalAndValueFree(
        SetupSecretBindingWriteOutcome writerOutcome,
        string expectedOutcome)
    {
        await fixture.ResetAsync();
        fixture.Writer.Outcome = writerOutcome;
        Guid bindingId = await fixture.AddSetupBinding("setup.encryption");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string secret = NewLongToken();
        Guid operationKey = Guid.CreateVersion7();
        using HttpRequestMessage write = SecretWriteRequest(
            enrollment, "setup.encryption", secret, operationKey);
        using HttpResponseMessage response = await fixture.Client.SendAsync(write);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        OperationReceipt receipt = await AssertOperationReceipt(
            response,
            enrollment,
            expectedState: "failed",
            expectedOutcome);
        SetupSecretBindingOperation operation = await LoadOperation(operationKey);
        await AssertOperation(
            operation,
            enrollment,
            operationKey,
            "setup.encryption",
            SetupSecretBindingOperationState.Failed,
            Enum.Parse<SetupSecretBindingOperationOutcome>(
                expectedOutcome,
                ignoreCase: true),
            fixture.CommitmentAuthority.LastCommitment!);
        await Assert.That(operation.Id).IsEqualTo(receipt.OperationId);
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(1);
        await Assert.That(fixture.CommitmentAuthority.CallCount).IsEqualTo(1);
        await Assert.That(fixture.CommitBarrier.CallCount).IsEqualTo(1);
        await Assert.That(fixture.Coordinator.AcquireCount).IsEqualTo(1);
        await Assert.That(fixture.Coordinator.DisposeCount).IsEqualTo(1);
        await AssertStoredRowsDoNotContain([secret], OperationEntityName);
        await AssertCapturedDoesNotContain([secret]);
    }

    [Test]
    public async Task CommitmentAuthorityFailureCreatesNoDispatchableReceiptOrProviderCall()
    {
        await fixture.ResetAsync();
        fixture.CommitmentAuthority.FailClosed = true;
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string secret = NewLongToken();
        Guid operationKey = Guid.CreateVersion7();
        using HttpRequestMessage write = SecretWriteRequest(
            enrollment, "setup.signing", secret, operationKey);
        using HttpResponseMessage response = await fixture.Client.SendAsync(write);

        await AssertUnavailableProblem(response);
        await Assert.That(fixture.CommitmentAuthority.CallCount).IsEqualTo(1);
        await Assert.That(fixture.CommitmentAuthority.LastBorrowedValue.Span.ToArray()
                .All(static value => value == 0))
            .IsTrue();
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(0);
        await Assert.That(fixture.CommitBarrier.CallCount).IsEqualTo(0);
        await Assert.That(fixture.Coordinator.AcquireCount).IsEqualTo(0);
        await AssertEntityCount(OperationEntityName, 0);
        await AssertStoredRowsDoNotContain(
            [secret, enrollment.Capability], OperationEntityName);
        await AssertCapturedDoesNotContain([secret, enrollment.Capability]);
    }

    [Test]
    public async Task SecretWriteCoordinationUsesRotatedEnrollmentGeneration()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        using HttpRequestMessage rotation = RotationRequest(
            enrollment,
            enrollment.Capability!,
            Guid.CreateVersion7());
        using HttpResponseMessage rotationResponse = await fixture.Client.SendAsync(rotation);
        await Assert.That(rotationResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string rotatedCapability = rotationResponse.Headers
            .GetValues(CapabilityHeader).Single();
        using JsonDocument rotationDocument = JsonDocument.Parse(
            await rotationResponse.Content.ReadAsStringAsync());
        long rotatedGeneration = rotationDocument.RootElement
            .GetProperty("generation").GetInt64();
        EnrollmentResponse rotated = enrollment with
        {
            Capability = rotatedCapability,
            Generation = rotatedGeneration
        };
        string secret = NewLongToken();
        using HttpRequestMessage write = SecretWriteRequest(
            rotated, "setup.signing", secret);
        using HttpResponseMessage response = await fixture.Client.SendAsync(write);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        SetupSecretBindingCoordinationRequest coordination =
            fixture.Coordinator.Snapshot().Last();
        await Assert.That(coordination.TenantId).IsEqualTo(rotated.Scenario.TenantId);
        await Assert.That(coordination.EnrollmentId).IsEqualTo(rotated.EnrollmentId);
        await Assert.That(coordination.EnrollmentGeneration)
            .IsEqualTo(rotatedGeneration);
        await Assert.That(fixture.Coordinator.DisposeCount).IsEqualTo(2);
        await AssertCapturedDoesNotContain([secret, rotatedCapability]);
    }

    [Test]
    public async Task ConcurrentDuplicateSecretWritesDispatchExactlyOnce()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string secret = NewLongToken();
        Guid operationKey = Guid.CreateVersion7();
        using HttpRequestMessage first = SecretWriteRequest(
            enrollment, "setup.signing", secret, operationKey);
        using HttpRequestMessage second = SecretWriteRequest(
            enrollment, "setup.signing", secret, operationKey);

        HttpResponseMessage[] responses = await Task.WhenAll(
                fixture.Client.SendAsync(first),
                fixture.Client.SendAsync(second))
            .WaitAsync(TimeSpan.FromSeconds(10));
        using HttpResponseMessage firstResponse = responses[0];
        using HttpResponseMessage secondResponse = responses[1];

        await Assert.That(responses.All(
                response => response.StatusCode == HttpStatusCode.Accepted))
            .IsTrue();
        OperationReceipt firstReceipt = await AssertOperationReceipt(
            firstResponse, enrollment, "succeeded", "ready");
        OperationReceipt secondReceipt = await AssertOperationReceipt(
            secondResponse, enrollment, "succeeded", "ready");
        await Assert.That(secondReceipt.OperationId).IsEqualTo(firstReceipt.OperationId);
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(1);
        await AssertEntityCount(OperationEntityName, 1);
        SetupSecretBindingOperation operation = await LoadOperation(operationKey);
        await Assert.That(operation.Id).IsEqualTo(firstReceipt.OperationId);
        await Assert.That(operation.State)
            .IsEqualTo(SetupSecretBindingOperationState.Succeeded);
        await Assert.That(operation.Outcome)
            .IsEqualTo(SetupSecretBindingOperationOutcome.Ready);
        await Assert.That(operation.SettledAt).IsNotNull();
        await Assert.That(fixture.Coordinator.AcquireCount).IsEqualTo(2);
        await Assert.That(fixture.Coordinator.DisposeCount).IsEqualTo(2);
        await AssertStoredRowsDoNotContain([secret], OperationEntityName);
        await AssertCapturedDoesNotContain([secret]);
    }

    [Test]
    public async Task SecretWriteIdempotencyConflictDoesNotRedispatch()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        Guid operationKey = Guid.CreateVersion7();
        string firstSecret = NewLongToken();
        string conflictingSecret = NewLongToken();
        using HttpRequestMessage first = SecretWriteRequest(
            enrollment, "setup.signing", firstSecret, operationKey);
        using HttpResponseMessage firstResponse = await fixture.Client.SendAsync(first);
        using HttpRequestMessage conflict = SecretWriteRequest(
            enrollment, "setup.signing", conflictingSecret, operationKey);
        using HttpResponseMessage conflictResponse = await fixture.Client.SendAsync(conflict);

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await AssertConflictProblem(conflictResponse);
        string conflictBody = await conflictResponse.Content.ReadAsStringAsync();
        await Assert.That(conflictBody).DoesNotContain(firstSecret);
        await Assert.That(conflictBody).DoesNotContain(conflictingSecret);
        await Assert.That(conflictBody).DoesNotContain(enrollment.Capability!);
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(1);
        await Assert.That(fixture.CommitmentAuthority.CallCount).IsEqualTo(2);
        await Assert.That(fixture.Coordinator.AcquireCount).IsEqualTo(2);
        await Assert.That(fixture.Coordinator.DisposeCount).IsEqualTo(2);
        await AssertEntityCount(OperationEntityName, 1);
        await AssertStoredRowsDoNotContain(
            [firstSecret, conflictingSecret], OperationEntityName);
        await AssertCapturedDoesNotContain([firstSecret, conflictingSecret]);
    }

    [Test]
    public async Task SecretWriteIdempotencyBindsTheAllowlistedBindingIdentity()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        await fixture.AddSetupBinding("setup.encryption");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        Guid operationKey = Guid.CreateVersion7();
        string secret = NewLongToken();
        using HttpRequestMessage first = SecretWriteRequest(
            enrollment, "setup.signing", secret, operationKey);
        using HttpResponseMessage firstResponse = await fixture.Client.SendAsync(first);
        using HttpRequestMessage conflict = SecretWriteRequest(
            enrollment, "setup.encryption", secret, operationKey);
        using HttpResponseMessage conflictResponse = await fixture.Client.SendAsync(conflict);

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await AssertConflictProblem(conflictResponse);
        string conflictBody = await conflictResponse.Content.ReadAsStringAsync();
        await Assert.That(conflictBody).DoesNotContain(secret);
        await Assert.That(conflictBody).DoesNotContain(enrollment.Capability!);
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(1);
        await Assert.That(fixture.CommitmentAuthority.CallCount).IsEqualTo(2);
        await AssertEntityCount(OperationEntityName, 1);
        SetupSecretBindingOperation operation = await LoadOperation(operationKey);
        await Assert.That(operation.BindingKey).IsEqualTo("setup.signing");
        await Assert.That(operation.State)
            .IsEqualTo(SetupSecretBindingOperationState.Succeeded);
        await AssertStoredRowsDoNotContain([secret], OperationEntityName);
        await AssertCapturedDoesNotContain([secret]);
    }

    [Test]
    public async Task ConcurrentRevocationRequiresExactDispatchMilestoneAndNoDispatchableRow()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string secret = NewLongToken();
        Guid operationKey = Guid.CreateVersion7();
        using CoordinatorAcquireBarrier barrier = fixture.Coordinator.ArmBeforeAcquireBarrier();
        using HttpRequestMessage write = SecretWriteRequest(
            enrollment, "setup.signing", secret, operationKey);
        Task<HttpResponseMessage> writeTask = fixture.Client.SendAsync(write);

        Task completed = await Task.WhenAny(
            barrier.Started, writeTask).WaitAsync(TimeSpan.FromSeconds(10));
        if (completed == writeTask)
        {
            using HttpResponseMessage early = await writeTask;
            await Assert.That(early.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            throw new InvalidOperationException(
                "missing-setup-secret-binding-dispatch-activity");
        }

        using HttpRequestMessage revoke = RevokeRequest(enrollment);
        using HttpResponseMessage revokeResponse = await fixture.Client.SendAsync(revoke);
        await Assert.That(revokeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        barrier.Release();

        using HttpResponseMessage writeResponse =
            await writeTask.WaitAsync(TimeSpan.FromSeconds(10));
        await AssertUnavailableProblem(writeResponse);
        string failureBody = await writeResponse.Content.ReadAsStringAsync();
        await Assert.That(failureBody).DoesNotContain(secret);
        await Assert.That(failureBody).DoesNotContain(enrollment.Capability!);
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(0);
        await Assert.That(fixture.CommitBarrier.CallCount).IsEqualTo(0);
        await Assert.That(fixture.Coordinator.AcquireCount).IsEqualTo(2);
        await Assert.That(fixture.Coordinator.DisposeCount).IsEqualTo(2);
        await AssertEntityCount(OperationEntityName, 0);
        await AssertStoredRowsDoNotContain([secret, enrollment.Capability], OperationEntityName);
        await AssertCapturedDoesNotContain([secret, enrollment.Capability]);
    }

    [Test]
    public async Task ProviderDispatchWinnerRemainsSettledAfterConcurrentRevocation()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string secret = NewLongToken();
        Guid operationKey = Guid.CreateVersion7();
        using ProviderWriteBarrier barrier = fixture.Writer.ArmBarrier();
        using HttpRequestMessage write = SecretWriteRequest(
            enrollment, "setup.signing", secret, operationKey);
        Task<HttpResponseMessage> writeTask = fixture.Client.SendAsync(write);

        Task completed = await Task.WhenAny(
            barrier.Started, writeTask).WaitAsync(TimeSpan.FromSeconds(10));
        if (completed == writeTask)
        {
            using HttpResponseMessage early = await writeTask;
            await Assert.That(early.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            throw new InvalidOperationException("missing-setup-provider-write-barrier");
        }

        using HttpRequestMessage revoke = RevokeRequest(enrollment);
        Task<HttpResponseMessage> revokeTask = fixture.Client.SendAsync(revoke);
        barrier.Release();

        using HttpResponseMessage writeResponse =
            await writeTask.WaitAsync(TimeSpan.FromSeconds(10));
        using HttpResponseMessage revokeResponse =
            await revokeTask.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(revokeResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(writeResponse.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(1);
        await Assert.That(fixture.CommitBarrier.CallCount).IsEqualTo(1);
        await Assert.That(fixture.Coordinator.AcquireCount).IsEqualTo(2);
        await Assert.That(fixture.Coordinator.DisposeCount).IsEqualTo(2);
        await AssertNoDispatchableOperation(enrollment.EnrollmentId);
        SetupSecretBindingOperation operation = await LoadOperation(operationKey);
        await Assert.That(operation.State)
            .IsEqualTo(SetupSecretBindingOperationState.Succeeded);
        await Assert.That(operation.Outcome)
            .IsEqualTo(SetupSecretBindingOperationOutcome.Ready);
        await Assert.That(operation.SettledAt).IsNotNull();
        await AssertStoredRowsDoNotContain([secret], OperationEntityName);
        await AssertCapturedDoesNotContain([secret]);
    }

    [Test]
    public async Task SecretWriteCancellationBeforeDispatchLeavesNoDispatchableOperation()
    {
        await fixture.ResetAsync();
        await fixture.AddSetupBinding("setup.signing");
        EnrollmentResponse enrollment = await CreateEnrollment(
            fixture.Primary, Guid.CreateVersion7(), NewToken(), HttpStatusCode.Created);
        string secret = NewLongToken();
        Guid operationKey = Guid.CreateVersion7();
        using TelemetryBarrier barrier = fixture.Capture.ArmStructuredLogBarrier(
            "secret_binding.write", "before_provider_dispatch");
        using HttpRequestMessage write = SecretWriteRequest(
            enrollment, "setup.signing", secret, operationKey);
        using var cancellation = new CancellationTokenSource();
        Task<HttpResponseMessage> writeTask =
            fixture.Client.SendAsync(write, cancellation.Token);

        Task completed = await Task.WhenAny(
            barrier.Started, writeTask).WaitAsync(TimeSpan.FromSeconds(10));
        if (completed == writeTask)
        {
            using HttpResponseMessage early = await writeTask;
            await Assert.That(early.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            throw new InvalidOperationException("missing-setup-secret-binding-dispatch-activity");
        }

        cancellation.Cancel();
        barrier.Release();
        await Assert.That(async () =>
                await writeTask.WaitAsync(TimeSpan.FromSeconds(10)))
            .Throws<OperationCanceledException>();
        await Assert.That(fixture.Writer.CallCount).IsEqualTo(0);
        await Assert.That(fixture.CommitBarrier.CallCount).IsEqualTo(1);
        await Assert.That(fixture.Coordinator.AcquireCount).IsEqualTo(1);
        await Assert.That(fixture.Coordinator.DisposeCount).IsEqualTo(1);
        await AssertNoDispatchableOperation(enrollment.EnrollmentId);
        SetupSecretBindingOperation operation = await LoadOperation(operationKey);
        await Assert.That(operation.State)
            .IsEqualTo(SetupSecretBindingOperationState.Cancelled);
        await Assert.That(operation.Outcome)
            .IsEqualTo(SetupSecretBindingOperationOutcome.Cancelled);
        await Assert.That(operation.SettledAt).IsNotNull();
        await AssertStoredRowsDoNotContain([secret], OperationEntityName);
        await AssertCapturedDoesNotContain([secret]);
    }

    [Test]
    public async Task CancellationAtIssuanceLeavesNoCredentialOrReceiptResidue()
    {
        await fixture.ResetAsync();
        string challenge = NewToken();
        Guid operationKey = Guid.CreateVersion7();
        using TelemetryBarrier barrier = fixture.Capture.ArmStructuredLogBarrier(
            "enrollment.create", "before_commit");
        using HttpRequestMessage request = CreateEnrollmentRequest(
            fixture.Primary.TenantId,
            fixture.Primary.UserId,
            operationKey,
            challenge);
        using var cancellation = new CancellationTokenSource();
        Task<HttpResponseMessage> responseTask =
            fixture.Client.SendAsync(request, cancellation.Token);

        Task completed = await Task.WhenAny(
            barrier.Started, responseTask).WaitAsync(TimeSpan.FromSeconds(10));
        if (completed == responseTask)
        {
            using HttpResponseMessage early = await responseTask;
            await Assert.That(early.StatusCode).IsEqualTo(HttpStatusCode.Created);
            throw new InvalidOperationException("missing-setup-enrollment-issuance-activity");
        }

        cancellation.Cancel();
        barrier.Release();
        await Assert.That(async () =>
                await responseTask.WaitAsync(TimeSpan.FromSeconds(10)))
            .Throws<OperationCanceledException>();
        await AssertEntityCount(EnrollmentEntityName, 0);
        await AssertEntityCount(IssuanceClaimEntityName, 0);
        await AssertCapturedDoesNotContain([challenge]);
    }

    private Type RequireController() =>
        typeof(Program).Assembly.GetType(ControllerTypeName)
        ?? throw new InvalidOperationException(
            $"missing-final-d1-owner:{ControllerTypeName}");

    private static async Task AssertEndpoint(
        Type controller,
        string routeName,
        string method,
        string? template,
        string? ratePolicy,
        string? timeoutPolicy,
        long? requestSize)
    {
        MethodInfo action = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate => candidate
                .GetCustomAttributes<HttpMethodAttribute>()
                .Any(attribute => attribute.Name == routeName));
        HttpMethodAttribute http = action.GetCustomAttributes<HttpMethodAttribute>()
            .Single(attribute => attribute.Name == routeName);

        await Assert.That(http.HttpMethods).HasSingleItem();
        await Assert.That(http.HttpMethods.Single()).IsEqualTo(method);
        await Assert.That(http.Template).IsEqualTo(template);
        await Assert.That(
                action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class
                ?? controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(
                action.GetCustomAttribute<AuthorizeAttribute>()
                ?? controller.GetCustomAttribute<AuthorizeAttribute>())
            .IsNotNull();
        await Assert.That(
                action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(ratePolicy);
        await Assert.That(
                action.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName)
            .IsEqualTo(timeoutPolicy);
        await Assert.That(
                (action.GetCustomAttribute<RequestSizeLimitAttribute>()
                    as IRequestSizeLimitMetadata)?.MaxRequestBodySize)
            .IsEqualTo(requestSize);
    }

    private async Task<EnrollmentResponse> CreateEnrollment(
        TenantScenarioSeed.TenantScenarioResult scenario,
        Guid operationKey,
        string challenge,
        HttpStatusCode expected,
        IReadOnlyList<string>? requestedScopes = null)
    {
        using HttpRequestMessage request = CreateEnrollmentRequest(
            scenario.TenantId,
            scenario.UserId,
            operationKey,
            challenge,
            requestedScopes);
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(expected)
            .Because($"final route returned: {body}");
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/hal+json");
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string? capability = response.Headers.TryGetValues(
            CapabilityHeader, out IEnumerable<string>? values)
            ? values.Single()
            : null;
        return new EnrollmentResponse(
            scenario,
            root.GetProperty("enrollmentId").GetGuid(),
            root.GetProperty("generation").GetInt64(),
            root.GetProperty("expiresAt").GetDateTimeOffset(),
            root.GetProperty("issuance").GetString()!,
            capability,
            root.Clone());
    }

    private static HttpRequestMessage CreateEnrollmentRequest(
        Guid tenantId,
        Guid? userId,
        Guid? idempotencyKey,
        string challenge,
        IReadOnlyList<string>? requestedScopes = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, EnrollmentCollectionRoute(tenantId))
        {
            Content = JsonContent.Create(new
            {
                clientChallenge = challenge,
                requestedScopes = requestedScopes ?? RequestedScopes
            })
        };
        if (userId.HasValue)
            Authenticate(request, userId.Value, tenantId);
        if (idempotencyKey.HasValue)
            request.Headers.Add(IdempotencyHeader, idempotencyKey.Value.ToString("D"));
        return request;
    }

    private static HttpRequestMessage EnrollmentRequest(
        TenantScenarioSeed.TenantScenarioResult scenario,
        Guid enrollmentId,
        Guid userId,
        string? capability)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, EnrollmentRoute(scenario.TenantId, enrollmentId));
        Authenticate(request, userId, scenario.TenantId);
        if (capability is not null)
            request.Headers.Add(CapabilityHeader, capability);
        return request;
    }

    private static HttpRequestMessage RevokeRequest(
        EnrollmentResponse enrollment,
        Guid? operationKey = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            EnrollmentRoute(enrollment.Scenario.TenantId, enrollment.EnrollmentId));
        Authenticate(request, enrollment.Scenario.UserId, enrollment.Scenario.TenantId);
        request.Headers.Add(CapabilityHeader, enrollment.Capability);
        request.Headers.Add(
            IdempotencyHeader,
            (operationKey ?? Guid.CreateVersion7()).ToString("D"));
        return request;
    }

    private async Task Revoke(EnrollmentResponse enrollment)
    {
        using HttpRequestMessage request = RevokeRequest(enrollment);
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private async Task<string> Rotate(EnrollmentResponse enrollment)
    {
        using HttpRequestMessage request = RotationRequest(
            enrollment,
            enrollment.Capability!,
            Guid.CreateVersion7());
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return response.Headers.GetValues(CapabilityHeader).Single();
    }

    private static HttpRequestMessage RotationRequest(
        EnrollmentResponse enrollment,
        string capability,
        Guid operationKey)
    {
        var route =
            $"{EnrollmentRoute(enrollment.Scenario.TenantId, enrollment.EnrollmentId)}/capability-rotations";
        var request = new HttpRequestMessage(HttpMethod.Post, route);
        Authenticate(request, enrollment.Scenario.UserId, enrollment.Scenario.TenantId);
        request.Headers.Add(CapabilityHeader, capability);
        request.Headers.Add(IdempotencyHeader, operationKey.ToString("D"));
        return request;
    }

    private static HttpRequestMessage SecretWriteRequest(
        EnrollmentResponse enrollment,
        string bindingKey,
        string secret,
        Guid? operationKey = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put, SecretWriteRoute(enrollment, bindingKey))
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(secret))
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        Authenticate(request, enrollment.Scenario.UserId, enrollment.Scenario.TenantId);
        request.Headers.Add(CapabilityHeader, enrollment.Capability);
        request.Headers.Add(
            IdempotencyHeader,
            (operationKey ?? Guid.CreateVersion7()).ToString("D"));
        return request;
    }

    private static HttpRequestMessage OperationReadRequest(
        EnrollmentResponse enrollment,
        Guid operationId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{EnrollmentRoute(enrollment.Scenario.TenantId, enrollment.EnrollmentId)}/secret-binding-operations/{operationId:D}");
        Authenticate(request, enrollment.Scenario.UserId, enrollment.Scenario.TenantId);
        request.Headers.Add(CapabilityHeader, enrollment.Capability);
        return request;
    }

    private static HttpRequestMessage ReadinessRequest(EnrollmentResponse enrollment)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{EnrollmentRoute(enrollment.Scenario.TenantId, enrollment.EnrollmentId)}/secret-bindings/readiness");
        Authenticate(request, enrollment.Scenario.UserId, enrollment.Scenario.TenantId);
        request.Headers.Add(CapabilityHeader, enrollment.Capability);
        return request;
    }

    private static void Authenticate(HttpRequestMessage request, Guid userId, Guid tenantId) =>
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(userId, tenantId));

    private static void AuthenticateWithoutUserId(HttpRequestMessage request)
    {
        string claims = JsonSerializer.Serialize(
        new[]
        {
            new TestAuthHandler.TestClaimDto(
                ClaimTypes.Name,
                "Setup unresolved principal")
        });
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(claims)));
    }

    private static string EnrollmentCollectionRoute(Guid tenantId) =>
        $"/api/tenants/{tenantId:D}/setup/enrollments";

    private static string EnrollmentRoute(Guid tenantId, Guid enrollmentId) =>
        $"{EnrollmentCollectionRoute(tenantId)}/{enrollmentId:D}";

    private static string SecretWriteRoute(
        EnrollmentResponse enrollment, string bindingKey) =>
        $"{EnrollmentRoute(enrollment.Scenario.TenantId, enrollment.EnrollmentId)}/secret-bindings/{Uri.EscapeDataString(bindingKey)}";

    private async Task<ProblemTuple> SendForProblem(HttpRequestMessage request)
    {
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        return await ReadUnavailableProblem(response);
    }

    private static async Task<ProblemTuple> ReadUnavailableProblem(
        HttpResponseMessage response)
    {
        await AssertUnavailableProblem(response);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;
        return new ProblemTuple(
            response.StatusCode,
            root.GetProperty("type").GetString()!,
            root.GetProperty("title").GetString()!,
            root.GetProperty("code").GetString()!,
            root.GetProperty("detail").GetString()!,
            response.Content.Headers.ContentType?.MediaType,
            response.Headers.CacheControl?.Private == true,
            response.Headers.CacheControl?.NoStore == true);
    }

    private static async Task AssertUnavailableProblem(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Location).IsNull();
        await Assert.That(response.Headers.RetryAfter).IsNull();
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;
        await Assert.That(root.GetProperty("type").GetString())
            .IsEqualTo(UnavailableProblemType);
        await Assert.That(root.GetProperty("title").GetString())
            .IsEqualTo(UnavailableProblemTitle);
        await Assert.That(root.GetProperty("code").GetString())
            .IsEqualTo(UnavailableProblemCode);
        await Assert.That(root.GetProperty("detail").GetString())
            .IsEqualTo(UnavailableProblemDetail);
    }

    private static async Task AssertAuthenticationRequiredProblem(
        HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;
        await Assert.That(root.GetProperty("type").GetString())
            .IsEqualTo("https://tools.ietf.org/html/rfc9110#section-15.5.2");
        await Assert.That(root.GetProperty("code").GetString())
            .IsEqualTo("authentication_required");
    }

    private static async Task AssertConflictProblem(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;
        await Assert.That(root.GetProperty("type").GetString())
            .IsEqualTo("/problems/setup-enrollment-idempotency-conflict");
        await Assert.That(root.GetProperty("title").GetString())
            .IsEqualTo("Setup enrollment request conflicts with an existing operation");
        await Assert.That(root.GetProperty("code").GetString())
            .IsEqualTo("setup_enrollment_idempotency_conflict");
        await Assert.That(root.GetProperty("detail").GetString())
            .IsEqualTo(
                "The idempotency key is already bound to different setup enrollment input.");
    }

    private static async Task AssertHal(EnrollmentResponse enrollment, string canary)
    {
        JsonElement links = enrollment.Body.GetProperty("_links");
        await Assert.That(links.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(
            [
                "self", "revoke", "rotate-capability",
                "secret-binding-readiness"
            ]);
        foreach (JsonProperty relation in links.EnumerateObject())
        {
            string serialized = relation.Value.GetRawText();
            await Assert.That(serialized).DoesNotContain(enrollment.Capability!);
            await Assert.That(serialized).DoesNotContain(canary);
            await Assert.That(serialized).DoesNotContain(
                "provider", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<OperationReceipt> AssertOperationReceipt(
        HttpResponseMessage response,
        EnrollmentResponse enrollment,
        string expectedState,
        string expectedOutcome,
        Guid? expectedOperationId = null)
    {
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        await Assert.That(root.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(
            [
                "operationId", "state", "outcome", "enrollmentGeneration",
                "createdAt", "settledAt", "_links"
            ]);
        Guid operationId = root.GetProperty("operationId").GetGuid();
        if (expectedOperationId.HasValue)
            await Assert.That(operationId).IsEqualTo(expectedOperationId.Value);
        await Assert.That(root.GetProperty("state").GetString()).IsEqualTo(expectedState);
        await Assert.That(root.GetProperty("outcome").GetString()).IsEqualTo(expectedOutcome);
        await Assert.That(root.GetProperty("enrollmentGeneration").GetInt64())
            .IsEqualTo(enrollment.Generation);
        await Assert.That(root.GetProperty("createdAt").GetDateTimeOffset().Offset)
            .IsEqualTo(TimeSpan.Zero);
        await Assert.That(root.GetProperty("settledAt").GetDateTimeOffset().Offset)
            .IsEqualTo(TimeSpan.Zero);
        JsonElement links = root.GetProperty("_links");
        await Assert.That(links.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["self", "secret-binding-operation"]);
        string operationPath =
            $"{EnrollmentRoute(enrollment.Scenario.TenantId, enrollment.EnrollmentId)}/secret-binding-operations/{operationId:D}";
        foreach (JsonProperty relation in links.EnumerateObject())
        {
            string serialized = relation.Value.GetRawText();
            await Assert.That(serialized).Contains(operationPath);
            await Assert.That(serialized).DoesNotContain(enrollment.Capability!);
        }
        await Assert.That(root.TryGetProperty("tenantId", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("actorId", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("bindingKey", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("commitment", out _)).IsFalse();
        await Assert.That(body).DoesNotContain(enrollment.Capability!);
        return new OperationReceipt(operationId);
    }

    private async Task<SetupSecretBindingOperation> LoadOperation(Guid operationKey)
    {
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return await db.Set<SetupSecretBindingOperation>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(operation => operation.OperationKey == operationKey);
    }

    private static async Task AssertOperation(
        SetupSecretBindingOperation operation,
        EnrollmentResponse enrollment,
        Guid operationKey,
        string expectedBindingKey,
        SetupSecretBindingOperationState expectedState,
        SetupSecretBindingOperationOutcome expectedOutcome,
        string expectedCommitment)
    {
        await Assert.That(operation.TenantId).IsEqualTo(enrollment.Scenario.TenantId);
        await Assert.That(operation.ActorId).IsEqualTo(enrollment.Scenario.ActorId);
        await Assert.That(operation.EnrollmentId).IsEqualTo(enrollment.EnrollmentId);
        await Assert.That(operation.EnrollmentGeneration).IsEqualTo(enrollment.Generation);
        await Assert.That(operation.OperationKey).IsEqualTo(operationKey);
        await Assert.That(operation.BindingKey).IsEqualTo(expectedBindingKey);
        await Assert.That(operation.RequestFingerprint.Length).IsEqualTo(64);
        await Assert.That(operation.RequestFingerprint.All(
                character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            .IsTrue();
        await Assert.That(operation.CommitmentKeyVersion)
            .IsEqualTo(TestSetupSecretBindingCommitmentAuthority.KeyVersion);
        await Assert.That(operation.SecretValueCommitment).IsEqualTo(expectedCommitment);
        await Assert.That(operation.State).IsEqualTo(expectedState);
        await Assert.That(operation.Outcome).IsEqualTo(expectedOutcome);
        await Assert.That(operation.SettledAt).IsNotNull();
    }

    private async Task AssertEntityCount(string entityName, int expected) =>
        await Assert.That(await EntityCount(entityName)).IsEqualTo(expected);

    private async Task AssertClaimCount(Guid operationKey, int expected)
    {
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        IEntityType entity = db.Model.FindEntityType(IssuanceClaimEntityName)
            ?? throw new InvalidOperationException(
                $"missing-final-d1-entity:{IssuanceClaimEntityName}");
        int count = SetFor(db, entity.ClrType).Cast<object>().Count(row =>
            row.GetType().GetProperty("OperationKey")?.GetValue(row) is Guid value
            && value == operationKey);
        await Assert.That(count).IsEqualTo(expected);
    }

    private async Task AssertEnrollmentState(
        Guid enrollmentId,
        string expectedState,
        long expectedGeneration,
        bool revoked,
        DateTimeOffset? expectedExpiresAt = null)
    {
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        IEntityType entity = db.Model.FindEntityType(EnrollmentEntityName)
            ?? throw new InvalidOperationException(
                $"missing-final-d1-entity:{EnrollmentEntityName}");
        object row = SetFor(db, entity.ClrType).Cast<object>().Single(value =>
            value.GetType().GetProperty("Id")?.GetValue(value) is Guid id
            && id == enrollmentId);
        await Assert.That(row.GetType().GetProperty("State")?.GetValue(row)?.ToString())
            .IsEqualTo(expectedState);
        await Assert.That(row.GetType().GetProperty("Generation")?.GetValue(row))
            .IsEqualTo(expectedGeneration);
        await Assert.That(row.GetType().GetProperty("RevokedAt")?.GetValue(row) is not null)
            .IsEqualTo(revoked);
        if (expectedExpiresAt.HasValue)
        {
            await Assert.That(
                    row.GetType().GetProperty("ExpiresAt")?.GetValue(row))
                .IsEqualTo(expectedExpiresAt.Value.UtcDateTime);
        }
    }

    private async Task<int> EntityCount(string entityName)
    {
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        IEntityType entity = db.Model.FindEntityType(entityName)
            ?? throw new InvalidOperationException($"missing-final-d1-entity:{entityName}");
        IEnumerable set = SetFor(db, entity.ClrType);
        return set.Cast<object>().Count();
    }

    private async Task AssertStoredRowsDoNotContain(
        IReadOnlyList<string?> canaries, params string[] entityNames)
    {
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        foreach (string entityName in entityNames)
        {
            IEntityType entity = db.Model.FindEntityType(entityName)
                ?? throw new InvalidOperationException($"missing-final-d1-entity:{entityName}");
            foreach (object row in SetFor(db, entity.ClrType))
            {
                foreach (PropertyInfo property in row.GetType().GetProperties(
                             BindingFlags.Public | BindingFlags.Instance))
                {
                    string rendered = property.GetValue(row)?.ToString() ?? string.Empty;
                    foreach (string canary in canaries.OfType<string>())
                        await Assert.That(rendered).DoesNotContain(canary);
                }
            }
        }
    }

    private async Task AssertNoDispatchableOperation(Guid enrollmentId)
    {
        await using AsyncServiceScope scope = fixture.Factory.Services.CreateAsyncScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        IEntityType entity = db.Model.FindEntityType(OperationEntityName)
            ?? throw new InvalidOperationException(
                $"missing-final-d1-entity:{OperationEntityName}");
        object[] rows = SetFor(db, entity.ClrType).Cast<object>()
            .Where(row => row.GetType().GetProperty("EnrollmentId")
                ?.GetValue(row) is Guid value && value == enrollmentId)
            .ToArray();
        await Assert.That(rows).Count().IsEqualTo(1);
        string state = rows[0].GetType().GetProperty("State")
            ?.GetValue(rows[0])?.ToString() ?? string.Empty;
        await Assert.That(state).IsNotEqualTo("Accepted");
    }

    private static IEnumerable SetFor(DbContext db, Type entityType)
    {
        MethodInfo setMethod = typeof(DbContext).GetMethods()
            .Single(method =>
                method.Name == nameof(DbContext.Set)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 0);
        return (IEnumerable)(setMethod.MakeGenericMethod(entityType)
            .Invoke(db, null)
            ?? throw new InvalidOperationException("missing-final-d1-dbset"));
    }

    private async Task AssertCapturedDoesNotContain(IReadOnlyList<string?> canaries)
    {
        foreach (string captured in fixture.Capture.Snapshot())
        {
            foreach (string canary in canaries.OfType<string>())
                await Assert.That(captured).DoesNotContain(canary);
        }
    }

    private async Task AssertSetupTelemetryDoesNotContain(
        IReadOnlyList<string?> canaries)
    {
        foreach (string captured in fixture.Capture.SetupSnapshot())
        {
            foreach (string canary in canaries.OfType<string>())
                await Assert.That(captured).DoesNotContain(canary);
        }
    }

    private static string NewToken() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string NewShortToken() =>
        Base64Url(RandomNumberGenerator.GetBytes(16));

    private static string NewLongToken() =>
        Base64Url(RandomNumberGenerator.GetBytes(64));

    private static string Digest(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed record EnrollmentResponse(
        TenantScenarioSeed.TenantScenarioResult Scenario,
        Guid EnrollmentId,
        long Generation,
        DateTimeOffset ExpiresAt,
        string Issuance,
        string? Capability,
        JsonElement Body);

    private sealed record ProblemTuple(
        HttpStatusCode Status,
        string Type,
        string Title,
        string Code,
        string Detail,
        string? MediaType,
        bool Private,
        bool NoStore);

    private sealed record OperationReceipt(Guid OperationId);
}

public sealed class SetupLiveAuthorityRedFixture : IAsyncInitializer, IAsyncDisposable
{
    public StubAuthorizationProvider Authorization { get; } = new() { AllowAll = true };
    public ManualSetupTimeProvider Clock { get; } = new();
    public SetupLiveCapture Capture { get; } = new();
    public RecordingSetupSecretBindingWriter Writer { get; } = new();
    public TestSetupSecretBindingCommitmentAuthority CommitmentAuthority { get; } = new();
    public RecordingSetupSecretBindingCommitBarrier CommitBarrier { get; } = new();
    public RecordingSetupSecretBindingOperationCoordinator Coordinator { get; } = new();
    public UnreadBodyProbe BodyProbe { get; } = new();
    public SetupLiveAuthorityWebApplicationFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public TenantScenarioSeed.TenantScenarioResult Primary { get; private set; } = null!;
    public TenantScenarioSeed.TenantScenarioResult Secondary { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Factory = new SetupLiveAuthorityWebApplicationFactory(
            Clock,
            Capture,
            Writer,
            CommitmentAuthority,
            CommitBarrier,
            Coordinator,
            BodyProbe)
        {
            AuthorizationProviderOverride = Authorization
        };
        Factory.AdditionalConfiguration["SecretProvider:Provider"] = "Environment";
        Client = Factory.CreateClient();
        Capture.Start();
        await ResetAsync();
    }

    public async Task ResetAsync()
    {
        Authorization.AllowAll = true;
        Authorization.CheckPredicate = null;
        Writer.Reset();
        CommitmentAuthority.Reset();
        CommitBarrier.Reset();
        Coordinator.Reset();
        BodyProbe.Reset();
        Clock.Reset();
        Capture.Reset();
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        IHostEnvironment environment =
            scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        await DatabaseSeeder.SeedAsync(db, environment);
        ILookupDataCache? cache =
            scope.ServiceProvider.GetService<ILookupDataCache>();
        if (cache is not null)
            await cache.RefreshAsync();
        Primary = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        Secondary = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
    }

    public async Task<Guid> AddBinding(SecretBinding binding)
    {
        binding.Id = Guid.CreateVersion7();
        binding.CreatedAt = Clock.GetUtcNow().UtcDateTime;
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        ExploreDbContext db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        db.SecretBindings.Add(binding);
        await db.SaveChangesAsync();
        return binding.Id;
    }

    public Task<Guid> AddSetupBinding(string bindingKey) => AddBinding(new SecretBinding
    {
        SettingKey = bindingKey,
        Scope = SecretScope.Instance,
        SourceType = SecretSourceType.EnvironmentVariable,
        EnvironmentVariableName = $"ISLAMU_{bindingKey.Replace('.', '_').ToUpperInvariant()}"
    });

    public async ValueTask DisposeAsync()
    {
        Capture.Dispose();
        CommitmentAuthority.Dispose();
        Coordinator.Dispose();
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}

public sealed class SetupLiveAuthorityWebApplicationFactory(
    ManualSetupTimeProvider clock,
    SetupLiveCapture capture,
    RecordingSetupSecretBindingWriter writer,
    TestSetupSecretBindingCommitmentAuthority commitmentAuthority,
    RecordingSetupSecretBindingCommitBarrier commitBarrier,
    RecordingSetupSecretBindingOperationCoordinator coordinator,
    UnreadBodyProbe bodyProbe)
    : AuthenticatedWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<ISetupSecretBindingWriter>();
            services.RemoveAll<ISetupSecretBindingReadinessReader>();
            services.RemoveAll<ISetupSecretBindingCommitmentAuthority>();
            services.RemoveAll<ISetupSecretBindingCommitBarrier>();
            services.RemoveAll<ISetupSecretBindingOperationCoordinator>();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<ISetupSecretBindingWriter>(writer);
            services.AddSingleton<ISetupSecretBindingReadinessReader>(writer);
            services.AddSingleton<ISetupSecretBindingCommitmentAuthority>(commitmentAuthority);
            services.AddSingleton<ISetupSecretBindingCommitBarrier>(commitBarrier);
            services.AddSingleton<ISetupSecretBindingOperationCoordinator>(coordinator);
            services.AddSingleton<ILoggerProvider>(capture.LoggerProvider);
            services.AddSingleton<IStartupFilter>(
                new UnreadBodyStartupFilter(bodyProbe));
        });
    }
}

public sealed class UnreadBodyProbe
{
    public const string HeaderName = "X-Setup-Unread-Body-Probe";
    private int _readCount;
    public int ReadCount => Volatile.Read(ref _readCount);
    public void RecordRead() => Interlocked.Increment(ref _readCount);
    public void Reset() => Interlocked.Exchange(ref _readCount, 0);
}

public sealed class UnreadBodyStartupFilter(UnreadBodyProbe probe) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.ContainsKey(UnreadBodyProbe.HeaderName))
                    context.Request.Body = new ThrowOnReadStream(context.Request.Body, probe);
                await nextMiddleware();
            });
            next(app);
        };

    private sealed class ThrowOnReadStream(Stream inner, UnreadBodyProbe probe) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Fail();
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) => Task.FromResult(Fail());
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fail());
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private int Fail()
        {
            probe.RecordRead();
            throw new InvalidOperationException("setup-secret-body-was-read");
        }
    }
}

public sealed class RecordingSetupSecretBindingWriter :
    ISetupSecretBindingWriter,
    ISetupSecretBindingReadinessReader
{
    private ProviderWriteBarrier? _barrier;
    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);
    public SetupSecretBindingWriteOutcome Outcome { get; set; } =
        SetupSecretBindingWriteOutcome.Ready;
    public SetupSecretBindingWriteOutcome ReadinessOutcome { get; set; } =
        SetupSecretBindingWriteOutcome.Ready;
    public SetupSecretBindingWriteRequest? LastRequest { get; private set; }
    public string? LastSecretDigest { get; private set; }
    public ReadOnlyMemory<byte> LastBorrowedValue { get; private set; }

    public Task<SetupSecretBindingWriteOutcome> GetReadinessAsync(
        Guid bindingId,
        string bindingKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadinessOutcome);
    }

    public Task<SetupSecretBindingWriteOutcome> WriteAsync(
        SetupSecretBindingWriteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _callCount);
        LastRequest = request;
        LastSecretDigest = Digest(request.SecretValue.Span);
        LastBorrowedValue = request.SecretValue;
        Interlocked.Exchange(ref _barrier, null)?.SignalAndWait();
        return Task.FromResult(Outcome);
    }

    public ProviderWriteBarrier ArmBarrier()
    {
        var barrier = new ProviderWriteBarrier();
        if (Interlocked.CompareExchange(ref _barrier, barrier, null) is not null)
            throw new InvalidOperationException("setup-provider-write-barrier-already-armed");
        return barrier;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _barrier, null)?.Release();
        Interlocked.Exchange(ref _callCount, 0);
        Outcome = SetupSecretBindingWriteOutcome.Ready;
        ReadinessOutcome = SetupSecretBindingWriteOutcome.Ready;
        LastRequest = null;
        LastSecretDigest = null;
        LastBorrowedValue = default;
    }

    private static string Digest(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

public sealed class ProviderWriteBarrier : IDisposable
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _release = new(initialState: false);

    public Task Started => _started.Task;

    public void Release() => _release.Set();

    public void SignalAndWait()
    {
        _started.TrySetResult();
        if (!_release.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("setup-provider-write-barrier-release-timeout");
    }

    public void Dispose()
    {
        Release();
        _release.Dispose();
    }
}

public sealed class TestSetupSecretBindingCommitmentAuthority :
    ISetupSecretBindingCommitmentAuthority,
    IDisposable
{
    public const int KeyVersion = 37;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);
    public SetupSecretBindingCommitmentRequest? LastRequest { get; private set; }
    public ReadOnlyMemory<byte> LastBorrowedValue { get; private set; }
    public string? LastCommitment { get; private set; }
    public string? LastSecretDigest { get; private set; }
    public bool FailClosed { get; set; }

    public Task<SetupSecretBindingCommitment> CommitAsync(
        SetupSecretBindingCommitmentRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _callCount);
        LastRequest = request;
        LastBorrowedValue = request.SecretValue;
        LastSecretDigest = Convert.ToHexString(
            SHA256.HashData(request.SecretValue.Span)).ToLowerInvariant();
        if (FailClosed)
            throw new InvalidOperationException(
                "setup-secret-binding-commitment-unavailable");
        LastCommitment = Convert.ToHexString(
            HMACSHA256.HashData(_key, request.SecretValue.Span)).ToLowerInvariant();
        return Task.FromResult(new SetupSecretBindingCommitment(
            KeyVersion,
            LastCommitment));
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
        LastRequest = null;
        LastBorrowedValue = default;
        LastCommitment = null;
        LastSecretDigest = null;
        FailClosed = false;
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}

public sealed class RecordingSetupSecretBindingCommitBarrier :
    ISetupSecretBindingCommitBarrier
{
    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);

    public Task WaitBeforeProviderDispatchAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void Reset() => Interlocked.Exchange(ref _callCount, 0);
}

public sealed class RecordingSetupSecretBindingOperationCoordinator :
    ISetupSecretBindingOperationCoordinator,
    IDisposable
{
    private CoordinatorAcquireBarrier? _beforeAcquireBarrier;
    private int _acquireCount;
    private int _disposeCount;
    private readonly ConcurrentQueue<SetupSecretBindingCoordinationRequest> _requests = new();
    // ponytail: fixture-wide lock; replace with keyed locks only if this test fixture needs parallel enrollments.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int AcquireCount => Volatile.Read(ref _acquireCount);
    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public async Task<IAsyncDisposable> AcquireAsync(
        SetupSecretBindingCoordinationRequest request,
        CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _beforeAcquireBarrier, null)?.SignalAndWait();
        await _gate.WaitAsync(cancellationToken);
        Interlocked.Increment(ref _acquireCount);
        _requests.Enqueue(request);
        return new Lease(this);
    }

    public SetupSecretBindingCoordinationRequest[] Snapshot() => _requests.ToArray();

    public CoordinatorAcquireBarrier ArmBeforeAcquireBarrier()
    {
        var barrier = new CoordinatorAcquireBarrier();
        if (Interlocked.CompareExchange(
                ref _beforeAcquireBarrier,
                barrier,
                null) is not null)
        {
            throw new InvalidOperationException(
                "setup-coordinator-acquire-barrier-already-armed");
        }
        return barrier;
    }

    public void Dispose() => _gate.Dispose();

    public void Reset()
    {
        Interlocked.Exchange(ref _beforeAcquireBarrier, null)?.Release();
        Interlocked.Exchange(ref _acquireCount, 0);
        Interlocked.Exchange(ref _disposeCount, 0);
        while (_requests.TryDequeue(out _))
        {
        }
    }

    private sealed class Lease(
        RecordingSetupSecretBindingOperationCoordinator owner) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Increment(ref owner._disposeCount);
                owner._gate.Release();
            }
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class CoordinatorAcquireBarrier : IDisposable
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _release = new(initialState: false);

    public Task Started => _started.Task;
    public void Release() => _release.Set();

    public void SignalAndWait()
    {
        _started.TrySetResult();
        if (!_release.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("setup-coordinator-acquire-release-timeout");
    }

    public void Dispose()
    {
        Release();
        _release.Dispose();
    }
}

public sealed class ManualSetupTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _initial = DateTimeOffset.UtcNow;
    private DateTimeOffset _current;

    public ManualSetupTimeProvider() => _current = _initial;

    public override DateTimeOffset GetUtcNow() => _current;

    public void SetUtcNow(DateTimeOffset value) => _current = value;

    public void Reset() => _current = _initial;
}

public sealed class SetupLiveCapture : IDisposable
{
    public const string SourceName = "ISLAMU.Event.Setup.Live";
    private readonly ConcurrentQueue<string> _values = new();
    private readonly ConcurrentQueue<string> _setupValues = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;
    private TelemetryBarrier? _barrier;

    public SetupLiveCapture()
    {
        LoggerProvider = new SetupLiveCapturingLoggerProvider(_values, LogObserved);
        _activityListener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                _values.Enqueue(activity.Source.Name);
                _values.Enqueue(activity.DisplayName);
                foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
                    _values.Enqueue($"activity:{tag.Key}={tag.Value}");
                foreach (ActivityEvent activityEvent in activity.Events)
                    _values.Enqueue(activityEvent.Name);
                if (string.Equals(activity.Source.Name, SourceName, StringComparison.Ordinal))
                {
                    _setupValues.Enqueue(activity.Source.Name);
                    _setupValues.Enqueue(activity.DisplayName);
                    foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
                        _setupValues.Enqueue($"activity:{tag.Key}={tag.Value}");
                    foreach (ActivityEvent activityEvent in activity.Events)
                        _setupValues.Enqueue(activityEvent.Name);
                }
            }
        };
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                listener.EnableMeasurementEvents(instrument);
                _values.Enqueue(instrument.Meter.Name);
                _values.Enqueue(instrument.Name);
            }
        };
        _meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) =>
                RecordMeasurement(instrument, measurement, tags));
        _meterListener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, _) =>
                RecordMeasurement(instrument, measurement, tags));
        _meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) =>
                RecordMeasurement(instrument, measurement, tags));
    }

    public ILoggerProvider LoggerProvider { get; }

    public void Start()
    {
        ActivitySource.AddActivityListener(_activityListener);
        _meterListener.Start();
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _barrier, null)?.Release();
        while (_values.TryDequeue(out _))
        {
        }
        while (_setupValues.TryDequeue(out _))
        {
        }
    }

    public string[] Snapshot() => _values.ToArray();
    public string[] SetupSnapshot() => _setupValues.ToArray();

    public TelemetryBarrier ArmStructuredLogBarrier(
        string operation, string milestone)
    {
        var barrier = new TelemetryBarrier(operation, milestone);
        if (Interlocked.CompareExchange(ref _barrier, barrier, null) is not null)
            throw new InvalidOperationException("setup-live-telemetry-barrier-already-armed");
        barrier.Disposed += () => Interlocked.CompareExchange(ref _barrier, null, barrier);
        return barrier;
    }

    public void Dispose()
    {
        Reset();
        _activityListener.Dispose();
        _meterListener.Dispose();
        LoggerProvider.Dispose();
    }

    private void LogObserved(
        EventId eventId,
        IReadOnlyDictionary<string, object?> properties)
    {
        if (eventId.Id == 19_620
            && string.Equals(eventId.Name, "SetupLiveMilestone", StringComparison.Ordinal))
        {
            _setupValues.Enqueue(eventId.Name);
            foreach (KeyValuePair<string, object?> property in properties)
            {
                _setupValues.Enqueue($"{property.Key}={property.Value}");
                if (property.Key is not "SetupOperation"
                    and not "SetupMilestone"
                    and not "{OriginalFormat}"
                    and not "EventId"
                    and not "SourceContext"
                    and not "MachineName"
                    and not "ProcessId"
                    and not "ThreadId")
                {
                    _setupValues.Enqueue($"forbidden-field:{property.Key}");
                }
            }
        }
        TelemetryBarrier? armed = _barrier;
        if (armed is null
            || eventId.Id != 19_620
            || !string.Equals(
                eventId.Name, "SetupLiveMilestone", StringComparison.Ordinal)
            || !properties.TryGetValue("SetupOperation", out object? operation)
            || !properties.TryGetValue("SetupMilestone", out object? milestone)
            || !string.Equals(operation?.ToString(), armed.Operation, StringComparison.Ordinal)
            || !string.Equals(milestone?.ToString(), armed.Milestone, StringComparison.Ordinal))
            return;
        TelemetryBarrier? barrier = Interlocked.CompareExchange(
            ref _barrier, null, armed) == armed ? armed : null;
        if (barrier is null)
            return;
        barrier.SignalAndWait();
    }

    private void RecordMeasurement<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        _values.Enqueue($"{instrument.Meter.Name}:{instrument.Name}:{measurement}");
        foreach (KeyValuePair<string, object?> tag in tags)
            _values.Enqueue($"metric:{tag.Key}={tag.Value}");
        if (string.Equals(instrument.Meter.Name, SourceName, StringComparison.Ordinal))
        {
            _setupValues.Enqueue($"{instrument.Meter.Name}:{instrument.Name}:{measurement}");
            foreach (KeyValuePair<string, object?> tag in tags)
                _setupValues.Enqueue($"metric:{tag.Key}={tag.Value}");
        }
    }
}

public sealed class TelemetryBarrier(
    string operation,
    string milestone) : IDisposable
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _release = new(initialState: false);

    public Task Started => _started.Task;
    public string Operation { get; } = operation;
    public string Milestone { get; } = milestone;
    public event Action? Disposed;

    public void Release() => _release.Set();

    public void SignalAndWait()
    {
        _started.TrySetResult();
        if (!_release.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("setup-live-telemetry-barrier-release-timeout");
    }

    public void Dispose()
    {
        Release();
        _release.Dispose();
        Disposed?.Invoke();
    }
}

public sealed class SetupLiveCapturingLoggerProvider(
    ConcurrentQueue<string> values,
    Action<EventId, IReadOnlyDictionary<string, object?>> observed) :
    ILoggerProvider,
    ISupportExternalScope
{
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) =>
        new CapturingLogger(categoryName, values, observed, () => _scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(
        string category,
        ConcurrentQueue<string> values,
        Action<EventId, IReadOnlyDictionary<string, object?>> observed,
        Func<IExternalScopeProvider> scopeProvider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            scopeProvider().Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            values.Enqueue(category);
            values.Enqueue(eventId.Name
                ?? eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            values.Enqueue(formatter(state, exception));
            var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (KeyValuePair<string, object?> property in properties)
                    snapshot[property.Key] = property.Value;
            }
            int scopeIndex = 0;
            scopeProvider().ForEachScope<object?>(
                (scope, _) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        foreach (KeyValuePair<string, object?> pair in pairs)
                            snapshot[$"Scope[{scopeIndex}].{pair.Key}"] = pair.Value;
                    }
                    else
                    {
                        snapshot[$"Scope[{scopeIndex}]"] = scope?.ToString();
                    }
                    scopeIndex++;
                },
                state: null);
            foreach (KeyValuePair<string, object?> property in snapshot)
                values.Enqueue($"{property.Key}={property.Value}");
            observed(eventId, snapshot);
            if (exception is not null)
                values.Enqueue(exception.ToString());
        }
    }
}
