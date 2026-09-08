using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationFormTemplateTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Create_PinsPublishedSourceVersionWithoutCopyingMutableTemplateGraph()
    {
        RegistrationFormVersion source = PublishedSource();

        RegistrationFormTemplate template = RegistrationFormTemplate.Create(
            source.TenantId,
            " Template ",
            " Description ",
            " Registration ",
            " starter_pack ",
            source,
            Now.AddMinutes(1));

        await Assert.That(template.TenantId).IsEqualTo(source.TenantId);
        await Assert.That(template.IsPlatformOwned).IsFalse();
        await Assert.That(template.Name).IsEqualTo("Template");
        await Assert.That(template.PackKey).IsEqualTo("starter_pack");
        await Assert.That(template.SourceEventId).IsEqualTo(source.EventId);
        await Assert.That(template.SourceRegistrationFormId).IsEqualTo(source.RegistrationFormId);
        await Assert.That(template.SourceRegistrationFormVersionId).IsEqualTo(source.Id);
        await Assert.That(() => RegistrationFormTemplate.Create(
                source.TenantId, "Draft", "Description", "Registration", null, DraftSource(), Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CloneToTemplateInstance_RequiresPublishedSourceAndCreatesIndependentProvenancePinnedDraft()
    {
        RegistrationFormVersion source = PublishedSource();
        RegistrationForm target = RegistrationForm.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            "tenant.registration", "clone", "Clone", Now.AddMinutes(2));

        RegistrationFormVersion clone = source.CloneToTemplateInstance(
            target,
            Now.AddMinutes(3),
            source.RegistrationFormId,
            source.Id);

        await Assert.That(clone.Id).IsNotEqualTo(source.Id);
        await Assert.That(clone.TenantId).IsEqualTo(target.TenantId);
        await Assert.That(clone.EventId).IsEqualTo(target.EventId);
        await Assert.That(clone.RegistrationFormId).IsEqualTo(target.Id);
        await Assert.That(clone.StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Draft);
        await Assert.That(clone.SourceKindId).IsEqualTo((int)RegistrationFormVersionSourceKindEnum.TemplateClone);
        await Assert.That(clone.SourceTemplateFormId).IsEqualTo(source.RegistrationFormId);
        await Assert.That(clone.SourceTemplateVersionId).IsEqualTo(source.Id);

        RegistrationFormSection sourceSection = source.Sections.Single();
        RegistrationFormSection cloneSection = clone.Sections.Single();
        RegistrationFormField sourceField = sourceSection.Fields.Single();
        RegistrationFormField cloneField = cloneSection.Fields.Single();
        await Assert.That(cloneSection.Id).IsNotEqualTo(sourceSection.Id);
        await Assert.That(cloneField.Id).IsNotEqualTo(sourceField.Id);
        await Assert.That(cloneField.RegistrationFormId).IsEqualTo(target.Id);
        await Assert.That(cloneField.IsExportable).IsTrue();
        await Assert.That(cloneField.ExportPurposeCode).IsEqualTo("REGISTRATION.CONTACT");

        clone.UpdateFieldDetails(cloneField, 1, "Changed later");

        await Assert.That(sourceField.Label).IsEqualTo("Email");
        await Assert.That(cloneField.Label).IsEqualTo("Changed later");
        await Assert.That(() => DraftSource().CloneToTemplateInstance(
                target, Now.AddMinutes(4), source.RegistrationFormId, source.Id))
            .Throws<InvalidOperationException>();
    }

    private static RegistrationFormVersion PublishedSource()
    {
        RegistrationForm form = RegistrationForm.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            "platform.registration", "source", "Source", Now);
        RegistrationFormVersion version = DraftSource(form);
        version.PinGeneratedSchemaBundle(SchemaBundle(version), Now.AddMinutes(1));
        return version;
    }

    private static RegistrationFormVersion DraftSource()
    {
        RegistrationForm form = RegistrationForm.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            "platform.registration", "source", "Source", Now);
        return DraftSource(form);
    }

    private static RegistrationFormVersion DraftSource(RegistrationForm form)
    {
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", Now);
        RegistrationFormField field = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1,
            "person", "email", "Email", RegistrationFieldTypeEnum.Email, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, true,
            "registration.contact", false, false, Now);
        version.AddSection(section);
        version.AddField(section, field);
        version.AddOption(field, RegistrationFormFieldOption.Create(Guid.CreateVersion7(), field, 1, "primary", "Primary", Now));
        form.AddVersion(version);
        return version;
    }

    private static string SchemaBundle(RegistrationFormVersion version) =>
        $"{{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"versionId\":\"{version.Id:D}\",\"version\":{version.Version},\"languageTag\":\"{version.LanguageTag}\",\"data\":{{\"type\":\"object\"}},\"ui\":{{\"sections\":[]}},\"logic\":{{\"rules\":[]}},\"mapping\":{{\"fields\":[],\"options\":[]}}}}";
}
