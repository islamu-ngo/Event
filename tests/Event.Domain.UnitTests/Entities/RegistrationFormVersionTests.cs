using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationFormVersionTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Id(1);
    private static readonly Guid EventId = Id(2);

    [Test]
    public async Task PublishedVersion_RejectsEveryGraphMutation_AndCanRetire()
    {
        RegistrationFormVersion version = DraftWithGraph();
        RegistrationFormSection section = version.Sections.Single();
        RegistrationFormField field = section.Fields.Single();
        version.PinGeneratedSchemaBundle(SchemaBundle(version), Now.AddHours(1));

        await Assert.That(() => version.AddSection(RegistrationFormSection.Create(Id(40), version, 2, "Other", Now)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.RenameSection(section, "Changed"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.AddField(section, Field(version, section, Id(41), 2, "phone")))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.UpdateFieldGovernance(
            field, 2, RegistrationOrganizerVisibilityEnum.Hidden, false, false))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.UpdateFieldValidation(
            field, true, false, 1, 200, null, null, null, null, null, null))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.UpdateFieldDetails(field, 2, "Changed"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.AddOption(
            field, RegistrationFormFieldOption.Create(Id(42), field, 2, "other", "Other", Now)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.UpdateFieldOption(field, field.Options.Single(), 2, "changed", "Changed"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.RemoveField(field, Now.AddHours(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => version.RetireOption(field, field.Options.Single(), Now.AddHours(2)))
            .Throws<InvalidOperationException>();

        version.Retire(Now.AddHours(3));
        await Assert.That(version.StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Retired);
        await Assert.That(version.RetiredAt).IsEqualTo(Now.AddHours(3));
    }

    [Test]
    public async Task PinGeneratedSchemaBundle_WithIncompleteBundle_LeavesDraftArtifactsUnchanged()
    {
        RegistrationFormVersion version = DraftWithGraph();
        string incompleteBundle =
            $"{{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"versionId\":\"{version.Id:D}\",\"version\":{version.Version},\"languageTag\":\"{version.LanguageTag}\",\"data\":{{\"type\":\"object\"}},\"ui\":{{\"sections\":[]}}}}";

        await Assert.That(() => version.PinGeneratedSchemaBundle(incompleteBundle, Now.AddHours(1))).Throws<ArgumentException>();
        await AssertDraftAndUnpinned(version);
    }

    [Test]
    public async Task PinGeneratedSchemaBundle_WithMalformedOrMismatchedBundle_LeavesDraftArtifactsUnchanged()
    {
        RegistrationFormVersion malformed = DraftWithGraph();
        await Assert.That(() => malformed.PinGeneratedSchemaBundle("{", Now.AddHours(1))).Throws<JsonException>();
        await AssertDraftAndUnpinned(malformed);

        RegistrationFormVersion mismatched = DraftWithGraph();
        string mismatchedBundle = SchemaBundle(mismatched).Replace(
            mismatched.Id.ToString("D"), Guid.CreateVersion7().ToString("D"), StringComparison.Ordinal);
        await Assert.That(() => mismatched.PinGeneratedSchemaBundle(mismatchedBundle, Now.AddHours(1)))
            .Throws<ArgumentException>();
        await AssertDraftAndUnpinned(mismatched);
    }

    [Test]
    public async Task CloneToDraft_CopiesContentWithNewGraphIds_AndPreservesStableIdentityProvenanceAndLanguage()
    {
        RegistrationFormVersion published = DraftWithGraph();
        RegistrationFormSection sourceSection = published.Sections.Single();
        RegistrationFormField sourceField = sourceSection.Fields.Single();
        RegistrationFormFieldOption sourceOption = sourceField.Options.Single();
        published.PinGeneratedSchemaBundle(SchemaBundle(published), Now.AddHours(1));

        RegistrationFormVersion clone = published.CloneToDraft(2, Now.AddHours(2));
        RegistrationFormSection clonedSection = clone.Sections.Single();
        RegistrationFormField clonedField = clonedSection.Fields.Single();
        RegistrationFormFieldOption clonedOption = clonedField.Options.Single();

        await Assert.That(clone.Id).IsNotEqualTo(published.Id);
        await Assert.That(clonedSection.Id).IsNotEqualTo(sourceSection.Id);
        await Assert.That(clonedField.Id).IsNotEqualTo(sourceField.Id);
        await Assert.That(clonedOption.Id).IsNotEqualTo(sourceOption.Id);
        await Assert.That((clonedField.Namespace, clonedField.Key)).IsEqualTo((sourceField.Namespace, sourceField.Key));
        await Assert.That(clonedOption.Key).IsEqualTo(sourceOption.Key);
        await Assert.That(clone.SourceTemplateFormId).IsEqualTo(published.SourceTemplateFormId);
        await Assert.That(clone.SourceTemplateVersionId).IsEqualTo(published.SourceTemplateVersionId);
        await Assert.That(clone.LanguageTag).IsEqualTo("ar-SA");
        await Assert.That(clone.StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Draft);

        clone.RenameSection(clonedSection, "Clone only");
        await Assert.That(sourceSection.Title).IsEqualTo("Details");
        await Assert.That(clonedSection.Title).IsEqualTo("Clone only");
    }

    [Test]
    public async Task PublishedVersionClone_PreservesExactConsentText()
    {
        RegistrationFormVersion published = Version();
        RegistrationFormSection section = RegistrationFormSection.Create(Id(50), published, 1, "Terms", Now);
        RegistrationFormField consent = RegistrationFormField.Create(
            Id(51), section, 1, "platform.registration", "event_terms", "Terms", RegistrationFieldTypeEnum.Consent,
            1, RegistrationOrganizerVisibilityEnum.Hidden, true, false, Now, "EVENT_TERMS", "v1",
            "I agree to the event terms and privacy notice.");
        published.AddSection(section);
        published.AddField(section, consent);
        published.PinGeneratedSchemaBundle(SchemaBundle(published), Now.AddHours(1));

        RegistrationFormVersion clone = published.CloneToDraft(2, Now.AddHours(2));

        await Assert.That(clone.Sections.Single().Fields.Single().ConsentText)
            .IsEqualTo("I agree to the event terms and privacy notice.");
    }

    [Test]
    public async Task Ordinals_MustBePositiveAndUniqueWithinEachOwner()
    {
        RegistrationFormVersion version = Version();
        RegistrationFormSection section = RegistrationFormSection.Create(Id(10), version, 1, "Details", Now);
        version.AddSection(section);
        RegistrationFormField field = Field(version, section, Id(20), 1, "email");
        version.AddField(section, field);
        version.AddOption(field, RegistrationFormFieldOption.Create(Id(30), field, 1, "yes", "Yes", Now));

        await Assert.That(() => RegistrationFormSection.Create(Id(11), version, 0, "Invalid", Now))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => version.AddSection(RegistrationFormSection.Create(Id(12), version, 1, "Duplicate", Now)))
            .Throws<ArgumentException>();
        await Assert.That(() => Field(version, section, Id(21), 0, "invalid"))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => version.AddField(section, Field(version, section, Id(22), 1, "phone")))
            .Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormFieldOption.Create(Id(31), field, 0, "no", "No", Now))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => version.AddOption(
            field, RegistrationFormFieldOption.Create(Id(32), field, 1, "maybe", "Maybe", Now)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task FieldCanonicalIdentity_MustBeUniqueAcrossSectionsWithinVersion()
    {
        RegistrationFormVersion version = Version();
        RegistrationFormSection firstSection = RegistrationFormSection.Create(Id(10), version, 1, "Details", Now);
        RegistrationFormSection secondSection = RegistrationFormSection.Create(Id(11), version, 2, "Preferences", Now);
        version.AddSection(firstSection);
        version.AddSection(secondSection);
        version.AddField(firstSection, Field(version, firstSection, Id(20), 1, "email"));

        RegistrationFormField duplicate = RegistrationFormField.Create(
            Id(21), secondSection, 1, " PLATFORM.REGISTRATION ", " EMAIL ", "Duplicate",
            RegistrationFieldTypeEnum.Email, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, Now);

        await Assert.That(() => version.AddField(secondSection, duplicate))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Governance_RejectsInvalidConsentAndRetentionCombinations()
    {
        RegistrationFormVersion version = Version();
        RegistrationFormSection section = RegistrationFormSection.Create(Id(10), version, 1, "Details", Now);

        await Assert.That(() => RegistrationFormField.Create(
            Id(20), section, 1, "platform.registration", "email", "Email",
            RegistrationFieldTypeEnum.Email, 0, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, Now)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(21), section, 1, "platform.registration", "consent", "Consent",
            RegistrationFieldTypeEnum.Consent, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(22), section, 1, "platform.registration", "restricted", "Restricted",
            RegistrationFieldTypeEnum.ShortText, 1, RegistrationOrganizerVisibilityEnum.Hidden,
            true, true, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(23), section, 1, "platform.registration", "consent", "Consent",
            RegistrationFieldTypeEnum.Consent, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            true, false, Now, "EVENT_TERMS", null)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(24), section, 1, "platform.registration", "consent-text", "Consent",
            RegistrationFieldTypeEnum.Consent, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            true, false, Now, "EVENT_TERMS", "v1")).Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(26), section, 1, "platform.registration", "email", "Email",
            RegistrationFieldTypeEnum.Email, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, Now, "EVENT_TERMS", "v1")).Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(27), section, 1, "platform.registration", "email", "Email",
            RegistrationFieldTypeEnum.Email, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, Now, "EVENT_TERMS", null)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(28), section, 1, "platform.registration", "email-analytics", "Email analytics",
            RegistrationFieldTypeEnum.Email, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, false, null, true, false, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(29), section, 1, "platform.registration", "consent-analytics", "Consent analytics",
            RegistrationFieldTypeEnum.Boolean, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            true, false, false, null, true, true, Now, "EVENT_TERMS", "v1", "I agree."))
            .Throws<ArgumentException>();

        RegistrationFormField valid = RegistrationFormField.Create(
            Id(25), section, 1, "platform.registration", "consent", "Consent",
            RegistrationFieldTypeEnum.Consent, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            true, false, Now, " event_terms ", " v1 ", " I agree to the event terms. ");
        await Assert.That(valid.RequiresExplicitConsent).IsTrue();
        await Assert.That(valid.IsProviderTransferAllowed).IsFalse();
        await Assert.That(valid.ConsentPurposeCode).IsEqualTo("EVENT_TERMS");
        await Assert.That(valid.ConsentTextVersion).IsEqualTo("v1");
        await Assert.That(valid.ConsentText).IsEqualTo("I agree to the event terms.");

        RegistrationFormField analytics = RegistrationFormField.Create(
            Id(30), section, 2, "platform.registration", "age", "Age",
            RegistrationFieldTypeEnum.Integer, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, false, null, true, true, Now);
        await Assert.That((analytics.IsAnalyticsRelevant, analytics.IsOperationallyFilterable)).IsEqualTo((true, true));

        version.AddSection(section);
        version.AddField(section, valid);
        version.AddField(section, analytics);
        await Assert.That(() => version.UpdateFieldGovernance(
            valid, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, true, false))
            .Throws<ArgumentException>();
        version.UpdateFieldGovernance(
            valid, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, true, false, " updated_terms ", " v2 ",
            " I agree to the updated terms. ");
        await Assert.That((valid.ConsentPurposeCode, valid.ConsentTextVersion, valid.ConsentText))
            .IsEqualTo(("UPDATED_TERMS", "v2", "I agree to the updated terms."));
    }

    [Test]
    public async Task DraftFieldAndOptionDetails_CanBeUpdatedWithNormalizedValues()
    {
        RegistrationFormVersion version = DraftWithGraph();
        RegistrationFormField field = version.Sections.Single().Fields.Single();
        RegistrationFormFieldOption option = field.Options.Single();

        version.UpdateFieldDetails(field, 2, " Updated question ");
        version.UpdateFieldOption(field, option, 2, " UPDATED_OPTION ", " Updated option ");

        await Assert.That((field.Ordinal, field.Label)).IsEqualTo((2, "Updated question"));
        await Assert.That((option.Ordinal, option.Key, option.Label))
            .IsEqualTo((2, "updated_option", "Updated option"));
        await Assert.That(() => version.UpdateFieldDetails(field, 0, "Question"))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => version.UpdateFieldOption(field, option, 1, " ", "Option"))
            .Throws<ArgumentException>();
        await Assert.That(() => version.RemoveField(field, DateTime.SpecifyKind(Now, DateTimeKind.Local)))
            .Throws<ArgumentException>();

        version.RemoveField(field, Now.AddHours(1));
        field.Remove(Now.AddHours(2));
        await Assert.That(field.IsDeleted).IsTrue();
        await Assert.That(field.DeletedAt).IsEqualTo(Now.AddHours(1));
    }

    [Test]
    public async Task IdentityAndLanguage_RejectMalformedValues_AndProviderQuestionIdentityIsAbsent()
    {
        RegistrationFormVersion version = Version();
        RegistrationFormSection section = RegistrationFormSection.Create(Id(10), version, 1, "Details", Now);

        await Assert.That(() => FormVersionRules.NormalizeLanguageTag(" ")).Throws<ArgumentException>();
        await Assert.That(() => FormVersionRules.NormalizeLanguageTag("not_a_tag")).Throws<ArgumentException>();
        await Assert.That(() => FormVersionRules.NormalizeLanguageTag("de")).Throws<ArgumentException>();
        await Assert.That(FormVersionRules.NormalizeLanguageTag(" ar-sa ")).IsEqualTo("ar-SA");
        await Assert.That(() => Field(version, section, Id(20), 1, " ")).Throws<ArgumentException>();
        await Assert.That(() => RegistrationFormField.Create(
            Id(21), section, 1, " ", "email", "Email", RegistrationFieldTypeEnum.Email, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, Now)).Throws<ArgumentException>();

        string[] canonicalMembers =
        [
            .. typeof(RegistrationForm).GetMembers().Select(member => member.Name),
            .. typeof(RegistrationFormVersion).GetMembers().Select(member => member.Name),
            .. typeof(RegistrationFormSection).GetMembers().Select(member => member.Name),
            .. typeof(RegistrationFormField).GetMembers().Select(member => member.Name),
            .. typeof(RegistrationFormFieldOption).GetMembers().Select(member => member.Name),
            .. new[]
            {
                typeof(RegistrationForm), typeof(RegistrationFormVersion), typeof(RegistrationFormSection),
                typeof(RegistrationFormField), typeof(RegistrationFormFieldOption)
            }.SelectMany(type => type.GetMethods())
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.Name ?? string.Empty)
        ];
        await Assert.That(canonicalMembers.Any(name => name.Contains("ProviderQuestion", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    private static RegistrationFormVersion DraftWithGraph()
    {
        RegistrationFormVersion version = RegistrationFormVersion.Create(
            Id(4), Form(), 1, "ar-SA", Id(90), Id(91), Now);
        RegistrationFormSection section = RegistrationFormSection.Create(Id(10), version, 1, "Details", Now);
        version.AddSection(section);
        RegistrationFormField field = Field(version, section, Id(20), 1, "email");
        version.AddField(section, field);
        version.AddOption(field, RegistrationFormFieldOption.Create(Id(30), field, 1, "primary", "Primary", Now));
        return version;
    }

    private static RegistrationFormVersion Version() => RegistrationFormVersion.Create(
        Id(4), Form(), 1, "en", null, null, Now);

    private static RegistrationForm Form() => RegistrationForm.Create(
        Id(3), TenantId, EventId, "platform.registration", "attendee", "Attendee form", Now);

    private static RegistrationFormField Field(
        RegistrationFormVersion version,
        RegistrationFormSection section,
        Guid id,
        int ordinal,
        string key) => RegistrationFormField.Create(
            id, section, ordinal, "platform.registration", key, "Question", RegistrationFieldTypeEnum.Email,
            1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, Now);

    private static Guid Id(int value) => Guid.Parse($"0198a2b0-0000-7000-8000-{value:000000000000}");

    private static string SchemaBundle(RegistrationFormVersion version) =>
        $"{{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"versionId\":\"{version.Id:D}\",\"version\":{version.Version},\"languageTag\":\"{version.LanguageTag}\",\"data\":{{\"type\":\"object\"}},\"ui\":{{\"sections\":[]}},\"logic\":{{\"rules\":[]}},\"mapping\":{{\"fields\":[],\"options\":[]}}}}";

    private static async Task AssertDraftAndUnpinned(RegistrationFormVersion version)
    {
        await Assert.That(version.StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Draft);
        await Assert.That(version.SchemaHash).IsNull();
        await Assert.That(version.DataSchemaArtifact).IsNull();
        await Assert.That(version.UiSchemaArtifact).IsNull();
        await Assert.That(version.LogicSchemaArtifact).IsNull();
        await Assert.That(version.MappingArtifact).IsNull();
        await Assert.That(version.PublishedAt).IsNull();
    }
}
