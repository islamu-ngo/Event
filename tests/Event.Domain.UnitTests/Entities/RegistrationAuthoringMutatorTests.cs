using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationAuthoringMutatorTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Workflow_RoutesRequirementUpdatesAndRemoval_WithoutBreakingOrdinals()
    {
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(Id(1), Id(2), Id(3), "attendee", Now);
        RegistrationRequirement first = Required(workflow, 1);
        RegistrationRequirement second = Required(workflow, 2);
        workflow.AddRequirement(first);
        workflow.AddRequirement(second);
        Guid observedStamp = workflow.ConcurrencyStamp;

        workflow.UpdatePurpose(" volunteer ");
        await Assert.That(workflow.ConcurrencyStamp).IsNotEqualTo(observedStamp);
        workflow.UpdateRequirement(first, 3, RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.MIRROR_ONLY, RegistrationRequirementSubjectTypeEnum.LeadBookerOnly, null);

        await Assert.That(workflow.Purpose).IsEqualTo("volunteer");
        await Assert.That(first.Ordinal).IsEqualTo(3);
        await Assert.That(first.CriticalityId).IsEqualTo((int)RegistrationRequirementCriticalityEnum.Optional);
        await Assert.That(() => workflow.UpdateRequirement(first, 2,
            RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.MIRROR_ONLY, RegistrationRequirementSubjectTypeEnum.LeadBookerOnly, null))
            .Throws<ArgumentException>();

        workflow.RemoveRequirement(second, Now.AddMinutes(1));
        await Assert.That(second.IsDeleted).IsTrue();
        await Assert.That(second.DeletedAt).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task DraftVersion_RoutesSectionFieldOptionAndRuleUpdates()
    {
        FormGraph graph = Graph();
        RegistrationFormFieldOption option = RegistrationFormFieldOption.Create(
            Id(30), graph.First, 1, "yes", "Yes", Now);
        graph.Version.AddOption(graph.First, option);
        RegistrationFormRule rule = RegistrationFormRule.Create(Id(40), graph.Version, 1,
            Reference(graph.Second), RegistrationFormRuleEffect.Show,
            new FormCondition.ExistsCondition(Reference(graph.First)), Now);
        graph.Version.AddRule(rule);

        graph.Version.UpdateSection(graph.FirstSection, 1, " Contact ");
        graph.Version.UpdateFieldDetails(graph.First, 2, " Email address ");
        graph.Version.UpdateFieldOption(graph.First, option, 2, "affirmative", " Yes, please ");
        graph.Version.UpdateRule(rule, 2, Reference(graph.Second), RegistrationFormRuleEffect.Require,
            new FormCondition.ExistsCondition(Reference(graph.First)));

        await Assert.That((graph.FirstSection.Ordinal, graph.FirstSection.Title)).IsEqualTo((1, "Contact"));
        await Assert.That((graph.First.Ordinal, graph.First.Label)).IsEqualTo((2, "Email address"));
        await Assert.That((option.Ordinal, option.Key, option.Label)).IsEqualTo((2, "affirmative", "Yes, please"));
        await Assert.That((rule.Ordinal, rule.Effect)).IsEqualTo((2, RegistrationFormRuleEffect.Require));
    }

    [Test]
    public async Task DraftVersion_RejectsDuplicateOrderingAndReferencedFieldRemoval()
    {
        FormGraph graph = Graph();
        RegistrationFormField sibling = Field(graph.FirstSection, 23, 2, "sibling");
        graph.Version.AddField(graph.FirstSection, sibling);
        RegistrationFormRule rule = RegistrationFormRule.Create(Id(40), graph.Version, 1,
            Reference(graph.Second), RegistrationFormRuleEffect.Show,
            new FormCondition.ExistsCondition(Reference(graph.First)), Now);
        graph.Version.AddRule(rule);

        await Assert.That(() => graph.Version.UpdateSection(graph.FirstSection, 3, "Invalid reorder"))
            .Throws<ArgumentException>();
        await Assert.That((graph.FirstSection.Ordinal, graph.FirstSection.Title)).IsEqualTo((1, "First"));
        await Assert.That(() => graph.Version.UpdateSection(graph.FirstSection, 2, "Duplicate ordinal"))
            .Throws<ArgumentException>();
        await Assert.That(() => graph.Version.UpdateFieldDetails(graph.First, 2, "Duplicate ordinal"))
            .Throws<ArgumentException>();
        await Assert.That(() => graph.Version.RemoveField(graph.First, Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.RemoveSection(graph.FirstSection, Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();

        graph.Version.RemoveRule(rule, Now.AddMinutes(2));
        graph.Version.RemoveField(graph.First, Now.AddMinutes(3));
        graph.Version.RemoveField(sibling, Now.AddMinutes(3));
        graph.Version.RemoveSection(graph.FirstSection, Now.AddMinutes(4));
        await Assert.That(graph.First.IsDeleted).IsTrue();
        await Assert.That(graph.FirstSection.IsDeleted).IsTrue();
    }

    [Test]
    public async Task PublishedVersion_RejectsNewAuthoringRoutes()
    {
        FormGraph graph = Graph();
        RegistrationFormFieldOption option = RegistrationFormFieldOption.Create(
            Id(30), graph.First, 1, "yes", "Yes", Now);
        graph.Version.AddOption(graph.First, option);
        RegistrationFormRule rule = RegistrationFormRule.Create(Id(40), graph.Version, 1,
            Reference(graph.Second), RegistrationFormRuleEffect.Show,
            new FormCondition.ExistsCondition(Reference(graph.First)), Now);
        graph.Version.AddRule(rule);
        graph.Version.PinGeneratedSchemaBundle(SchemaBundle(graph.Version), Now.AddMinutes(1));

        await Assert.That(() => graph.Version.UpdateSection(graph.FirstSection, 3, "Changed"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.UpdateFieldDetails(graph.First, 2, "Changed"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.UpdateFieldOption(graph.First, option, 2, "changed", "Changed"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.UpdateRule(rule, 2, Reference(graph.Second),
            RegistrationFormRuleEffect.Require, new FormCondition.ExistsCondition(Reference(graph.First))))
            .Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.RemoveRule(rule, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.RemoveField(graph.First, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.RemoveSection(graph.FirstSection, Now.AddMinutes(2)))
            .Throws<InvalidOperationException>();
    }

    private static RegistrationRequirement Required(RegistrationWorkflow workflow, int ordinal) =>
        RegistrationRequirement.Create(Id(10 + ordinal), workflow, ordinal,
            RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.MIRROR_ONLY, RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now);

    private static FormGraph Graph()
    {
        RegistrationForm form = RegistrationForm.Create(
            Id(1), Id(2), Id(3), "platform.registration", "authoring", "Authoring", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(Id(4), form, 1, "en", null, null, Now);
        RegistrationFormSection firstSection = RegistrationFormSection.Create(Id(10), version, 1, "First", Now);
        RegistrationFormSection secondSection = RegistrationFormSection.Create(Id(11), version, 2, "Second", Now);
        version.AddSection(firstSection);
        version.AddSection(secondSection);
        RegistrationFormField first = Field(firstSection, 21, 1, "first");
        RegistrationFormField second = Field(secondSection, 22, 1, "second");
        version.AddField(firstSection, first);
        version.AddField(secondSection, second);
        return new(version, firstSection, first, second);
    }

    private static RegistrationFormField Field(
        RegistrationFormSection section,
        int idSuffix,
        int ordinal,
        string key) =>
        RegistrationFormField.Create(Id(idSuffix), section, ordinal, "platform.registration", key, key,
            RegistrationFieldTypeEnum.ShortText, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, Now);

    private static FormFieldReference Reference(RegistrationFormField field) => new(field.Namespace, field.Key);

    private static string SchemaBundle(RegistrationFormVersion version) =>
        $"{{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"versionId\":\"{version.Id:D}\",\"version\":{version.Version},\"languageTag\":\"{version.LanguageTag}\",\"data\":{{}},\"ui\":{{}},\"logic\":{{}},\"mapping\":{{}}}}";

    private static Guid Id(int suffix) => Guid.Parse($"0198a2b0-0000-7000-8000-{suffix:D12}");

    private sealed record FormGraph(
        RegistrationFormVersion Version,
        RegistrationFormSection FirstSection,
        RegistrationFormField First,
        RegistrationFormField Second);
}
