// ABOUTME: Verifies registration-form authoring route names and optimistic-concurrency metadata.
// ABOUTME: Protects stable operation IDs, authenticated classification, and write-rate limits.

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.RegistrationAnalytics;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.RegistrationAnalytics;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class RegistrationFormsControllerContractTests
{
    [Test]
    public async Task Preflight_UsesCanonicalPostRouteAndOperationId()
    {
        HttpPostAttribute route = typeof(RegistrationFormsController)
            .GetMethod(nameof(RegistrationFormsController.Preflight))!
            .GetCustomAttribute<HttpPostAttribute>()!;

        await Assert.That(route.Template).IsEqualTo("registration-forms/{formId:guid}/versions/{versionId:guid}/preflight");
        await Assert.That(route.Template).DoesNotContain("publish:preflight");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetRegistrationFormPublishPreflight);
    }

    [Test]
    public async Task Preflight_WhenBlocked_ReturnsStableValidationProblem()
    {
        var mediator = Substitute.For<IMediator>();
        var preflight = new RegistrationFormPublishPreflightDto(false,
            [new RegistrationFormPublishPreflightIssueDto("registration_form_required_field_missing", "Add a required field.")]);
        mediator.Send(Arg.Any<GetRegistrationFormPublishPreflightQuery>(), Arg.Any<CancellationToken>()).Returns(preflight);
        RegistrationFormsController controller = CreateController(mediator);

        ActionResult<HalResource<RegistrationFormPublishPreflightDto>> result = await controller.Preflight(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), CancellationToken.None);

        var problem = result.Result as ObjectResult;
        await Assert.That(problem?.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(((ProblemDetails)problem!.Value!).Extensions["code"]).IsEqualTo("registration_form_preflight_failed");
    }

    [Test]
    public async Task Preflight_WhenAllowed_ReturnsHalResource()
    {
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<RegistrationFormPublishPreflightDto, RegistrationFormPublishPreflightDto>>();
        var preflight = new RegistrationFormPublishPreflightDto(true, []);
        var resource = new HalResource<RegistrationFormPublishPreflightDto>(preflight);
        mediator.Send(Arg.Any<GetRegistrationFormPublishPreflightQuery>(), Arg.Any<CancellationToken>()).Returns(preflight);
        assembler.ToResource(preflight, Arg.Any<HttpContext>()).Returns(resource);
        RegistrationFormsController controller = CreateController(mediator, assembler);

        ActionResult<HalResource<RegistrationFormPublishPreflightDto>> result = await controller.Preflight(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), CancellationToken.None);

        var response = result.Result as ObjectResult;
        await Assert.That(response?.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(response!.Value).IsEqualTo(resource);
    }

    [Test]
    public async Task GetWorkflow_ReturnsFormAndImmutableVersionSummaries()
    {
        Guid versionId = Guid.CreateVersion7();
        var version = new RegistrationFormVersionSummaryDto(
            versionId, 2, 2, "published", "Published", "en-US", new string('a', 64),
            DateTime.UtcNow, null, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var form = new RegistrationFormDto(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "platform.registration", "attendee", "Attendee", Guid.CreateVersion7(), [version]);
        var workflow = new RegistrationWorkflowDto(
            Guid.CreateVersion7(), form.TenantId, form.EventId, "registration", Guid.CreateVersion7(), [], [form]);
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<RegistrationWorkflowDto, RegistrationWorkflowDto>>();
        var resource = new HalResource<RegistrationWorkflowDto>(workflow);
        mediator.Send(Arg.Any<GetRegistrationWorkflowQuery>(), Arg.Any<CancellationToken>()).Returns(workflow);
        assembler.ToResource(workflow, Arg.Any<HttpContext>()).Returns(resource);
        RegistrationFormsController controller = CreateController(mediator, workflowAssembler: assembler);

        ActionResult<HalResource<RegistrationWorkflowDto>> result = await controller.GetWorkflow(
            workflow.EventId, workflow.Purpose, CancellationToken.None);

        var response = result.Result as ObjectResult;
        var body = (HalResource<RegistrationWorkflowDto>)response!.Value!;
        await Assert.That(body.Data.Forms.Single().Versions.Single().Id).IsEqualTo(versionId);
    }

    [Test]
    public async Task GetAnswerAnalytics_UsesExactScopeAndHalAssembler()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<RegistrationAnswerAnalyticsDto, RegistrationAnswerAnalyticsDto>>();
        var analytics = new RegistrationAnswerAnalyticsDto(Guid.CreateVersion7(), eventId, formId, versionId, 3, []);
        var resource = new HalResource<RegistrationAnswerAnalyticsDto>(analytics);
        mediator.Send(Arg.Is<GetRegistrationAnswerAnalyticsQuery>(query =>
                query.EventId == eventId && query.FormId == formId && query.FormVersionId == versionId),
            Arg.Any<CancellationToken>()).Returns(analytics);
        assembler.ToResource(analytics, Arg.Any<HttpContext>()).Returns(resource);
        RegistrationFormsController controller = CreateController(mediator, analyticsAssembler: assembler);

        ActionResult<HalResource<RegistrationAnswerAnalyticsDto>> result = await controller.GetAnswerAnalytics(eventId, formId, versionId, CancellationToken.None);

        var response = result.Result as ObjectResult;
        await Assert.That(response?.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(response!.Value).IsEqualTo(resource);
    }

    [Test]
    public async Task Actions_UseStableNamedRoutesAndExplicitFailureContracts()
    {
        var routes = new Dictionary<string, string>
        {
            [nameof(RegistrationFormsController.GetWorkflow)] = RouteNames.GetRegistrationWorkflow,
            [nameof(RegistrationFormsController.GetAnswerAnalytics)] = RouteNames.GetRegistrationAnswerAnalytics,
            [nameof(RegistrationFormsController.CreateWorkflow)] = RouteNames.CreateRegistrationWorkflow,
            [nameof(RegistrationFormsController.UpdateWorkflow)] = RouteNames.UpdateRegistrationWorkflow,
            [nameof(RegistrationFormsController.CreateRequirement)] = RouteNames.CreateRegistrationRequirement,
            [nameof(RegistrationFormsController.UpdateRequirement)] = RouteNames.UpdateRegistrationRequirement,
            [nameof(RegistrationFormsController.DeleteRequirement)] = RouteNames.DeleteRegistrationRequirement,
            [nameof(RegistrationFormsController.GetForm)] = RouteNames.GetRegistrationForm,
            [nameof(RegistrationFormsController.CreateForm)] = RouteNames.CreateRegistrationForm,
            [nameof(RegistrationFormsController.GetVersion)] = RouteNames.GetRegistrationFormVersion,
            [nameof(RegistrationFormsController.CreateVersion)] = RouteNames.CreateRegistrationFormVersion,
            [nameof(RegistrationFormsController.AddSection)] = RouteNames.AddRegistrationFormSection,
            [nameof(RegistrationFormsController.UpdateSection)] = RouteNames.UpdateRegistrationFormSection,
            [nameof(RegistrationFormsController.DeleteSection)] = RouteNames.DeleteRegistrationFormSection,
            [nameof(RegistrationFormsController.AddField)] = RouteNames.AddRegistrationFormField,
            [nameof(RegistrationFormsController.UpdateField)] = RouteNames.UpdateRegistrationFormField,
            [nameof(RegistrationFormsController.DeleteField)] = RouteNames.DeleteRegistrationFormField,
            [nameof(RegistrationFormsController.AddOption)] = RouteNames.AddRegistrationFormFieldOption,
            [nameof(RegistrationFormsController.UpdateOption)] = RouteNames.UpdateRegistrationFormFieldOption,
            [nameof(RegistrationFormsController.RetireOption)] = RouteNames.RetireRegistrationFormFieldOption,
            [nameof(RegistrationFormsController.AddRule)] = RouteNames.AddRegistrationFormRule,
            [nameof(RegistrationFormsController.UpdateRule)] = RouteNames.UpdateRegistrationFormRule,
            [nameof(RegistrationFormsController.DeleteRule)] = RouteNames.DeleteRegistrationFormRule,
            [nameof(RegistrationFormsController.Preflight)] = RouteNames.GetRegistrationFormPublishPreflight,
            [nameof(RegistrationFormsController.Publish)] = RouteNames.PublishRegistrationFormVersion,
            [nameof(RegistrationFormsController.GetTemplates)] = RouteNames.GetRegistrationFormTemplates,
            [nameof(RegistrationFormsController.GetTemplate)] = RouteNames.GetRegistrationFormTemplate,
            [nameof(RegistrationFormsController.CreateTemplate)] = RouteNames.CreateRegistrationFormTemplate,
            [nameof(RegistrationFormsController.InstantiateTemplate)] = RouteNames.InstantiateRegistrationFormTemplate
        };

        await Assert.That(typeof(RegistrationFormsController).GetCustomAttribute<EndpointClassificationAttribute>()!.Class)
            .IsEqualTo(EndpointClass.Authenticated);
        foreach ((string actionName, string routeName) in routes)
        {
            MethodInfo action = typeof(RegistrationFormsController).GetMethod(actionName)!;
            await Assert.That(action.GetCustomAttribute<HttpMethodAttribute>()!.Name).IsEqualTo(routeName);
            foreach (int status in new[] { 400, 401, 403, 404 })
                await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(value => value.StatusCode == status)).IsTrue();
        }

        foreach (MethodInfo action in typeof(RegistrationFormsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                     .Where(method => method.DeclaringType == typeof(RegistrationFormsController) && method.Name is not nameof(RegistrationFormsController.GetWorkflow) and not nameof(RegistrationFormsController.GetAnswerAnalytics) and not nameof(RegistrationFormsController.GetForm) and not nameof(RegistrationFormsController.GetVersion) and not nameof(RegistrationFormsController.Preflight) and not nameof(RegistrationFormsController.GetTemplates) and not nameof(RegistrationFormsController.GetTemplate) and not nameof(RegistrationFormsController.CreateTemplate) and not nameof(RegistrationFormsController.InstantiateTemplate)))
        {
            ParameterInfo header = action.GetParameters().Single(parameter => parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == "If-Match");
            await Assert.That(header.GetCustomAttribute<RequiredAttribute>()).IsNotNull();
            await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
            await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(value => value.StatusCode == StatusCodes.Status409Conflict)).IsTrue();
        }

        foreach (string read in new[] { nameof(RegistrationFormsController.GetWorkflow), nameof(RegistrationFormsController.GetAnswerAnalytics), nameof(RegistrationFormsController.GetForm), nameof(RegistrationFormsController.GetVersion), nameof(RegistrationFormsController.Preflight), nameof(RegistrationFormsController.GetTemplates), nameof(RegistrationFormsController.GetTemplate) })
            await Assert.That(typeof(RegistrationFormsController).GetMethod(read)!.IsDefined(typeof(PrivateNoStoreAttribute))).IsTrue();
    }

    [Test]
    public async Task DeleteRequirement_AcceptsOnlyStrongQuotedNonEmptyGuid()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<DeleteRegistrationRequirementCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => BaseCommandResponse.Success(
                call.Arg<DeleteRegistrationRequirementCommand>().ExpectedConcurrencyStamp));
        RegistrationFormsController controller = CreateController(mediator);
        Guid eventId = Guid.CreateVersion7();
        Guid workflowId = Guid.CreateVersion7();
        Guid requirementId = Guid.CreateVersion7();
        Guid stamp = Guid.CreateVersion7();
        foreach (string? invalid in new[] { null, "", stamp.ToString(), $"W/\"{stamp:D}\"", "\"not-a-guid\"", $"\"{Guid.Empty:D}\"",
            "*", $"\"{stamp:D}", $"{stamp:D}\"", $"\"\"{stamp:D}\"\"", $"\"{stamp:D}\", \"{Guid.CreateVersion7():D}\"" })
        {
            var result = await controller.DeleteRequirement(eventId, workflowId, requirementId, invalid, CancellationToken.None);
            var response = result.Result as ObjectResult;
            await Assert.That(response?.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
            await Assert.That(response!.Value).IsTypeOf<ValidationProblemDetails>();
        }

        var accepted = await controller.DeleteRequirement(eventId, workflowId, requirementId, $"\"{stamp:D}\"", CancellationToken.None);
        var success = accepted.Result as OkObjectResult;
        await Assert.That(success).IsNotNull();
        await Assert.That(((BaseCommandResponse<Guid>)success!.Value!).Id).IsEqualTo(stamp);
    }

    private static RegistrationFormsController CreateController(
        IMediator mediator,
        IResourceAssembler<RegistrationFormPublishPreflightDto, RegistrationFormPublishPreflightDto>? preflightAssembler = null,
        IResourceAssembler<RegistrationWorkflowDto, RegistrationWorkflowDto>? workflowAssembler = null,
        IResourceAssembler<RegistrationAnswerAnalyticsDto, RegistrationAnswerAnalyticsDto>? analyticsAssembler = null) =>
        new(
            mediator,
            workflowAssembler ?? Substitute.For<IResourceAssembler<RegistrationWorkflowDto, RegistrationWorkflowDto>>(),
            Substitute.For<IResourceAssembler<RegistrationFormDto, RegistrationFormDto>>(),
            Substitute.For<IResourceAssembler<RegistrationFormVersionDto, RegistrationFormVersionDto>>(),
            analyticsAssembler ?? Substitute.For<IResourceAssembler<RegistrationAnswerAnalyticsDto, RegistrationAnswerAnalyticsDto>>(),
            Substitute.For<IResourceAssembler<RegistrationFormTemplateDto, RegistrationFormTemplateDto>>(),
            preflightAssembler ?? Substitute.For<IResourceAssembler<RegistrationFormPublishPreflightDto, RegistrationFormPublishPreflightDto>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
