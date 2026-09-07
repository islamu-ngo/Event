using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class RegistrationFormAuthoringServiceTests
{
    [Test]
    public async Task CreateVersionUsesStrongQuotedStampAfterExactHalTargetValidation()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid stamp = Guid.CreateVersion7();
        var input = new RegistrationFormVersionInput { LanguageTag = "en" };
        var link = new HalLink { Href = $"/api/events/{eventId}/registration-forms/{formId}/versions", Method = "POST" };
        api.CreateRegistrationFormVersionAsync(eventId, formId, $"\"{stamp}\"", input, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Id = Guid.CreateVersion7(), Success = true });

        await service.CreateVersionAsync(eventId, formId, stamp, input, link);

        await api.Received(1).CreateRegistrationFormVersionAsync(
            eventId, formId, $"\"{stamp}\"", input, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateVersionWithMismatchedHrefDoesNotDispatch()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        var input = new RegistrationFormVersionInput { LanguageTag = "en" };
        var stale = new HalLink { Href = $"/api/events/{eventId}/registration-forms/{Guid.CreateVersion7()}/versions", Method = "POST" };

        await Assert.That(async () => await service.CreateVersionAsync(eventId, formId, Guid.CreateVersion7(), input, stale)).Throws<InvalidOperationException>();

        await api.DidNotReceiveWithAnyArgs().CreateRegistrationFormVersionAsync(default, default, default!, default!, cancellationToken: default);
    }

    [Test]
    public async Task InstantiateTemplateUsesAdvertisedTemplateRelationOnly()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        Guid templateId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        var input = new InstantiateRegistrationFormTemplateInputDto
        {
            EventId = Guid.CreateVersion7(),
            WorkflowId = Guid.CreateVersion7(),
            Namespace = "event",
            Key = "attendee",
            Name = "Attendee",
            ExpectedWorkflowConcurrencyStamp = Guid.CreateVersion7()
        };
        var link = new HalLink { Href = $"/api/registration-form-templates/{templateId}/instantiate", Method = "POST" };
        api.InstantiateRegistrationFormTemplateAsync(templateId, input, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = formId });

        Guid created = await service.InstantiateTemplateAsync(templateId, input, link);

        await Assert.That(created).IsEqualTo(formId);
        await api.Received(1).InstantiateRegistrationFormTemplateAsync(templateId, input, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InstantiateTemplateWithMismatchedHrefDoesNotDispatch()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        var input = new InstantiateRegistrationFormTemplateInputDto { EventId = Guid.CreateVersion7(), WorkflowId = Guid.CreateVersion7(), Namespace = "event", Key = "attendee", Name = "Attendee", ExpectedWorkflowConcurrencyStamp = Guid.CreateVersion7() };
        var stale = new HalLink { Href = $"/api/registration-form-templates/{Guid.CreateVersion7()}/instantiate", Method = "POST" };

        await Assert.That(async () => await service.InstantiateTemplateAsync(Guid.CreateVersion7(), input, stale)).Throws<InvalidOperationException>();

        await api.DidNotReceiveWithAnyArgs().InstantiateRegistrationFormTemplateAsync(default, default!, cancellationToken: default);
    }

    [Test]
    public async Task ReorderSectionsDispatchesOneAtomicPutWithAuthoritativeOrder()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        Guid stamp = Guid.CreateVersion7();
        Guid[] order = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        var link = new HalLink
        {
            Href = $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/reorder",
            Method = "PUT"
        };
        var authoritative = new HalResourceOfRegistrationFormVersionDto { Id = versionId };
        api.ReorderRegistrationFormSectionsAsync(eventId, formId, versionId, $"\"{stamp}\"",
                Arg.Any<RegistrationFormReorderInput>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(authoritative);

        HalResourceOfRegistrationFormVersionDto result = await service.ReorderSectionsAsync(
            eventId, formId, versionId, stamp, order, link);

        await Assert.That(result).IsSameReferenceAs(authoritative);
        await api.Received(1).ReorderRegistrationFormSectionsAsync(eventId, formId, versionId, $"\"{stamp}\"",
            Arg.Is<RegistrationFormReorderInput>(input => input.OrderedIds.SequenceEqual(order)),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SectionAndFieldCrudUseExactRelationsStrongEtagsAndCancellation()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        Guid sectionId = Guid.CreateVersion7();
        Guid fieldId = Guid.CreateVersion7();
        Guid sectionStamp = Guid.CreateVersion7();
        Guid fieldStamp = Guid.CreateVersion7();
        using var cancellation = new CancellationTokenSource();
        string root = $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}";
        var sectionInput = new RegistrationFormSectionInput { Ordinal = 1, Title = "Contact" };
        var fieldInput = new RegistrationFormFieldCreateInput
        {
            Ordinal = 1,
            Namespace = "event",
            Key = "name",
            Label = "Name",
            FieldTypeId = 1,
            RetentionPolicyId = 1,
            OrganizerVisibilityId = 2
        };
        var field = new RegistrationFormFieldDto { Id = fieldId, ConcurrencyStamp = fieldStamp };
        api.AddRegistrationFormFieldAsync(eventId, formId, versionId, sectionId, $"\"{sectionStamp}\"", fieldInput,
                cancellationToken: cancellation.Token)
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = fieldId });

        Guid created = await service.AddFieldAsync(eventId, formId, versionId, sectionId, sectionStamp, fieldInput,
            Link("POST", $"{root}/fields"), cancellation.Token);
        await service.DeleteFieldAsync(eventId, formId, versionId, sectionId, field,
            Link("DELETE", $"{root}/fields/{fieldId}"), cancellation.Token);
        await service.DeleteSectionAsync(eventId, formId, versionId, sectionId, sectionStamp,
            Link("DELETE", root), cancellation.Token);

        await Assert.That(created).IsEqualTo(fieldId);
        await api.Received(1).AddRegistrationFormFieldAsync(eventId, formId, versionId, sectionId,
            $"\"{sectionStamp}\"", fieldInput, cancellationToken: cancellation.Token);
        await api.Received(1).DeleteRegistrationFormFieldAsync(eventId, formId, versionId, sectionId, fieldId,
            $"\"{fieldStamp}\"", cancellationToken: cancellation.Token);
        await api.Received(1).DeleteRegistrationFormSectionAsync(eventId, formId, versionId, sectionId,
            $"\"{sectionStamp}\"", cancellationToken: cancellation.Token);
    }

    [Test]
    public async Task OptionCrudUsesOnlyExactItemRelations()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        Guid sectionId = Guid.CreateVersion7();
        Guid fieldId = Guid.CreateVersion7();
        Guid fieldStamp = Guid.CreateVersion7();
        Guid optionId = Guid.CreateVersion7();
        Guid optionStamp = Guid.CreateVersion7();
        string root = $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{fieldId}/options";
        var field = new RegistrationFormFieldDto { Id = fieldId, ConcurrencyStamp = fieldStamp };
        var option = new RegistrationFormFieldOptionDto { Id = optionId, ConcurrencyStamp = optionStamp };
        var input = new RegistrationFormOptionInput { Ordinal = 1, Key = "general", Label = "General" };
        api.AddRegistrationFormFieldOptionAsync(eventId, formId, versionId, sectionId, fieldId, $"\"{fieldStamp}\"", input,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = optionId });

        Guid created = await service.AddOptionAsync(eventId, formId, versionId, sectionId, field, input, Link("POST", root));
        await service.UpdateOptionAsync(eventId, formId, versionId, sectionId, fieldId, option, input,
            Link("PATCH", $"{root}/{optionId}"));
        await service.RetireOptionAsync(eventId, formId, versionId, sectionId, fieldId, option,
            Link("DELETE", $"{root}/{optionId}"));

        await Assert.That(created).IsEqualTo(optionId);
        await api.Received(1).UpdateRegistrationFormFieldOptionAsync(eventId, formId, versionId, sectionId, fieldId,
            optionId, $"\"{optionStamp}\"", input, cancellationToken: Arg.Any<CancellationToken>());
        await api.Received(1).RetireRegistrationFormFieldOptionAsync(eventId, formId, versionId, sectionId, fieldId,
            optionId, $"\"{optionStamp}\"", cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RuleCrudUsesOnlyExactVersionAndItemRelations()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        Guid versionStamp = Guid.CreateVersion7();
        Guid ruleId = Guid.CreateVersion7();
        Guid ruleStamp = Guid.CreateVersion7();
        string root = $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/rules";
        var input = new RegistrationFormRuleInput
        {
            Ordinal = 1,
            TargetNamespace = "event",
            TargetKey = "email",
            Effect = 1,
            Condition = new RegistrationFormConditionInputDto { Operator = "exists", FieldNamespace = "event", FieldKey = "name" }
        };
        var rule = new RegistrationFormRuleDto { Id = ruleId, ConcurrencyStamp = ruleStamp };
        api.AddRegistrationFormRuleAsync(eventId, formId, versionId, $"\"{versionStamp}\"", input,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = ruleId });

        Guid created = await service.AddRuleAsync(eventId, formId, versionId, versionStamp, input, Link("POST", root));
        await service.UpdateRuleAsync(eventId, formId, versionId, rule, input, Link("PATCH", $"{root}/{ruleId}"));
        await service.DeleteRuleAsync(eventId, formId, versionId, rule, Link("DELETE", $"{root}/{ruleId}"));

        await Assert.That(created).IsEqualTo(ruleId);
        await api.Received(1).UpdateRegistrationFormRuleAsync(eventId, formId, versionId, ruleId,
            $"\"{ruleStamp}\"", input, cancellationToken: Arg.Any<CancellationToken>());
        await api.Received(1).DeleteRegistrationFormRuleAsync(eventId, formId, versionId, ruleId,
            $"\"{ruleStamp}\"", cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MismatchedNestedRelationNeverDispatches()
    {
        IRegistrationFormsClient api = Substitute.For<IRegistrationFormsClient>();
        var service = new RegistrationFormAuthoringService(api);
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        Guid sectionId = Guid.CreateVersion7();
        var input = new RegistrationFormFieldCreateInput
        {
            Ordinal = 1,
            Namespace = "event",
            Key = "name",
            Label = "Name",
            FieldTypeId = 1,
            RetentionPolicyId = 1,
            OrganizerVisibilityId = 2
        };

        await Assert.That(async () => await service.AddFieldAsync(eventId, formId, versionId, sectionId,
            Guid.CreateVersion7(), input, Link("POST", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{Guid.CreateVersion7()}/fields")))
            .Throws<InvalidOperationException>();

        await api.DidNotReceiveWithAnyArgs().AddRegistrationFormFieldAsync(default, default, default, default, default!, default!, cancellationToken: default);
    }

    private static HalLink Link(string method, string href) => new() { Method = method, Href = href };
}
