using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas.Policies;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

[Category("Phase20AdmissionApiRed")]
[NotInParallel("Phase20AdmissionApiRed")]
public sealed partial class AdmissionTicketApiRedContractTests
{
    [Test]
    public async Task CurrentOrderReadsCharacterizeAccountAndGuestAuthorization()
    {
        MethodInfo account = typeof(AuthenticatedRegistrationOrderController)
            .GetMethod(nameof(AuthenticatedRegistrationOrderController.GetCurrent))!;
        MethodInfo guest = typeof(GuestRegistrationOrderController)
            .GetMethod(nameof(GuestRegistrationOrderController.GetGuest))!;

        await Assert.That(account.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(account.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(account.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(account.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        await Assert.That(account.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        await Assert.That(account.GetParameters().Any(IsCapabilityParameter)).IsFalse();

        await Assert.That(guest.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(guest.GetCustomAttribute<AuthorizeAttribute>()).IsNull();
        await Assert.That(guest.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Public);
        await Assert.That(guest.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        await Assert.That(guest.GetParameters().Single(IsCapabilityParameter)
            .GetCustomAttribute<FromHeaderAttribute>()?.Name)
            .IsEqualTo("X-Registration-Order-Capability");
    }

    [Test]
    public async Task CurrentOrderAffordancesCharacterizeHalAsTheAuthoritySurface()
    {
        var order = new RegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            AccountUserId = Guid.CreateVersion7(),
            StatusId = (int)RegistrationOrderStatusEnum.ReadyForCheckout,
            StatusCode = "READY_FOR_CHECKOUT"
        };

        LinkDefinition[] links = new RegistrationOrderLinkPolicy(TimeProvider.System)
            .GetLinks(order, null)
            .ToArray();

        await Assert.That(links.Select(link => link.Rel)).Contains(LinkRelations.Continue);
        await Assert.That(links.Select(link => link.Rel)).Contains(LinkRelations.Finalize);
        await Assert.That(links.Where(link => link.Rel is not LinkRelations.Self)
            .All(link => !string.IsNullOrWhiteSpace(link.PermissionAction)
                         && !string.IsNullOrWhiteSpace(link.PermissionResourceKind))).IsTrue();

        var scenario = new AdmissionApiScenario();
        var dispatcher = new AdmissionScenarioDispatcher(scenario, AdmissionApiRequestContracts.ForProbe());
        string validCapability = scenario.IssueValidCapability();
        var wrongMember = new CanonicalProbeRequests.RedeemAdmissionTicketRecoveryCommand(
            Capability: string.Empty, WrongMember: validCapability);
        var nestedTicket = new CanonicalProbeRequests.GetCurrentAdmissionTicketQuery(
            Guid.Empty, new ProbeNestedTicket(scenario.AccountTicketId));

        await Assert.That(() => dispatcher.Dispatch(
            new DecoyProbeRequests.RedeemAdmissionTicketRecoveryCommand(validCapability),
            typeof(ProbeResponse))).Throws<InvalidOperationException>();
        await Assert.That(dispatcher.Dispatch(wrongMember, typeof(ProbeResponse))).IsNull();
        await Assert.That(dispatcher.Dispatch(nestedTicket, typeof(ProbeResponse))).IsNull();
        await Assert.That(() => dispatcher.Dispatch(
            new CanonicalProbeRequests.RequestAdmissionTicketRecoveryCommand(scenario.PresentIdentity),
            typeof(WrongProbeResponse))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AdmissionRoutesExposeCanonicalMachineContracts()
    {
        var violations = new List<string>();
        foreach (ApiRouteContract expected in AllRoutes())
        {
            ActionContract? action = FindAction(expected);
            if (action is null)
            {
                violations.Add($"missing {expected.HttpMethod} {expected.Template}");
                continue;
            }

            if (action.RouteName != expected.RouteName)
                violations.Add($"{expected.Template} route name {action.RouteName ?? "<null>"}");

            bool accountRead = expected == AccountList || expected == AccountDetail
                || expected == AccountQr || expected == AccountPrint;
            if (accountRead && action.Method.GetCustomAttribute<AuthorizeAttribute>() is null)
                violations.Add($"{expected.Template} is not authenticated");
            if (accountRead && action.Method.GetCustomAttribute<EndpointClassificationAttribute>()?.Class
                != EndpointClass.Authenticated)
                violations.Add($"{expected.Template} is not Authenticated-class");

            if (expected == RecoveryConsume)
                ValidateRecoveryConsumeMetadata(action, violations);
            else if (expected == RecoveryRequest)
                ValidateRecoveryRequestMetadata(action, violations);

            if (expected != RecoveryRequest && !ProducedStatuses(action).Contains(StatusCodes.Status404NotFound))
                violations.Add($"{expected.Template} lacks generic 404 metadata");
        }

        await Assert.That(violations).IsEmpty().Because(string.Join("; ", violations));
    }

    private static void ValidateRecoveryConsumeMetadata(ActionContract action, List<string> violations)
    {
        if (action.Method.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            violations.Add("recovery consume is not anonymous");
        if (action.Method.GetCustomAttribute<EndpointClassificationAttribute>()?.Class != EndpointClass.Public)
            violations.Add("recovery consume is not Public-class");
        if (action.Method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName != RecoveryConsumeRateLimitPolicy)
            violations.Add("recovery consume lacks its dedicated rate policy");
        if (!action.Method.GetParameters().Any(parameter =>
                parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == RecoveryCapabilityHeader))
            violations.Add($"recovery consume lacks {RecoveryCapabilityHeader}");
        if (action.Method.GetCustomAttribute<RequireIdempotencyKeyAttribute>() is not null)
            violations.Add("one-time recovery consume must not replay through idempotency storage");
    }

    private static void ValidateRecoveryRequestMetadata(ActionContract action, List<string> violations)
    {
        if (action.Method.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            violations.Add("recovery request is not anonymous");
        if (action.Method.GetCustomAttribute<EndpointClassificationAttribute>()?.Class
            != EndpointClass.PublicTransactional)
            violations.Add("recovery request is not PublicTransactional-class");
        if (action.Method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
            != RecoveryRequestRateLimitPolicy)
            violations.Add("recovery request lacks the public transactional rate policy");
        if (action.Method.GetCustomAttribute<RequireIdempotencyKeyAttribute>() is null)
            violations.Add("recovery request lacks idempotency");
    }
}
