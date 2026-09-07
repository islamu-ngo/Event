using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class RegistrationFormLinkPolicyTests
{
    [Test]
    public async Task WorkflowAssembler_EmbedsFormsUsingTheFormResourceShape()
    {
        var version = new RegistrationFormVersionSummaryDto(
            Guid.CreateVersion7(), 1, 2, "published", "Published", "en-US", new string('a', 64),
            DateTime.UtcNow, null, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var form = new RegistrationFormDto(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "platform.registration", "attendee", "Attendee", Guid.CreateVersion7(), [version]);
        var workflow = new RegistrationWorkflowDto(
            Guid.CreateVersion7(), form.TenantId, form.EventId, "registration", Guid.CreateVersion7(), [], [form]);
        var embeddedForm = new HalResource<RegistrationFormDto>
        {
            Data = form,
            Embedded = new Dictionary<string, object>
            {
                ["versions"] = new[] { new HalResource<RegistrationFormVersionSummaryDto>(version) }
            }
        };
        var formAssembler = Substitute.For<IResourceAssembler<RegistrationFormDto, RegistrationFormDto>>();
        var context = new DefaultHttpContext();
        formAssembler.ToResource(form, context).Returns(embeddedForm);
        var assembler = new RegistrationWorkflowResourceAssembler(
            Substitute.For<IHateoasLinkGenerator>(),
            Substitute.For<ILinkPolicy<RegistrationWorkflowDto>>(),
            Substitute.For<ICollectionLinkPolicy<RegistrationWorkflowDto>>(),
            new RegistrationWorkflowLinkPolicy(),
            formAssembler);

        HalResource<RegistrationWorkflowDto> resource = await assembler.ToResource(workflow, context);

        var forms = (List<HalResource<RegistrationFormDto>>)resource.Embedded!["forms"];
        await Assert.That(forms.Single()).IsEqualTo(embeddedForm);
        await Assert.That(forms.Single().Embedded!["versions"]).IsNotNull();
    }

    [Test]
    public async Task DraftVersion_UsesFormScopedPermissionsForAllChildMutations()
    {
        RegistrationFormVersionDto version = Version(RegistrationFormStatusEnum.Draft);
        RegistrationFormSectionDto section = version.Sections.Single();
        RegistrationFormFieldDto field = section.Fields.Single();
        RegistrationFormFieldOptionDto option = field.Options.Single();
        RegistrationFormRuleDto rule = version.Rules.Single();
        var policy = new RegistrationFormVersionLinkPolicy();
        LinkDefinition[] links = policy.GetLinks(version, null)
            .Concat(policy.GetSectionLinks(version, section))
            .Concat(policy.GetFieldLinks(version, section, field))
            .Concat(policy.GetOptionLinks(version, section, field, option))
            .Concat(policy.GetRuleLinks(version, rule))
            .Where(link => link.PermissionAction is not null)
            .ToArray();

        await Assert.That(links).IsNotEmpty();
        await Assert.That(links.All(link => link.PermissionResourceKind == ResourceKinds.RegistrationForm)).IsTrue();
        await Assert.That(links.All(link => link.PermissionResourceId == version.RegistrationFormId.ToString())).IsTrue();
        await Assert.That(links.All(link => link.PermissionScope?.TenantId == version.TenantId.ToString())).IsTrue();
    }

    [Test]
    public async Task PublishedVersion_OmitsEveryMutationIncludingChildren()
    {
        RegistrationFormVersionDto version = Version(RegistrationFormStatusEnum.Published);
        RegistrationFormSectionDto section = version.Sections.Single();
        RegistrationFormFieldDto field = section.Fields.Single();
        RegistrationFormFieldOptionDto option = field.Options.Single();
        RegistrationFormRuleDto rule = version.Rules.Single();
        var policy = new RegistrationFormVersionLinkPolicy();

        LinkDefinition[] links = policy.GetLinks(version, null)
            .Concat(policy.GetSectionLinks(version, section))
            .Concat(policy.GetFieldLinks(version, section, field))
            .Concat(policy.GetOptionLinks(version, section, field, option))
            .Concat(policy.GetRuleLinks(version, rule))
            .ToArray();

        await Assert.That(links.Select(link => link.Rel)).IsEquivalentTo(new[] { LinkRelations.Self, LinkRelations.Form, LinkRelations.Preflight });
    }

    [Test]
    public async Task RequirementLinks_AdvertiseOnlyTheStateAppropriateAttachmentAction()
    {
        RegistrationWorkflowDto workflow = Workflow();
        var detached = Requirement(isAttached: false);
        var attached = Requirement(isAttached: true);
        var policy = new RegistrationWorkflowLinkPolicy();

        LinkDefinition[] detachedLinks = policy.GetRequirementLinks(workflow, detached).ToArray();
        LinkDefinition[] attachedLinks = policy.GetRequirementLinks(workflow, attached).ToArray();

        await Assert.That(detachedLinks.Select(link => link.Rel)).Contains(LinkRelations.Attach);
        await Assert.That(detachedLinks.Select(link => link.Rel)).DoesNotContain(LinkRelations.Detach);
        await Assert.That(attachedLinks.Select(link => link.Rel)).Contains(LinkRelations.Detach);
        await Assert.That(attachedLinks.Select(link => link.Rel)).DoesNotContain(LinkRelations.Attach);
    }

    [Test]
    public async Task DraftVersionAssembler_WhenUpdateIsDenied_OmitsSectionAndFieldReorderLinks()
    {
        RegistrationFormVersionDto version = Version(RegistrationFormStatusEnum.Draft);
        var policy = new RegistrationFormVersionLinkPolicy();
        var evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(
                Arg.Any<IReadOnlyList<LinkDefinition>>(),
                Arg.Any<System.Security.Claims.ClaimsPrincipal?>(),
                Arg.Any<HttpContext>())
            .Returns(call => (IReadOnlyList<bool>)Enumerable.Repeat(
                false, call.Arg<IReadOnlyList<LinkDefinition>>().Count).ToArray());
        var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => new HalLink { Href = $"/{call.Arg<LinkDefinition>().Rel}" });
        var assembler = new RegistrationFormVersionResourceAssembler(
            linkGenerator,
            policy,
            new RegistrationFormVersionCollectionLinkPolicy(policy),
            policy,
            evaluator);

        HalResource<RegistrationFormVersionDto> resource = await assembler.ToResource(
            version, new DefaultHttpContext());

        var sections = (HalResource<RegistrationFormSectionDto>[])resource.Embedded!["sections"];
        await Assert.That(resource.Links!.ContainsKey(LinkRelations.ReorderSections)).IsFalse();
        await Assert.That(sections.All(section =>
            section.Links is null || !section.Links.ContainsKey(LinkRelations.ReorderFields))).IsTrue();
    }

    private static RegistrationFormVersionDto Version(RegistrationFormStatusEnum status)
    {
        var option = new RegistrationFormFieldOptionDto(Guid.CreateVersion7(), 1, "yes", "Yes", null, Guid.CreateVersion7());
        var field = new RegistrationFormFieldDto(
            Guid.CreateVersion7(), 1, "person", "consent", "Consent", 1, "TEXT", "Text", 1, 1,
            "ORGANIZER", "Organizer", false, false, false, null, false, false, null, null, null, true, false,
            null, null, null, null, null, null, null, null, Guid.CreateVersion7(), [option]);
        var section = new RegistrationFormSectionDto(Guid.CreateVersion7(), 1, "Details", Guid.CreateVersion7(), [field]);
        var condition = new RegistrationFormConditionInputDto("exists", "person", "consent");
        var rule = new RegistrationFormRuleDto(Guid.CreateVersion7(), 1, "person", "consent", 1, condition, Guid.CreateVersion7());
        return new RegistrationFormVersionDto(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, (int)status, status.ToString().ToUpperInvariant(), status.ToString(), "en", null, status == RegistrationFormStatusEnum.Published ? DateTime.UtcNow : null, null, null, null, Guid.CreateVersion7(), [section], [rule]);
    }

    private static RegistrationWorkflowDto Workflow() => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "registration", Guid.CreateVersion7(), [], []);

    private static RegistrationRequirementDto Requirement(bool isAttached) => new(
        Guid.CreateVersion7(), 1, 1, "REQUIRED", "Required", false,
        (int)RegistrationRequirementCompletionEffectEnum.BlocksRegistration, "BLOCKS_REGISTRATION", "Blocks registration",
        (int)RegistrationAnswerSyncModeEnum.COMPLETION_ONLY, "COMPLETION_ONLY", "Completion only",
        (int)RegistrationRequirementSubjectTypeEnum.AllOrders, "ALL_ORDERS", "All orders", null,
        Guid.CreateVersion7(), isAttached, []);
}
