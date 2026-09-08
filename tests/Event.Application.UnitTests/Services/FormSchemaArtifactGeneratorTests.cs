using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Application.UnitTests.Services;

public sealed class FormSchemaArtifactGeneratorTests
{
    private const string GoldenHash = "6a00e655fa6f96c3269904be33f91b4d69cd6f2ccbe22c9ddb0ecb7609d21e7c";
    private static readonly DateTime Now = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly IFormSchemaArtifactGenerator Generator = new FormSchemaArtifactGenerator();

    [Test]
    public async Task Generate_ProducesFourDraft202012Artifacts_WithGoldenHash()
    {
        FormSchemaArtifactBundle artifacts = Generator.Generate(Build());

        await Assert.That(artifacts.DataSchemaJson).Contains("\"$schema\":\"https://json-schema.org/draft/2020-12/schema\"");
        await Assert.That(artifacts.DataSchemaJson).Contains("\"x-isExportable\":false");
        await Assert.That(artifacts.UiSchemaJson).Contains("\"sections\"");
        await Assert.That(artifacts.LogicSchemaJson).Contains("\"rules\"");
        await Assert.That(artifacts.MappingArtifactJson).IsEqualTo("{\"fields\":[],\"options\":[]}");
        await Assert.That(artifacts.SchemaHash).IsEqualTo(GoldenHash);
        await Assert.That(artifacts.SchemaHash).Matches("^[0-9a-f]{64}$");

        using JsonDocument bundle = JsonDocument.Parse(artifacts.CanonicalBundleJson);
        string[] artifactNames = [.. bundle.RootElement.EnumerateObject()
            .Where(property => property.Name is "data" or "ui" or "logic" or "mapping")
            .Select(property => property.Name)];
        await Assert.That(artifactNames).IsEquivalentTo(new[] { "data", "ui", "logic", "mapping" });
    }

    [Test]
    public async Task Generate_IsByteStableAcrossCultureConstructionOrderAndRepeatedCalls()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            FormSchemaArtifactBundle first = Generator.Generate(Build(reverseConstruction: false));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-BE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-BE");
            FormSchemaArtifactBundle second = Generator.Generate(Build(reverseConstruction: true));
            FormSchemaArtifactBundle repeated = Generator.Generate(Build(reverseConstruction: false));

            await Assert.That(second.CanonicalBundleJson).IsEqualTo(first.CanonicalBundleJson);
            await Assert.That(second.SchemaHash).IsEqualTo(first.SchemaHash);
            await Assert.That(repeated.CanonicalBundleJson).IsEqualTo(first.CanonicalBundleJson);
            await Assert.That(repeated.SchemaHash).IsEqualTo(first.SchemaHash);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task PublicationFacade_GeneratesAndPinsExactArtifacts_WithoutCallerSuppliedBundleOrHash()
    {
        RegistrationFormVersion version = Build();
        FormSchemaArtifactBundle artifacts = Generator.Generate(version);
        const string callerSuppliedBundle = "{\"data\":{},\"ui\":{},\"logic\":{},\"mapping\":{}}";

        new FormSchemaArtifactPublicationService(Generator).Publish(version, Now.AddHours(2));

        await Assert.That(version.DataSchemaArtifact).IsEqualTo(artifacts.DataSchemaJson);
        await Assert.That(version.UiSchemaArtifact).IsEqualTo(artifacts.UiSchemaJson);
        await Assert.That(version.LogicSchemaArtifact).IsEqualTo(artifacts.LogicSchemaJson);
        await Assert.That(version.MappingArtifact).IsEqualTo(artifacts.MappingArtifactJson);
        await Assert.That(version.SchemaHash).IsEqualTo(artifacts.SchemaHash);
        await Assert.That(version.DataSchemaArtifact).IsNotEqualTo(callerSuppliedBundle);
        await Assert.That(typeof(RegistrationFormVersion).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(method => method.Name == "Publish")).IsFalse();
        await Assert.That(typeof(FormSchemaArtifactPublicationService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "Publish")
            .SelectMany(method => method.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(string))).IsFalse();
    }

    [Test]
    [Arguments("language")]
    [Arguments("section-title")]
    [Arguments("section-ordinal")]
    [Arguments("field-identity")]
    [Arguments("field-label")]
    [Arguments("field-type")]
    [Arguments("field-retention-policy")]
    [Arguments("field-organizer-visibility")]
    [Arguments("field-explicit-consent")]
    [Arguments("consent-text")]
    [Arguments("field-provider-transfer")]
    [Arguments("field-required")]
    [Arguments("field-multi")]
    [Arguments("field-min-length")]
    [Arguments("field-max-length")]
    [Arguments("field-regex")]
    [Arguments("field-min-number")]
    [Arguments("field-max-number")]
    [Arguments("field-min-date-time")]
    [Arguments("field-max-date-time")]
    [Arguments("field-url-schemes")]
    [Arguments("field-ordinal")]
    [Arguments("option-key")]
    [Arguments("option-label")]
    [Arguments("option-ordinal")]
    [Arguments("option-retirement")]
    [Arguments("rule-target")]
    [Arguments("rule-effect")]
    [Arguments("rule-condition")]
    [Arguments("rule-ordinal")]
    public async Task Generate_HashChangesForEveryArtifactRelevantMutation(string mutation)
    {
        string baseline = Generator.Generate(Build()).SchemaHash;

        await Assert.That(Generator.Generate(Build(mutation: mutation)).SchemaHash).IsNotEqualTo(baseline);
    }

    [Test]
    public async Task Generate_ConsentPurposeCodeChangesCanonicalBundleAndHash()
    {
        FormSchemaArtifactBundle baseline = Generator.Generate(Build());
        FormSchemaArtifactBundle changed = Generator.Generate(Build(mutation: "consent-purpose-code"));

        await Assert.That(changed.CanonicalBundleJson).Contains("\"x-consentPurposeCode\":\"CONTACT_UPDATES\"");
        await Assert.That(changed.CanonicalBundleJson).IsNotEqualTo(baseline.CanonicalBundleJson);
        await Assert.That(changed.SchemaHash).IsNotEqualTo(baseline.SchemaHash);
    }

    [Test]
    public async Task Generate_ConsentTextVersionChangesCanonicalBundleAndHash()
    {
        FormSchemaArtifactBundle baseline = Generator.Generate(Build());
        FormSchemaArtifactBundle changed = Generator.Generate(Build(mutation: "consent-text-version"));

        await Assert.That(changed.CanonicalBundleJson).Contains("\"x-consentTextVersion\":\"v2\"");
        await Assert.That(changed.CanonicalBundleJson).IsNotEqualTo(baseline.CanonicalBundleJson);
        await Assert.That(changed.SchemaHash).IsNotEqualTo(baseline.SchemaHash);
    }

    [Test]
    public async Task Generate_ConsentTextIsPinnedInCanonicalBundleAndHash()
    {
        FormSchemaArtifactBundle baseline = Generator.Generate(Build());
        FormSchemaArtifactBundle changed = Generator.Generate(Build(mutation: "consent-text"));

        await Assert.That(changed.CanonicalBundleJson)
            .Contains("\"x-consentText\":\"I agree to the updated event terms.\"");
        await Assert.That(changed.SchemaHash).IsNotEqualTo(baseline.SchemaHash);
    }

    private static RegistrationFormVersion Build(bool reverseConstruction = false, string? mutation = null)
    {
        RegistrationForm form = RegistrationForm.Create(Id(1), Id(2), Id(3), "platform.registration", "attendee",
            "Attendee form", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(
            Id(4), form, 7, mutation == "language" ? "ar-SA" : "en-US", null, null, Now);
        RegistrationFormSection first = RegistrationFormSection.Create(
            Id(10), version, mutation == "section-ordinal" ? 2 : 1,
            mutation == "section-title" ? "Changed details" : "Details", Now);
        RegistrationFormSection second = RegistrationFormSection.Create(
            Id(11), version, mutation == "section-ordinal" ? 3 : 2, "Preferences", Now);
        foreach (RegistrationFormSection section in reverseConstruction ? new[] { second, first } : new[] { first, second })
        {
            version.AddSection(section);
        }

        RegistrationFormField email = RegistrationFormField.Create(
            Id(20), first, mutation == "field-ordinal" ? 2 : 1, "platform.registration",
            mutation == "field-identity" ? "email_changed" : "email",
            mutation == "field-label" ? "Changed email" : "Email",
            mutation == "field-type" ? RegistrationFieldTypeEnum.LongText : RegistrationFieldTypeEnum.ShortText,
            mutation == "field-retention-policy" ? 2 : 1,
            mutation == "field-organizer-visibility" ? RegistrationOrganizerVisibilityEnum.Hidden : RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            mutation == "field-explicit-consent", mutation == "field-provider-transfer", Now,
            mutation == "field-explicit-consent" ? "EVENT_TERMS" : null,
            mutation == "field-explicit-consent" ? "v1" : null,
            mutation == "field-explicit-consent" ? "I agree to the event terms." : null);
        RegistrationFormField age = RegistrationFormField.Create(
            Id(21), first, mutation == "field-ordinal" ? 1 : 2, "platform.registration", "age", "Age",
            RegistrationFieldTypeEnum.Decimal, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, Now);
        RegistrationFormField consent = RegistrationFormField.Create(
            Id(22), second, 1, "platform.registration", "consent", "Consent",
            RegistrationFieldTypeEnum.Consent, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            true, false, Now,
            mutation == "consent-purpose-code" ? " contact_updates " : "EVENT_TERMS",
            mutation == "consent-text-version" ? " v2 " : "v1",
            mutation == "consent-text" ? " I agree to the updated event terms. " : "I agree to the event terms.");
        foreach ((RegistrationFormSection section, RegistrationFormField field) pair in
                 reverseConstruction ? new[] { (first, age), (first, email), (second, consent) } :
                     new[] { (second, consent), (first, email), (first, age) })
        {
            version.AddField(pair.section, pair.field);
        }

        version.UpdateFieldValidation(
            email,
            mutation == "field-required",
            mutation == "field-multi",
            mutation == "field-min-length" ? 3 : 2,
            mutation == "field-max-length" ? 121 : 120,
            mutation == "field-regex" ? "^[a-z]+$" : null,
            mutation == "field-min-number" ? 1m : null,
            mutation == "field-max-number" ? 100m : null,
            mutation == "field-min-date-time" ? new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero) : null,
            mutation == "field-max-date-time" ? new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero) : null,
            mutation == "field-url-schemes" ? "https,http" : "https");

        RegistrationFormFieldOption primary = RegistrationFormFieldOption.Create(
            Id(30), email, mutation == "option-ordinal" ? 2 : 1,
            mutation == "option-key" ? "primary_changed" : "primary",
            mutation == "option-label" ? "Changed primary" : "Primary", Now);
        RegistrationFormFieldOption alternate = RegistrationFormFieldOption.Create(
            Id(31), email, mutation == "option-ordinal" ? 1 : 2, "alternate", "Alternate", Now);
        foreach (RegistrationFormFieldOption option in reverseConstruction ? new[] { alternate, primary } : new[] { primary, alternate })
        {
            version.AddOption(email, option);
        }

        if (mutation == "option-retirement")
        {
            version.RetireOption(email, alternate, Now.AddHours(1));
        }

        FormFieldReference emailReference = new(email.Namespace, email.Key);
        FormFieldReference ageReference = new(age.Namespace, age.Key);
        FormFieldReference consentReference = new(consent.Namespace, consent.Key);
        RegistrationFormRule firstRule = RegistrationFormRule.Create(
            Id(40), version, mutation == "rule-ordinal" ? 2 : 1,
            mutation == "rule-target" ? ageReference : consentReference,
            mutation == "rule-effect" ? RegistrationFormRuleEffect.Hide : RegistrationFormRuleEffect.Show,
            mutation == "rule-target"
                ? new FormCondition.ExistsCondition(emailReference)
                : mutation == "rule-condition"
                ? new FormCondition.NotCondition(new FormCondition.ExistsCondition(emailReference))
                : new FormCondition.AllCondition([
                    new FormCondition.ExistsCondition(emailReference),
                    new FormCondition.CompareCondition(ageReference, FormComparisonKind.GreaterThanOrEqual,
                        FormScalarValue.From(18m))]), Now);
        RegistrationFormRule secondRule = RegistrationFormRule.Create(
            Id(41), version, mutation == "rule-ordinal" ? 1 : 2, consentReference,
            RegistrationFormRuleEffect.Require, new FormCondition.ExistsCondition(emailReference), Now);
        foreach (RegistrationFormRule rule in reverseConstruction ? new[] { secondRule, firstRule } : new[] { firstRule, secondRule })
        {
            version.AddRule(rule);
        }

        return version;
    }

    private static Guid Id(int value) => Guid.Parse($"0198a2b0-0000-7000-8000-{value:000000000000}");
}
