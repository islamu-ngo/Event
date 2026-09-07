using System.Reflection;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Application.UnitTests.Features.RegistrationForms;

public sealed class RegistrationFormPublishPreflightServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Check_RejectsIncompleteOptionsConsentAndBrokenReferences()
    {
        RegistrationFormVersion version = BuildVersion();
        RegistrationFormSection section = version.Sections.Single();
        RegistrationFormField source = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1,
            "platform.registration", "source", "Source", RegistrationFieldTypeEnum.ShortText, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, Now);
        RegistrationFormField choice = RegistrationFormField.Create(Guid.CreateVersion7(), section, 2,
            "platform.registration", "choice", "Choice", RegistrationFieldTypeEnum.SingleChoice, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, Now);
        RegistrationFormField consent = RegistrationFormField.Create(Guid.CreateVersion7(), section, 3,
            "platform.registration", "consent", "Consent", RegistrationFieldTypeEnum.Consent, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, true, false, Now, "TERMS", "v1",
            "I agree to the terms.");
        version.AddField(section, source);
        version.AddField(section, choice);
        version.AddField(section, consent);
        Set(consent, nameof(RegistrationFormField.ConsentPurposeCode), null);
        Set(consent, nameof(RegistrationFormField.ConsentText), null);
        Set(consent, nameof(RegistrationFormField.ConsentTextVersion), null);

        RegistrationFormRule forward = RegistrationFormRule.Create(Guid.CreateVersion7(), version, 1,
            new FormFieldReference(source.Namespace, source.Key), RegistrationFormRuleEffect.Show,
            new FormCondition.ExistsCondition(new FormFieldReference(choice.Namespace, choice.Key)), Now);
        FieldInfo rules = typeof(RegistrationFormVersion).GetField("_rules", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((List<RegistrationFormRule>)rules.GetValue(version)!).Add(forward);

        string[] codes = [.. new RegistrationFormPublishPreflightService().Check(version).Issues.Select(issue => issue.Code)];

        await Assert.That(codes).Contains("field.options_incomplete");
        await Assert.That(codes).Contains("field.consent_incomplete");
        await Assert.That(codes).Contains("rule.reference_forward");
    }

    [Test]
    public async Task Check_AcceptsCompleteResolvableGraph()
    {
        RegistrationFormVersion version = BuildVersion();
        RegistrationFormSection section = version.Sections.Single();
        RegistrationFormField source = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1,
            "platform.registration", "source", "Source", RegistrationFieldTypeEnum.ShortText, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, Now);
        RegistrationFormField consent = RegistrationFormField.Create(Guid.CreateVersion7(), section, 2,
            "platform.registration", "consent", "Consent", RegistrationFieldTypeEnum.Consent, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, true, false, Now, "TERMS", "v1",
            "I agree to the terms.");
        version.AddField(section, source);
        version.AddField(section, consent);
        version.AddRule(RegistrationFormRule.Create(Guid.CreateVersion7(), version, 1,
            new FormFieldReference(consent.Namespace, consent.Key), RegistrationFormRuleEffect.Show,
            new FormCondition.ExistsCondition(new FormFieldReference(source.Namespace, source.Key)), Now));

        var result = new RegistrationFormPublishPreflightService().Check(version);

        await Assert.That(result.CanPublish).IsTrue();
        await Assert.That(result.Issues).IsEmpty();
    }

    private static RegistrationFormVersion BuildVersion()
    {
        RegistrationForm form = RegistrationForm.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), "platform.registration", "attendee", "Attendee", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(Guid.CreateVersion7(), form, 1, "en-US", null, null, Now);
        version.AddSection(RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", Now));
        return version;
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);
}
