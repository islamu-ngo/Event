using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.RegistrationForms.Handlers.Queries;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class ParticipationRequirementAttachmentControllerContractTests
{
    [Test]
    public async Task ControllerExposesCanonicalAttachmentAndQuestionnaireRoutes()
    {
        MethodInfo? attach = typeof(EventParticipationController).GetMethod("AttachRequirement");
        MethodInfo? detach = typeof(EventParticipationController).GetMethod("DetachRequirement");
        MethodInfo? get = typeof(EventParticipationController).GetMethod("GetOptionalQuestionnaire");

        await Assert.That(attach).IsNotNull();
        await Assert.That(detach).IsNotNull();
        await Assert.That(get).IsNotNull();
        await Assert.That(attach!.GetCustomAttribute<HttpPostAttribute>()!.Template)
            .IsEqualTo("requirements/{requirementId:guid}");
        await Assert.That(detach!.GetCustomAttribute<HttpDeleteAttribute>()!.Template)
            .IsEqualTo("requirements/{requirementId:guid}");
        await Assert.That(get!.GetCustomAttribute<HttpGetAttribute>()!.Template)
            .IsEqualTo("optional-questionnaire");
        await Assert.That(attach.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(detach.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(get.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(RouteNames.GetOptionalQuestionnaire).IsEqualTo("GetOptionalQuestionnaire");
        await Assert.That(LinkRelations.OptionalQuestionnaire).IsEqualTo("optional-questionnaire");
    }

    [Test]
    public async Task EventHalAdvertisesOnlyAValidWalkInQuestionnaireDescriptor()
    {
        EventDto valid = PublicWalkInEvent(hasValidQuestionnaire: true);
        EventDto invalid = PublicWalkInEvent(hasValidQuestionnaire: false);
        var policy = new EventDetailLinkPolicy();

        LinkDefinition link = policy.GetLinks(valid, null)
            .Single(candidate => candidate.Rel == LinkRelations.OptionalQuestionnaire);

        await Assert.That(link.RouteName).IsEqualTo(RouteNames.GetOptionalQuestionnaire);
        await Assert.That(link.Method).IsEqualTo(HttpMethods.Get);
        await Assert.That(policy.GetLinks(invalid, null)
            .Any(candidate => candidate.Rel == LinkRelations.OptionalQuestionnaire)).IsFalse();
    }

    [Test]
    public async Task RequirementHalAdvertisesExactlyOneStateValidAttachmentMutation()
    {
        var policy = new RegistrationWorkflowLinkPolicy();
        RegistrationWorkflowDto workflow = Workflow();
        LinkDefinition[] unattached = policy.GetRequirementLinks(workflow, Requirement(isAttached: false)).ToArray();
        LinkDefinition[] attached = policy.GetRequirementLinks(workflow, Requirement(isAttached: true)).ToArray();

        LinkDefinition attach = unattached.Single(link => link.Rel == LinkRelations.Attach);
        LinkDefinition detach = attached.Single(link => link.Rel == LinkRelations.Detach);
        await Assert.That(unattached.Any(link => link.Rel == LinkRelations.Detach)).IsFalse();
        await Assert.That(attached.Any(link => link.Rel == LinkRelations.Attach)).IsFalse();
        await Assert.That(attach.RouteName).IsEqualTo(RouteNames.AttachRegistrationRequirement);
        await Assert.That(attach.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(attach.PermissionResourceKind).IsEqualTo(ResourceKinds.RegistrationForm);
        await Assert.That(attach.PermissionAction).IsEqualTo(AuthorizationActions.RegistrationForms.Attach);
        await Assert.That(detach.RouteName).IsEqualTo(RouteNames.DetachRegistrationRequirement);
        await Assert.That(detach.Method).IsEqualTo(HttpMethods.Delete);
        await Assert.That(detach.PermissionResourceKind).IsEqualTo(ResourceKinds.RegistrationForm);
        await Assert.That(detach.PermissionAction).IsEqualTo(AuthorizationActions.RegistrationForms.Detach);
    }

    [Test]
    public async Task DirectQuestionnaireGetUsesEligibilityGateAndTheSameNonDisclosingNotFoundPath()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid ineligibleEventId = Guid.CreateVersion7();
        Guid missingDescriptorEventId = Guid.CreateVersion7();
        var attachments = Substitute.For<IParticipationRequirementAttachmentRepository>();
        var events = Substitute.For<IEventRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        events.IsPubliclyEligibleAsync(tenantId, ineligibleEventId, Arg.Any<CancellationToken>())
            .Returns(false);
        events.IsPubliclyEligibleAsync(tenantId, missingDescriptorEventId, Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new GetOptionalQuestionnaireQueryHandler(attachments, events, tenantContext);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetOptionalQuestionnaireQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => handler.Handle(
                call.Arg<GetOptionalQuestionnaireQuery>(),
                call.Arg<CancellationToken>()));
        var assembler = Substitute.For<IResourceAssembler<OptionalQuestionnaireDto, OptionalQuestionnaireDto>>();
        var controller = new EventParticipationController(mediator, assembler)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        ActionResult<HalResource<OptionalQuestionnaireDto>> ineligible =
            await controller.GetOptionalQuestionnaire(ineligibleEventId, CancellationToken.None);
        ActionResult<HalResource<OptionalQuestionnaireDto>> missing =
            await controller.GetOptionalQuestionnaire(missingDescriptorEventId, CancellationToken.None);
        var ineligibleResponse = (ObjectResult)ineligible.Result!;
        var missingResponse = (ObjectResult)missing.Result!;
        var ineligibleProblem = (ProblemDetails)ineligibleResponse.Value!;
        var missingProblem = (ProblemDetails)missingResponse.Value!;

        await Assert.That(ineligibleResponse.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(missingResponse.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(ineligibleProblem.Title).IsEqualTo(missingProblem.Title);
        await Assert.That(ineligibleProblem.Detail).IsEqualTo(missingProblem.Detail);
        await attachments.DidNotReceive().GetOptionalQuestionnaireAsync(
            ineligibleEventId, tenantId, Arg.Any<CancellationToken>());
        await attachments.Received(1).GetOptionalQuestionnaireAsync(
            missingDescriptorEventId, tenantId, Arg.Any<CancellationToken>());
    }

    private static RegistrationWorkflowDto Workflow() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "registration",
        Guid.CreateVersion7(),
        [],
        []);

    private static RegistrationRequirementDto Requirement(bool isAttached) => new(
        Guid.CreateVersion7(),
        1,
        (int)RegistrationRequirementCriticalityEnum.Informational,
        "INFORMATIONAL",
        "Informational",
        true,
        (int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
        "NO_REGISTRATION_EFFECT",
        "No registration effect",
        (int)RegistrationAnswerSyncModeEnum.NONE,
        "NONE",
        "None",
        (int)RegistrationRequirementSubjectTypeEnum.AllOrders,
        "ALL_ORDERS",
        "All orders",
        null,
        Guid.CreateVersion7(),
        isAttached,
        []);

    private static EventDto PublicWalkInEvent(bool hasValidQuestionnaire) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        Title = "Walk-in event",
        ActorDisplayName = "Organizer",
        ActorTypeFullName = "User",
        EventStatusFullName = "Published",
        EventStatusMasterCode = "PUBLISHED",
        EventStatusId = (int)EventStatusEnum.Published,
        VisibilityTypeFullName = "Public",
        VisibilityTypeMasterCode = "PUBLIC",
        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
        EventFormatFullName = "In person",
        EventFormatMasterCode = "IN_PERSON",
        IsPubliclyEligible = true,
        ParticipationConfiguration = new EventParticipationConfigurationDto
        {
            ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.WalkIn,
            HasValidOptionalQuestionnaire = hasValidQuestionnaire
        }
    };
}
