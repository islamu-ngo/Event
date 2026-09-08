using Explore.Application.Constants;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Authentication;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.DTOs.Admissions;
using Explore.Application.DTOs.Event;
using Explore.Application.Exceptions;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[Category("Phase21AdmissionApiRed")]
[NotInParallel("Phase21AdmissionApiRed")]
[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class AdmissionCheckInHttpHalRedTests(ContractApiFixture fixture)
{
    private const string OpenApiEndpoint = "/openapi/islamu-event.json";
    private const string ScannerCapabilityHeader = "X-Admission-Scanner-Capability";
    private const string StaffRatePolicy = "admission_check_in";
    private const string CapabilityManagementRatePolicy = "admission_scanner_capability";
    private const string ScannerRatePolicy = "admission_scanner_check_in";

    private static readonly RouteSpec[] Routes =
    [
        new("/api/events/{eventId}/admission/scanner-capabilities", HttpMethods.Get,
            "ListAdmissionScannerCapabilities", Audience.Staff, RateLimitingExtensions.AuthenticatedPolicy,
            "HalCollectionResourceOfAdmissionScannerCapabilityDto"),
        new("/api/events/{eventId}/admission/scanner-capabilities", HttpMethods.Post,
            "IssueAdmissionScannerCapability", Audience.Staff, CapabilityManagementRatePolicy,
            "HalResourceOfAdmissionScannerCapabilityIssuedDto"),
        new("/api/events/{eventId}/admission/scanner-capabilities/{scannerCapabilityId}", HttpMethods.Delete,
            "RevokeAdmissionScannerCapability", Audience.Staff, CapabilityManagementRatePolicy,
            "HalResourceOfAdmissionScannerCapabilityDto"),

        new("/api/events/{eventId}/admission/check-ins", HttpMethods.Post,
            "CheckInAdmission", Audience.Staff, StaffRatePolicy,
            "HalResourceOfAdmissionCheckInResultDto"),
        new("/api/events/{eventId}/admission/check-ins/{checkInId}", HttpMethods.Get,
            "GetAdmissionCheckIn", Audience.Staff, RateLimitingExtensions.AuthenticatedPolicy,
            "HalResourceOfAdmissionCheckInResultDto"),
        new("/api/events/{eventId}/admission/check-ins/batch", HttpMethods.Post,
            "BatchCheckInAdmissions", Audience.Staff, StaffRatePolicy,
            "HalResourceOfAdmissionCheckInBatchResultDto"),
        new("/api/events/{eventId}/admission/check-ins/{checkInId}/undo", HttpMethods.Post,
            "UndoAdmissionCheckIn", Audience.Staff, StaffRatePolicy,
            "HalResourceOfAdmissionCheckInResultDto"),
        new("/api/events/{eventId}/admission/check-ins/summary", HttpMethods.Get,
            "GetAdmissionCheckInSummary", Audience.Staff, RateLimitingExtensions.AuthenticatedPolicy,
            "HalResourceOfAdmissionCheckInSummaryDto"),
        new("/api/events/{eventId}/admission/check-ins/audit", HttpMethods.Get,
            "GetAdmissionCheckInAudit", Audience.Staff, RateLimitingExtensions.AuthenticatedPolicy,
            "HalResourceOfAdmissionCheckInAuditPageDto"),
        new("/api/events/{eventId}/admission/check-ins/health", HttpMethods.Get,
            "GetAdmissionCheckInHealth", Audience.Staff, RateLimitingExtensions.AuthenticatedPolicy,
            "HalResourceOfAdmissionCheckInHealthDto"),
        new("/api/events/{eventId}/admission/check-ins/operations/stop", HttpMethods.Post,
            "StopAdmissionCheckIn", Audience.Staff, StaffRatePolicy,
            "HalResourceOfAdmissionCheckInOperationalResultDto"),
        new("/api/events/{eventId}/admission/check-ins/operations/restore", HttpMethods.Post,
            "RestoreAdmissionCheckIn", Audience.Staff, StaffRatePolicy,
            "HalResourceOfAdmissionCheckInOperationalResultDto"),
        new("/api/events/{eventId}/admission/check-ins/operations/reconcile", HttpMethods.Post,
            "ReconcileAdmissionCheckIn", Audience.Staff, StaffRatePolicy,
            "HalResourceOfAdmissionCheckInOperationalResultDto"),

        new("/api/admission/scanner/check-ins", HttpMethods.Post,
            "ScannerCheckInAdmission", Audience.ScannerCapability, ScannerRatePolicy,
            "HalResourceOfAdmissionCheckInResultDto"),
        new("/api/admission/scanner/check-ins/batch", HttpMethods.Post,
            "ScannerBatchCheckInAdmissions", Audience.ScannerCapability, ScannerRatePolicy,
            "HalResourceOfAdmissionCheckInBatchResultDto"),
        new("/api/admission/scanner/check-ins/{checkInId}/undo", HttpMethods.Post,
            "ScannerUndoAdmissionCheckIn", Audience.ScannerCapability, ScannerRatePolicy,
            "HalResourceOfAdmissionCheckInResultDto")
    ];

    [Test]
    public async Task RoutesSeparateAuthenticatedStaffFromNarrowScannerCapabilityAuthority()
    {
        IReadOnlyList<ActionContract> actions = ApiActions().ToArray();
        var violations = new List<string>();

        foreach (RouteSpec route in Routes)
        {
            ActionContract? action = FindAction(actions, route);
            if (action is null)
            {
                violations.Add($"missing {route.Method} {route.Path}");
                continue;
            }

            if (!string.Equals(action.RouteName, route.OperationId, StringComparison.Ordinal))
                violations.Add($"{route.Path} route name is {action.RouteName ?? "<null>"}");
            if (EffectiveAttribute<PrivateNoStoreAttribute>(action) is null)
                violations.Add($"{route.Path} is not private/no-store");
            if (EffectiveAttribute<EnableRateLimitingAttribute>(action)?.PolicyName != route.RatePolicy)
                violations.Add($"{route.Path} does not use {route.RatePolicy}");

            if (route.Audience == Audience.Staff)
                ValidateStaffRoute(action, route, violations);
            else
                ValidateScannerRoute(action, route, violations);

            ValidateProblemMetadata(action, route, violations);
            ValidateHalSuccessType(action, route, violations);
        }

        await Assert.That(violations).IsEmpty().Because(string.Join("; ", violations));
    }

    [Test]
    public async Task StaffSelectsTargetInBodyWhileScannerDerivesScopeFromAuthenticatedCapability()
    {
        IReadOnlyList<ActionContract> actions = await RequireAllRoutes();
        Type staffRequest = RequestDto(FindAction(actions, Route("CheckInAdmission"))!);
        Type scannerRequest = RequestDto(FindAction(actions, Route("ScannerCheckInAdmission"))!);

        await Assert.That(staffRequest.GetProperty("TargetId")).IsNotNull()
            .Because("the canonical staff route selects its admission target in the request body");
        await Assert.That(scannerRequest.GetProperty("TargetId")).IsNull()
            .Because("the AdmissionScanner principal supplies event and target scope");
        await Assert.That(scannerRequest.GetProperty("EventId")).IsNull();
        await Assert.That(scannerRequest).IsNotEqualTo(staffRequest)
            .Because("staff bearer and scanner-capability requests must not share an authority-bearing body contract");
    }

    [Test]
    public async Task ScannerCapabilityPlaintextExistsOnExactlyTheIssueResponseAndLaterReadsAreMasked()
    {
        IReadOnlyList<ActionContract> actions = await RequireAllRoutes();
        ActionContract issue = FindAction(actions, Route("IssueAdmissionScannerCapability"))!;
        ActionContract list = FindAction(actions, Route("ListAdmissionScannerCapabilities"))!;
        ActionContract revoke = FindAction(actions, Route("RevokeAdmissionScannerCapability"))!;

        Type issuedDto = SuccessDto(issue);
        Type listedDto = SuccessDto(list);
        Type revokedDto = SuccessDto(revoke);
        PropertyInfo[] issueSecrets = SecretProperties(issuedDto);

        await Assert.That(issue.Method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>())
            .IsNotNull()
            .Because("generic HTTP idempotency must never persist or replay one-time scanner plaintext");
        await Assert.That(issueSecrets.Select(property => property.Name).ToArray())
            .IsEquivalentTo(["Capability"])
            .Because("the issuance document is the only response allowed to disclose scanner plaintext");
        await Assert.That(SecretProperties(listedDto)).IsEmpty();
        await Assert.That(SecretProperties(revokedDto)).IsEmpty();

        foreach (Type capabilityType in new[] { issuedDto, listedDto, revokedDto })
        {
            string[] names = capabilityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .ToArray();
            await Assert.That(names).Contains("TargetId");
            await Assert.That(names).DoesNotContain("TargetIds");
        }

        foreach (Type maskedType in new[] { listedDto, revokedDto })
        {
            string[] names = maskedType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .ToArray();
            await Assert.That(names).Contains("MaskedCapability");
            await Assert.That(names).Contains("DeviceLabel");
            await Assert.That(names).Contains("ExpiresAt");
            await Assert.That(names).Contains("RevokedAt");
        }
    }

    [Test]
    public async Task CheckInUndoSingleAndBatchContractsExposeOnlyBoundedDoorDataAndBatchStopsAtOneHundred()
    {
        IReadOnlyList<ActionContract> actions = await RequireAllRoutes();
        var operationIds = new[]
        {
            "CheckInAdmission", "GetAdmissionCheckIn", "BatchCheckInAdmissions", "UndoAdmissionCheckIn",
            "ScannerCheckInAdmission", "ScannerBatchCheckInAdmissions", "ScannerUndoAdmissionCheckIn"
        };
        string[] forbiddenDoorMembers =
        [
            "Email", "HolderDisplayName", "Participant", "RegistrationOrder", "Answers", "Credential",
            "Capability", "LookupDigest", "TenantId", "TicketId"
        ];

        foreach (string operationId in operationIds)
        {
            Type dto = SuccessDto(FindAction(actions, Route(operationId))!);
            IEnumerable<Type> doorTypes = operationId.Contains("Batch", StringComparison.Ordinal)
                ? PublicTypesReferencedBy(dto).Append(dto)
                : [dto];
            string[] leaked = doorTypes.SelectMany(type => type.GetProperties())
                .Select(property => property.Name)
                .Where(name => forbiddenDoorMembers.Any(forbidden =>
                    name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            await Assert.That(leaked).IsEmpty()
                .Because($"{operationId} must return status/target/timestamp door data, not attendee or bearer data");
        }

        using JsonDocument document = await GetOpenApiDocument();
        JsonElement batchSchema = Schema(document, "AdmissionCheckInBatchRequestDto");
        JsonElement items = batchSchema.GetProperty("properties").GetProperty("items");
        await Assert.That(items.GetProperty("maxItems").GetInt32()).IsEqualTo(100);
        await Assert.That(items.GetProperty("minItems").GetInt32()).IsEqualTo(1);

        Type batchRequest = RequestDto(FindAction(actions, Route("BatchCheckInAdmissions"))!);
        PropertyInfo batchItems = batchRequest.GetProperties().Single(property =>
            property.Name.Equals("Items", StringComparison.Ordinal));
        MaxLengthAttribute? maxLength = batchItems.GetCustomAttribute<MaxLengthAttribute>();
        await Assert.That(maxLength?.Length).IsEqualTo(100);
    }

    [Test]
    public async Task ReportingContractsRequireExactTargetAndExposeOnlyOpaqueExportSafeFacts()
    {
        IReadOnlyList<ActionContract> actions = await RequireAllRoutes();
        ActionContract summary = FindAction(actions, Route("GetAdmissionCheckInSummary"))!;
        ActionContract audit = FindAction(actions, Route("GetAdmissionCheckInAudit"))!;

        ParameterInfo target = summary.Method.GetParameters().Single(parameter => parameter.Name == "targetId");
        await Assert.That(target.ParameterType).IsEqualTo(typeof(Guid));
        await Assert.That(target.GetCustomAttribute<FromQueryAttribute>()).IsNotNull();

        ParameterInfo cursor = audit.Method.GetParameters().Single(parameter => parameter.Name == "cursor");
        ParameterInfo pageSize = audit.Method.GetParameters().Single(parameter => parameter.Name == "pageSize");
        await Assert.That(cursor.ParameterType).IsEqualTo(typeof(string));
        await Assert.That(pageSize.GetCustomAttribute<RangeAttribute>()?.Maximum).IsEqualTo(100);

        Type summaryDto = SuccessDto(summary);
        string[] summaryNames = summaryDto.GetProperties().Select(property => property.Name).ToArray();
        await Assert.That(summaryNames).IsEquivalentTo([
            "TargetType", "CheckedInCount", "UndoneCount", "ActiveCount", "InactiveCount",
            "LastActivityTimeBucketUtc"
        ]);
        foreach (string countName in new[] { "CheckedInCount", "UndoneCount", "ActiveCount", "InactiveCount" })
            await Assert.That(summaryDto.GetProperty(countName)?.PropertyType).IsEqualTo(typeof(long));
        await Assert.That(summaryNames).DoesNotContain("RejectedCount");

        Type auditPageDto = SuccessDto(audit);
        Type auditItemDto = PublicTypesReferencedBy(auditPageDto).Single(type => type.Name.Contains("AuditItem", StringComparison.Ordinal));
        await Assert.That(auditPageDto.GetProperty("NextCursor")?.PropertyType).IsEqualTo(typeof(string));
        await Assert.That(auditItemDto.GetProperty("Cursor")?.PropertyType).IsEqualTo(typeof(string));
        await Assert.That(auditItemDto.GetProperties().Select(property => property.Name).ToArray()).IsEquivalentTo([
            "Cursor", "Action", "Outcome", "TargetType", "OccurredAtTimeBucketUtc"
        ]);
        await Assert.That(SecretProperties(auditItemDto)).IsEmpty();
    }

    [Test]
    public async Task ReportingRequiresAuthenticationAndMissingTargetIsBounded()
    {
        await using var factory = new AdmissionReportingFactory();
        using HttpClient anonymous = factory.CreateClient();
        Guid eventId = Guid.CreateVersion7();

        using HttpResponseMessage unauthorized = await anonymous.GetAsync(
            $"/api/events/{eventId:D}/admission/check-ins/audit?pageSize=100");
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        using HttpClient authenticated = factory.CreateClient();
        authenticated.DefaultRequestHeaders.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        using HttpResponseMessage missingTarget = await authenticated.GetAsync(
            $"/api/events/{eventId:D}/admission/check-ins/summary");
        await Assert.That(missingTarget.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using HttpResponseMessage absent = await authenticated.GetAsync(
            $"/api/events/{eventId:D}/admission/check-ins/summary?targetId={Guid.CreateVersion7():D}");
        string body = await absent.Content.ReadAsStringAsync();
        await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(body).DoesNotContain("tenant", StringComparison.OrdinalIgnoreCase);
        await Assert.That(body).DoesNotContain("targetId", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task ExactRequestDispatcherRejectsDecoyTypesAndWrongResponseContracts()
    {
        IReadOnlyList<ActionContract> actions = await RequireAllRoutes();
        ActionContract[] writes = actions.Where(action => Routes.Any(route =>
                route.OperationId == action.RouteName && route.Method == HttpMethods.Post))
            .ToArray();
        var dispatcher = new ExactRequestDispatcher(writes);

        foreach (ActionContract action in writes)
        {
            Type requestType = RequestDto(action);
            object request = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(requestType);
            Type responseType = SuccessDto(action);

            await Assert.That(dispatcher.Dispatch(request, responseType)).Contains(action.RouteName!);
            await Assert.That(() => dispatcher.Dispatch(request, typeof(DecoyResponse)))
                .Throws<InvalidOperationException>();
        }

        await Assert.That(() => dispatcher.Dispatch(new DecoyAdmissionCheckInRequest(), typeof(DecoyResponse)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WrongScannerScopeIsDocumentedAsOneGenericRfc7807Absence()
    {
        IReadOnlyList<ActionContract> actions = await RequireAllRoutes();

        foreach (RouteSpec route in Routes.Where(candidate => candidate.Audience == Audience.ScannerCapability))
        {
            ActionContract action = FindAction(actions, route)!;
            ProducesResponseTypeAttribute[] responses = ProducedResponses(action);
            ProducesResponseTypeAttribute notFound = responses.Single(response =>
                response.StatusCode == StatusCodes.Status404NotFound);

            await Assert.That(notFound.Type).IsEqualTo(typeof(ProblemDetails));
            await Assert.That(responses.Any(response =>
                response.StatusCode == StatusCodes.Status403Forbidden)).IsFalse()
                .Because("expired, revoked, stolen, wrong-tenant/event/target/action capabilities share generic absence");
        }
    }

    [Test]
    public async Task InvalidScannerBearerUsesTheSamePrivateGenericAbsenceAsWrongAction()
    {
        await using var factory = new AdmissionRateLimitFactory();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await SendInvalidScannerCheckIn(
            client,
            "invalid-scanner-capability");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(body).Contains("\"title\":\"Admission operation not found\"");
    }

    [Test]
    public async Task AdmissionDependencyOutageReturnsNoStoreBounded503ThatStopsClientQueues()
    {
        await using var factory = new AdmissionUnavailableFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/events/{Guid.CreateVersion7():D}/admission/check-ins")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    targetId = Guid.CreateVersion7(),
                    credential = "bounded-outage-probe"
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Pragma.Any(value =>
            string.Equals(value.Name, "no-cache", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy")).Contains("no-referrer");
        await Assert.That(body).Contains("\"code\":\"admission_check_in_unavailable\"");
        await Assert.That(body).Contains("Stop queued scans");
        await Assert.That(body).DoesNotContain("bounded-outage-probe");
    }

    [Test]
    public async Task AdmissionBatchDependencyOutageAbortsWithBounded503()
    {
        await using var factory = new AdmissionUnavailableFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/events/{Guid.CreateVersion7():D}/admission/check-ins/batch")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    targetId = Guid.CreateVersion7(),
                    items = new[]
                    {
                        new { credential = "batch-outage-one" },
                        new { credential = "batch-outage-two" }
                    }
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(body).Contains("\"code\":\"admission_check_in_unavailable\"");
        await Assert.That(body).DoesNotContain("batch-outage-one");
        await Assert.That(body).DoesNotContain("batch-outage-two");
    }

    [Test]
    public async Task HalAffordancesUseTheSamePublicPermissionContractAsDirectStaffRoutes()
    {
        await RequireAllRoutes();
        Guid eventId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        var dto = new EventDto
        {
            Id = eventId,
            TenantId = tenantId,
            Title = "Admission contract",
            ActorDisplayName = "Organizer",
            ActorTypeFullName = "Organization",
            EventStatusFullName = "Published",
            EventStatusMasterCode = "PUBLISHED",
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            EventFormatFullName = "In person",
            EventFormatMasterCode = "IN_PERSON",
            IsManagementView = true,
            ParticipationConfiguration = new EventParticipationConfigurationDto
            {
                ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.PlatformManaged
            }
        };

        LinkDefinition[] links = new EventDetailLinkPolicy().GetLinks(dto, null).ToArray();
        var expected = new Dictionary<string, (string OperationId, string Permission)>(StringComparer.Ordinal)
        {
            ["issue-scanner-capability"] = (
                "IssueAdmissionScannerCapability", AuthorizationActions.Events.ManageTickets),
            ["check-in-admissions"] = (
                "CheckInAdmission", AuthorizationActions.Events.EventCheckInManage),
            ["admission-check-in-summary"] = (
                "GetAdmissionCheckInSummary", AuthorizationActions.Events.EventCheckInView),
            ["admission-check-in-audit"] = (
                "GetAdmissionCheckInAudit", AuthorizationActions.Events.EventCheckInView),
            ["admission-check-in-health"] = (
                "GetAdmissionCheckInHealth", AuthorizationActions.Events.EventCheckInView),
            ["stop-admission-check-in"] = (
                "StopAdmissionCheckIn", AuthorizationActions.Events.EventCheckInManage),
            ["restore-admission-check-in"] = (
                "RestoreAdmissionCheckIn", AuthorizationActions.Events.EventCheckInManage),
            ["reconcile-admission-check-in"] = (
                "ReconcileAdmissionCheckIn", AuthorizationActions.Events.EventCheckInManage)
        };

        foreach ((string relation, (string operationId, string permission)) in expected)
        {
            LinkDefinition link = links.Single(candidate => candidate.Rel == relation);
            await Assert.That(link.RouteName).IsEqualTo(operationId);
            await Assert.That(link.PermissionAction).IsEqualTo(permission);
            await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.Event);
            await Assert.That(link.PermissionResourceId).IsEqualTo(eventId.ToString("D"));
            await Assert.That(link.PermissionScope?.TenantId).IsEqualTo(tenantId.ToString("D"));
        }

        foreach (string operationId in new[]
                 {
                     "IssueAdmissionScannerCapability", "RevokeAdmissionScannerCapability", "CheckInAdmission",
                     "UndoAdmissionCheckIn", "GetAdmissionCheckInSummary", "GetAdmissionCheckInAudit",
                     "GetAdmissionCheckInHealth", "StopAdmissionCheckIn", "RestoreAdmissionCheckIn",
                     "ReconcileAdmissionCheckIn"
                 })
        {
            ActionContract action = FindAction(ApiActions().ToArray(), Route(operationId))!;
            await Assert.That(SuccessResponse(action).Type?.Name.StartsWith(
                "Hal", StringComparison.Ordinal)).IsTrue()
                .Because($"{operationId} direct success and its advertised affordance must share HAL authority");
        }
    }

    [Test]
    public async Task AdmissionHalMutationControlsRequireTheirExactAuthority()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid checkInId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        var result = new AdmissionCheckInResult(
            AdmissionCheckInOutcome.CheckedIn,
            targetId,
            DateTimeOffset.UtcNow,
            checkInId);
        var url = Substitute.For<IUrlHelper>();
        url.Link(Arg.Any<string>(), Arg.Any<object>()).Returns("/bounded-route");

        var staff = new AdmissionCheckInController(null!, null!, null!, null!)
        {
            Url = url
        };
        HalResource<AdmissionCheckInResultDto> deniedStaff = staff.CheckInResource(
            eventId, result, canCheckIn: false, canUndo: false);
        HalResource<AdmissionCheckInResultDto> allowedStaff = staff.CheckInResource(
            eventId, result, canCheckIn: true, canUndo: true);
        await Assert.That(deniedStaff.Links).DoesNotContainKey(LinkRelations.CheckInAdmissions);
        await Assert.That(deniedStaff.Links).DoesNotContainKey(LinkRelations.UndoAdmissionCheckIn);
        await Assert.That(allowedStaff.Links).ContainsKey(LinkRelations.CheckInAdmissions);
        await Assert.That(allowedStaff.Links).ContainsKey(LinkRelations.UndoAdmissionCheckIn);

        var healthResult = new AdmissionCheckInHealthResult(
            targetId,
            AdmissionCheckInOperationalStatus.Active,
            AdmissionCheckInDependencyStatus.Available);
        var operations = new AdmissionCheckInOperationsController(null!, null!, null!)
        {
            Url = url
        };
        HalResource<AdmissionCheckInHealthDto> deniedOperations = operations.HealthResource(
            eventId, healthResult, canManage: false);
        HalResource<AdmissionCheckInHealthDto> allowedOperations = operations.HealthResource(
            eventId, healthResult, canManage: true);
        await Assert.That(deniedOperations.Links).DoesNotContainKey(LinkRelations.StopAdmissionCheckIn);
        await Assert.That(deniedOperations.Links).DoesNotContainKey(LinkRelations.ReconcileAdmissionCheckIn);
        await Assert.That(allowedOperations.Links).ContainsKey(LinkRelations.StopAdmissionCheckIn);
        await Assert.That(allowedOperations.Links).ContainsKey(LinkRelations.ReconcileAdmissionCheckIn);

        HalResource<AdmissionCheckInResultDto> ScannerResource(params AdmissionCheckInAction[] actions)
        {
            var controller = new AdmissionScannerCheckInController(null!);
            var claims = new List<Claim>
            {
                new(AdmissionScannerAuthenticationDefaults.CapabilityIdClaim, Guid.CreateVersion7().ToString("D")),
                new(AdmissionScannerAuthenticationDefaults.TenantIdClaim, Guid.CreateVersion7().ToString("D")),
                new(AdmissionScannerAuthenticationDefaults.EventIdClaim, eventId.ToString("D")),
                new(AdmissionScannerAuthenticationDefaults.TargetIdClaim, targetId.ToString("D"))
            };
            claims.AddRange(actions.Select(action => new Claim(
                AdmissionScannerAuthenticationDefaults.ActionClaim,
                action.ToString())));
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        claims,
                        ApiAuthenticationSchemeNames.AdmissionScanner))
                }
            };
            return controller.Resource(result);
        }

        HalResource<AdmissionCheckInResultDto> checkInOnly =
            ScannerResource(AdmissionCheckInAction.CheckIn);
        HalResource<AdmissionCheckInResultDto> undoOnly =
            ScannerResource(AdmissionCheckInAction.Undo);
        HalResource<AdmissionCheckInResultDto> both = ScannerResource(
            AdmissionCheckInAction.CheckIn,
            AdmissionCheckInAction.Undo);
        await Assert.That(checkInOnly.Links).ContainsKey(LinkRelations.CheckInAdmissions);
        await Assert.That(checkInOnly.Links).DoesNotContainKey(LinkRelations.UndoAdmissionCheckIn);
        await Assert.That(undoOnly.Links).DoesNotContainKey(LinkRelations.CheckInAdmissions);
        await Assert.That(undoOnly.Links).ContainsKey(LinkRelations.UndoAdmissionCheckIn);
        await Assert.That(both.Links).ContainsKey(LinkRelations.CheckInAdmissions);
        await Assert.That(both.Links).ContainsKey(LinkRelations.UndoAdmissionCheckIn);
    }

    [Test]
    public async Task NamedScannerLimiterRejectsSaturationWithoutQueueing()
    {
        await RequireAllRoutes();
        await using var factory = new AdmissionRateLimitFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage first = await SendInvalidScannerCheckIn(client, "scanner-rate-1");
        using HttpResponseMessage saturated = await SendInvalidScannerCheckIn(client, "scanner-rate-2");
        string body = await saturated.Content.ReadAsStringAsync();

        await Assert.That(first.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(saturated.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(saturated.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(saturated.Headers.Contains("Retry-After")).IsTrue();
        await Assert.That(body).Contains("\"code\":\"rate_limited\"");
        await Assert.That(factory.Telemetry.Observations).IsEquivalentTo([
            (AdmissionCheckInSaturationKind.RateLimiter, AdmissionCheckInTelemetryOutcome.Rejected)
        ]);
    }

    [Test]
    public async Task ScannerRatePartitionsAreCapabilitySpecificAndSeparateFromInvalidTraffic()
    {
        Guid firstCapabilityId = Guid.CreateVersion7();
        Guid secondCapabilityId = Guid.CreateVersion7();
        DefaultHttpContext first = ScannerContext(firstCapabilityId);
        DefaultHttpContext second = ScannerContext(secondCapabilityId);
        var invalid = new DefaultHttpContext();
        invalid.Request.Method = HttpMethods.Post;
        invalid.Request.Path = "/api/admission/scanner/check-ins";

        await Assert.That(RateLimitingExtensions.GetAuthenticatedPartitionKey(first))
            .IsEqualTo($"admission-scanner:{firstCapabilityId:N}");
        await Assert.That(RateLimitingExtensions.GetAuthenticatedPartitionKey(second))
            .IsEqualTo($"admission-scanner:{secondCapabilityId:N}");
        await Assert.That(RateLimitingExtensions.GetAuthenticatedPartitionKey(invalid))
            .IsEqualTo("anonymous");
        await Assert.That(AuthenticationExtensions.SelectDefaultAuthenticationScheme(invalid))
            .IsEqualTo(ApiAuthenticationSchemeNames.AdmissionScanner);

        static DefaultHttpContext ScannerContext(Guid capabilityId)
        {
            var context = new DefaultHttpContext();
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    AdmissionScannerAuthenticationDefaults.CapabilityIdClaim,
                    capabilityId.ToString("D"))
            ], ApiAuthenticationSchemeNames.AdmissionScanner));
            return context;
        }
    }

    [Test]
    public async Task OpenApiRegistersEveryRouteOperationAndPhase21HalSchema()
    {
        using JsonDocument document = await GetOpenApiDocument();
        JsonElement paths = document.RootElement.GetProperty("paths");
        string[] missingOperations = Routes.Where(route =>
                !paths.TryGetProperty(route.Path, out JsonElement path)
                || !path.TryGetProperty(route.Method.ToLowerInvariant(), out _))
            .Select(route => $"{route.Method} {route.Path}")
            .ToArray();
        await Assert.That(missingOperations).IsEmpty()
            .Because("Phase 21 OpenAPI requires " + string.Join(", ", missingOperations));

        foreach (RouteSpec route in Routes)
        {
            JsonElement operation = paths.GetProperty(route.Path).GetProperty(route.Method.ToLowerInvariant());
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(route.OperationId);
            await Assert.That(operation.GetProperty("x-rate-limit-policy").GetString()).IsEqualTo(route.RatePolicy);
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString())
                .IsEqualTo("Authenticated");
            if (route.Audience == Audience.ScannerCapability)
                await Assert.That(SecuritySchemes(operation)).IsEquivalentTo([ApiAuthenticationSchemeNames.AdmissionScanner]);
            else
                await Assert.That(SecuritySchemes(operation)).DoesNotContain(ApiAuthenticationSchemeNames.AdmissionScanner);
            await Assert.That(ResponseSchemaReference(operation, SuccessStatus(operation)))
                .IsEqualTo($"#/components/schemas/{route.SuccessSchema}");
        }

        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        string[] requiredSchemas =
        [
            "IssueAdmissionScannerCapabilityRequestDto", "RevokeAdmissionScannerCapabilityRequestDto",
            "AdmissionScannerCapabilityIssuedDto", "AdmissionScannerCapabilityDto",
            "AdmissionCheckInRequestDto", "AdmissionScannerCheckInRequestDto",
            "AdmissionCheckInUndoRequestDto", "AdmissionScannerCheckInUndoRequestDto",
            "AdmissionCheckInUndoReasonCodeEnum", "AdmissionCheckInBatchItemRequestDto",
            "AdmissionCheckInBatchRequestDto", "AdmissionScannerCheckInBatchRequestDto",
            "AdmissionCheckInResultDto", "AdmissionCheckInBatchResultDto",
            "AdmissionCheckInSummaryDto", "AdmissionCheckInAuditItemDto", "AdmissionCheckInAuditPageDto",
            "HalResourceOfAdmissionScannerCapabilityIssuedDto",
            "HalCollectionResourceOfAdmissionScannerCapabilityDto", "HalResourceOfAdmissionCheckInResultDto",
            "HalResourceOfAdmissionCheckInBatchResultDto", "HalResourceOfAdmissionCheckInSummaryDto",
            "HalResourceOfAdmissionCheckInAuditPageDto"
        ];

        foreach (string schema in requiredSchemas)
            await Assert.That(schemas.TryGetProperty(schema, out _)).IsTrue().Because($"missing schema {schema}");

        foreach (string removedSchema in new[]
                 {
                     "IssueAdmissionScannerCapabilityRequest", "RevokeAdmissionScannerCapabilityRequest",
                     "AdmissionCheckInRequest", "AdmissionScannerCheckInRequest",
                     "AdmissionCheckInUndoRequest", "AdmissionScannerCheckInUndoRequest",
                     "AdmissionCheckInBatchItemRequest", "AdmissionCheckInBatchRequest",
                     "AdmissionScannerCheckInBatchRequest"
                 })
        {
            await Assert.That(schemas.TryGetProperty(removedSchema, out _)).IsFalse()
                .Because($"pre-v1 schema alias {removedSchema} must be absent");
        }

        foreach (string undoSchemaName in new[]
                 {
                     "AdmissionCheckInUndoRequestDto",
                     "AdmissionScannerCheckInUndoRequestDto"
                 })
        {
            JsonElement properties = Schema(document, undoSchemaName).GetProperty("properties");
            await Assert.That(properties.TryGetProperty("reasonCode", out _)).IsTrue();
            await Assert.That(properties.TryGetProperty("reason", out _)).IsFalse();
        }

        JsonElement summaryOperation = paths.GetProperty(Route("GetAdmissionCheckInSummary").Path).GetProperty("get");
        JsonElement targetParameter = summaryOperation.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "targetId");
        await Assert.That(targetParameter.GetProperty("required").GetBoolean()).IsTrue();

        JsonElement summaryProperties = Schema(document, "AdmissionCheckInSummaryDto").GetProperty("properties");
        foreach (string count in new[] { "checkedInCount", "undoneCount", "activeCount", "inactiveCount" })
            await Assert.That(summaryProperties.GetProperty(count).GetProperty("format").GetString()).IsEqualTo("int64");
        await Assert.That(summaryProperties.TryGetProperty("rejectedCount", out _)).IsFalse();

        JsonElement auditProperties = Schema(document, "AdmissionCheckInAuditPageDto").GetProperty("properties");
        JsonElement auditItemProperties = Schema(document, "AdmissionCheckInAuditItemDto").GetProperty("properties");
        await Assert.That(HasJsonType(
            auditProperties.GetProperty("nextCursor"),
            "string")).IsTrue();
        await Assert.That(HasJsonType(
            auditItemProperties.GetProperty("cursor"),
            "string")).IsTrue();
        foreach (string forbidden in new[]
                 {
                     "tenantId", "eventId", "targetId", "actorId", "capabilityId", "deviceLabel", "reason",
                     "credential", "ticketId", "participant", "email"
                 })
            await Assert.That(auditItemProperties.TryGetProperty(forbidden, out _)).IsFalse();

        JsonElement capabilityIssueProperties = Schema(
            document, "IssueAdmissionScannerCapabilityRequestDto").GetProperty("properties");
        JsonElement capabilityProperties = Schema(
            document, "AdmissionScannerCapabilityDto").GetProperty("properties");
        await Assert.That(capabilityIssueProperties.TryGetProperty("targetId", out _)).IsTrue();
        await Assert.That(capabilityIssueProperties.TryGetProperty("targetIds", out _)).IsFalse();
        await Assert.That(capabilityProperties.TryGetProperty("targetId", out _)).IsTrue();
        await Assert.That(capabilityProperties.TryGetProperty("targetIds", out _)).IsFalse();

        JsonElement staffCheckIn = paths.GetProperty(Route("CheckInAdmission").Path).GetProperty("post");
        JsonElement scannerCheckIn = paths.GetProperty(Route("ScannerCheckInAdmission").Path).GetProperty("post");
        await Assert.That(RequestSchemaReference(staffCheckIn))
            .IsEqualTo("#/components/schemas/AdmissionCheckInRequestDto");
        await Assert.That(RequestSchemaReference(scannerCheckIn))
            .IsEqualTo("#/components/schemas/AdmissionScannerCheckInRequestDto");
        JsonElement staffProperties = Schema(document, "AdmissionCheckInRequestDto").GetProperty("properties");
        JsonElement scannerProperties = Schema(document, "AdmissionScannerCheckInRequestDto").GetProperty("properties");
        await Assert.That(staffProperties.TryGetProperty("targetId", out _)).IsTrue();
        await Assert.That(scannerProperties.TryGetProperty("targetId", out _)).IsFalse();
        await Assert.That(scannerProperties.TryGetProperty("eventId", out _)).IsFalse();

        string[] plaintextSchemas = requiredSchemas
            .Where(schema => schemas.TryGetProperty(schema, out JsonElement value)
                             && value.TryGetProperty("properties", out JsonElement properties)
                             && properties.TryGetProperty("capability", out _))
            .ToArray();
        await Assert.That(plaintextSchemas).IsEquivalentTo([
            "AdmissionScannerCapabilityIssuedDto",
            "HalResourceOfAdmissionScannerCapabilityIssuedDto"
        ]).Because("only the issue response DTO and its non-empty HAL projection may describe scanner plaintext");
    }

    private static void ValidateStaffRoute(ActionContract action, RouteSpec route, List<string> violations)
    {
        AuthorizeAttribute[] authorization = EffectiveAttributes<AuthorizeAttribute>(action);
        if (authorization.Length == 0)
            violations.Add($"{route.Path} is not authenticated");
        if (authorization.Any(attribute => AuthenticationSchemes(attribute).Contains(
                ApiAuthenticationSchemeNames.AdmissionScanner, StringComparer.Ordinal)))
            violations.Add($"{route.Path} mixes staff bearer and AdmissionScanner authority");
        if (EffectiveAttribute<AllowAnonymousAttribute>(action) is not null)
            violations.Add($"{route.Path} unexpectedly permits anonymous callers");
        if (EffectiveAttribute<EndpointClassificationAttribute>(action)?.Class != EndpointClass.Authenticated)
            violations.Add($"{route.Path} is not Authenticated-class");
        if (action.Method.GetParameters().Any(IsScannerCapabilityParameter))
            violations.Add($"{route.Path} accepts scanner capability authority on a staff route");
    }

    private static void ValidateScannerRoute(ActionContract action, RouteSpec route, List<string> violations)
    {
        AuthorizeAttribute[] authorization = EffectiveAttributes<AuthorizeAttribute>(action);
        string[] schemes = authorization.SelectMany(AuthenticationSchemes).Distinct(StringComparer.Ordinal).ToArray();
        if (authorization.Length != 1 || !schemes.SequenceEqual([ApiAuthenticationSchemeNames.AdmissionScanner]))
            violations.Add($"{route.Path} must use only [Authorize(AuthenticationSchemes = AdmissionScanner)]");
        if (EffectiveAttribute<AllowAnonymousAttribute>(action) is not null)
            violations.Add($"{route.Path} must never permit anonymous callers");
        if (EffectiveAttribute<EndpointClassificationAttribute>(action)?.Class != EndpointClass.Authenticated)
            violations.Add($"{route.Path} is not Authenticated-class");
        if (action.Method.GetCustomAttribute<RequireIdempotencyKeyAttribute>() is null)
            violations.Add($"{route.Path} lacks idempotency replay protection");
        if (action.Method.GetParameters().Any(IsScannerCapabilityParameter))
            violations.Add($"{route.Path} must derive capability material from the AdmissionScanner principal");
        if (action.Template.Contains("eventId", StringComparison.OrdinalIgnoreCase)
            || action.Template.Contains("targetId", StringComparison.OrdinalIgnoreCase))
            violations.Add($"{route.Path} lets scanner callers assert capability scope in the URL");
    }

    private static void ValidateProblemMetadata(ActionContract action, RouteSpec route, List<string> violations)
    {
        ProducesResponseTypeAttribute[] responses = ProducedResponses(action);
        int[] required = route.Method == HttpMethods.Get
            ? [StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound, StatusCodes.Status429TooManyRequests]
            : [StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status429TooManyRequests];
        required = required.Append(StatusCodes.Status401Unauthorized).Distinct().ToArray();
        if (route.Audience == Audience.Staff)
            required = required.Append(StatusCodes.Status403Forbidden).Distinct().ToArray();

        foreach (int status in required)
        {
            ProducesResponseTypeAttribute? response = responses.SingleOrDefault(candidate => candidate.StatusCode == status);
            Type expected = status == StatusCodes.Status400BadRequest
                ? typeof(ValidationProblemDetails)
                : typeof(ProblemDetails);
            if (response?.Type != expected)
                violations.Add($"{route.OperationId} lacks RFC7807 {status} metadata");
        }
    }

    private static void ValidateHalSuccessType(ActionContract action, RouteSpec route, List<string> violations)
    {
        Type? type = SuccessResponse(action).Type;
        if (type is null || !type.Name.StartsWith("Hal", StringComparison.Ordinal))
            violations.Add($"{route.OperationId} does not publish a HAL success contract");
    }

    private static async Task<IReadOnlyList<ActionContract>> RequireAllRoutes()
    {
        IReadOnlyList<ActionContract> actions = ApiActions().ToArray();
        string[] missing = Routes.Where(route => FindAction(actions, route) is null)
            .Select(route => $"{route.Method} {route.Path}")
            .ToArray();
        await Assert.That(missing).IsEmpty().Because("Phase 21 requires " + string.Join(", ", missing));
        return actions;
    }

    private static RouteSpec Route(string operationId) => Routes.Single(route => route.OperationId == operationId);

    private static ActionContract? FindAction(IReadOnlyList<ActionContract> actions, RouteSpec expected) =>
        actions.SingleOrDefault(candidate => candidate.Template == expected.Path
                                             && candidate.HttpMethod == expected.Method);

    private static IEnumerable<ActionContract> ApiActions()
    {
        foreach (Type controller in typeof(Program).Assembly.GetTypes().Where(type =>
                     !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)))
        {
            string prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            foreach (MethodInfo method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (HttpMethodAttribute route in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    string template = NormalizeRoute(CombineRoute(prefix, route.Template));
                    foreach (string httpMethod in route.HttpMethods)
                        yield return new ActionContract(controller, method, template, httpMethod, route.Name);
                }
            }
        }
    }

    private static string CombineRoute(string prefix, string? suffix) => "/" + string.Join('/',
        new[] { prefix, suffix }.Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim('/')));

    private static string NormalizeRoute(string route)
    {
        var result = new StringBuilder(route.Length);
        bool constraint = false;
        foreach (char character in route)
        {
            if (character == ':')
            {
                constraint = true;
                continue;
            }
            if (constraint && character != '}')
                continue;
            if (character == '}')
                constraint = false;
            result.Append(character);
        }
        return result.ToString();
    }

    private static T? EffectiveAttribute<T>(ActionContract action) where T : Attribute =>
        action.Method.GetCustomAttribute<T>() ?? action.Controller.GetCustomAttribute<T>();

    private static T[] EffectiveAttributes<T>(ActionContract action) where T : Attribute =>
        action.Method.GetCustomAttributes<T>().Concat(action.Controller.GetCustomAttributes<T>()).ToArray();

    private static string[] AuthenticationSchemes(AuthorizeAttribute attribute) =>
        (attribute.AuthenticationSchemes ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool IsScannerCapabilityParameter(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == ScannerCapabilityHeader
        || parameter.Name?.Equals("scannerCapability", StringComparison.OrdinalIgnoreCase) == true;

    private static ProducesResponseTypeAttribute[] ProducedResponses(ActionContract action) => action.Method
        .GetCustomAttributes<ProducesResponseTypeAttribute>()
        .ToArray();

    private static ProducesResponseTypeAttribute SuccessResponse(ActionContract action) => ProducedResponses(action)
        .Single(response => response.StatusCode is >= 200 and < 300);

    private static Type SuccessDto(ActionContract action) => UnwrapContractType(
        SuccessResponse(action).Type ?? throw new InvalidOperationException($"{action.RouteName} lacks success type."));

    private static Type RequestDto(ActionContract action)
    {
        ParameterInfo body = action.Method.GetParameters().Single(parameter =>
            parameter.GetCustomAttribute<FromBodyAttribute>() is not null);
        return body.ParameterType;
    }

    private static Type UnwrapContractType(Type type)
    {
        while (type.IsGenericType && type.GetGenericArguments().Length == 1)
            type = type.GetGenericArguments()[0];
        return type;
    }

    private static PropertyInfo[] SecretProperties(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.Name is "Capability" or "PlaintextCapability" or "Token" or "Secret"
                           || property.Name.Contains("Digest", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private static IEnumerable<Type> PublicTypesReferencedBy(Type type) => type.GetProperties()
        .SelectMany(property =>
        {
            Type candidate = property.PropertyType;
            if (candidate.IsArray)
                candidate = candidate.GetElementType()!;
            else if (candidate.IsGenericType)
                candidate = candidate.GetGenericArguments().Last();
            return candidate.Assembly == type.Assembly && candidate != typeof(string) ? [candidate] : Array.Empty<Type>();
        })
        .Distinct();

    private async Task<JsonDocument> GetOpenApiDocument()
    {
        using HttpResponseMessage response = await fixture.Client.GetAsync(OpenApiEndpoint);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Schema(JsonDocument document, string name) => document.RootElement
        .GetProperty("components").GetProperty("schemas").GetProperty(name);

    private static bool HasJsonType(JsonElement schema, string expected)
    {
        JsonElement type = schema.GetProperty("type");
        return type.ValueKind == JsonValueKind.String
            ? string.Equals(type.GetString(), expected, StringComparison.Ordinal)
            : type.ValueKind == JsonValueKind.Array &&
              type.EnumerateArray().Any(value =>
                  string.Equals(value.GetString(), expected, StringComparison.Ordinal));
    }

    private static int SuccessStatus(JsonElement operation) => operation.GetProperty("responses")
        .EnumerateObject().Select(response => int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture))
        .Single(status => status is >= 200 and < 300);

    private static string ResponseSchemaReference(JsonElement operation, int status) => operation
        .GetProperty("responses").GetProperty(status.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .GetProperty("content").EnumerateObject().First().Value.GetProperty("schema").GetProperty("$ref").GetString()!;

    private static string RequestSchemaReference(JsonElement operation) => operation.GetProperty("requestBody")
        .GetProperty("content").EnumerateObject().First().Value.GetProperty("schema").GetProperty("$ref").GetString()!;

    private static string[] SecuritySchemes(JsonElement operation) => operation.GetProperty("security")
        .EnumerateArray().SelectMany(requirement => requirement.EnumerateObject())
        .Select(scheme => scheme.Name).Distinct(StringComparer.Ordinal).ToArray();

    private static async Task<HttpResponseMessage> SendInvalidScannerCheckIn(HttpClient client, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admission/scanner/check-ins")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add(ScannerCapabilityHeader, "phase21-rate-limit-probe");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private enum Audience
    {
        Staff,
        ScannerCapability
    }

    private sealed record RouteSpec(
        string Path,
        string Method,
        string OperationId,
        Audience Audience,
        string RatePolicy,
        string SuccessSchema);

    private sealed record ActionContract(
        Type Controller,
        MethodInfo Method,
        string Template,
        string HttpMethod,
        string? RouteName);

    private sealed class ExactRequestDispatcher
    {
        private readonly IReadOnlyDictionary<Type, (Type ResponseType, string[] OperationIds)> contracts;

        internal ExactRequestDispatcher(IEnumerable<ActionContract> actions)
        {
            contracts = actions.GroupBy(RequestDto).ToDictionary(
                group => group.Key,
                group =>
                {
                    Type[] responseTypes = group.Select(SuccessDto).Distinct().ToArray();
                    if (responseTypes.Length != 1)
                        throw new InvalidOperationException(
                            $"{group.Key.FullName} is paired with conflicting Phase 21 response contracts.");
                    return (responseTypes[0], group.Select(action => action.RouteName
                        ?? throw new InvalidOperationException("Phase 21 write route lacks an operation ID.")).ToArray());
                });
        }

        internal string[] Dispatch(object request, Type responseType)
        {
            if (!contracts.TryGetValue(request.GetType(), out var contract))
                throw new InvalidOperationException(
                    $"Phase 21 dispatcher rejects exact request type {request.GetType().AssemblyQualifiedName}.");
            if (contract.ResponseType != responseType)
                throw new InvalidOperationException(
                    $"{request.GetType().FullName} requested {responseType.FullName}; expected {contract.ResponseType.FullName}.");
            return contract.OperationIds;
        }
    }

    private sealed record DecoyAdmissionCheckInRequest;
    private sealed record DecoyResponse;

    private sealed class AdmissionRateLimitFactory : AuthenticatedWebApplicationFactory
    {
        internal RecordingAdmissionTelemetry Telemetry { get; } = new();

        public AdmissionRateLimitFactory()
        {
            AdditionalConfiguration["RateLimiting:DisableInTesting"] = "false";
            AdditionalConfiguration["RateLimiting:AdmissionScannerCheckIn:PermitLimit"] = "1";
            AdditionalConfiguration["RateLimiting:AdmissionScannerCheckIn:WindowSeconds"] = "60";
            AdditionalConfiguration["RateLimiting:AdmissionScannerCheckIn:QueueLimit"] = "0";
            AdditionalConfiguration["RateLimiting:Global:TokenLimit"] = "1000";
            AdditionalConfiguration["RateLimiting:Global:TokensPerPeriod"] = "1000";
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAdmissionCheckInTelemetry>();
                services.AddSingleton<IAdmissionCheckInTelemetry>(Telemetry);
            });
        }
    }

    private sealed class AdmissionUnavailableFactory : AuthenticatedWebApplicationFactory
    {
        public AdmissionUnavailableFactory() => AuthorizationProviderOverride = new StubAuthorizationProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAdmissionCheckInAuthority>();
                services.AddScoped<IAdmissionCheckInAuthority, UnavailableAdmissionAuthority>();
            });
        }
    }

    private sealed class UnavailableAdmissionAuthority : IAdmissionCheckInAuthority
    {
        public Task<AdmissionCheckInAuthorizationDecision> AuthorizeAsync(
            AdmissionCheckInAuthorizationRequest request,
            CancellationToken cancellationToken) =>
            throw new AdmissionCheckInUnavailableException();
    }

    private sealed class AdmissionReportingFactory : AuthenticatedWebApplicationFactory
    {
        public AdmissionReportingFactory() => AuthorizationProviderOverride = new StubAuthorizationProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAdmissionCheckInSummaryQuery>();
                services.RemoveAll<IAdmissionCheckInReportingRepository>();
                services.AddScoped<IAdmissionCheckInSummaryQuery, EmptySummaryQuery>();
                services.AddScoped<IAdmissionCheckInReportingRepository, EmptyReportingRepository>();
            });
        }
    }

    private sealed class EmptySummaryQuery : IAdmissionCheckInSummaryQuery
    {
        public Task<AdmissionCheckInSummaryProjection?> GetAsync(Guid tenantId, Guid eventId, Guid targetId,
            CancellationToken cancellationToken) => Task.FromResult<AdmissionCheckInSummaryProjection?>(null);
    }

    private sealed class EmptyReportingRepository : IAdmissionCheckInReportingRepository
    {
        public Task<Explore.Domain.AdmissionCheckInEvent?> GetEventAsync(
            Guid tenantId,
            Guid eventId,
            Guid checkInId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Explore.Domain.AdmissionCheckInEvent?>(null);

        public Task<Explore.Domain.AdmissionCheckInEvent?> GetActiveEventAsync(
            Guid tenantId,
            Guid ticketId,
            Guid targetId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Explore.Domain.AdmissionCheckInEvent?>(null);

        public Task<IReadOnlyList<Explore.Domain.AdmissionCheckInEvent>> ListEventAuditPageAsync(Guid tenantId,
            Guid eventId, Explore.Domain.AdmissionCheckInEvent? cursor, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Explore.Domain.AdmissionCheckInEvent>>([]);

        public Task<IReadOnlyList<Explore.Domain.AdmissionTarget>> ListTargetsAsync(Guid tenantId, Guid eventId,
            IReadOnlyList<Guid> targetIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Explore.Domain.AdmissionTarget>>([]);
    }

    private sealed class RecordingAdmissionTelemetry : IAdmissionCheckInTelemetry
    {
        internal List<(AdmissionCheckInSaturationKind, AdmissionCheckInTelemetryOutcome)> Observations { get; } = [];
        public void RecordOperation(AdmissionCheckInAction action, AdmissionCheckInAuthorityKind authorityKind,
            AdmissionTargetTypeEnum? targetType, AdmissionCheckInTelemetryOutcome outcome, double durationMilliseconds) { }
        public void RecordBatch(AdmissionCheckInAuthorityKind authorityKind, AdmissionTargetTypeEnum? targetType,
            int batchSize) { }
        public void RecordSaturation(AdmissionCheckInSaturationKind kind, AdmissionCheckInTelemetryOutcome outcome) =>
            Observations.Add((kind, outcome));
        public void RecordBacklog(AdmissionCheckInBacklogKind kind, AdmissionTargetTypeEnum? targetType, long depth) { }
    }
}
