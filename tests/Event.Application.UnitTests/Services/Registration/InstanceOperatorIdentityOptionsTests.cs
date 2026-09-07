namespace Event.Application.UnitTests.Services.Registration;

using Explore.Application;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

public sealed class InstanceOperatorIdentityOptionsTests
{
    [Test]
    public async Task ValidatorAcceptsCompleteGeneralOperatorIdentity()
    {
        var validator = new InstanceOperatorIdentityOptionsValidator();

        ValidateOptionsResult result = validator.Validate(null, Complete());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidatorRejectsIncompleteIdentityWithPayloadFreeFieldCodes()
    {
        var validator = new InstanceOperatorIdentityOptionsValidator();
        var options = Complete();
        options.PublicName = "Private Operator Name";
        options.LegalNoticeUrl = "http://private.example.test/legal";
        options.PublicContactEmail = string.Empty;

        ValidateOptionsResult result = validator.Validate(null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).IsNotNull();
        await Assert.That(result.Failures!)
            .IsEquivalentTo(
            [
                "instance_operator_identity_public_contact_email_missing",
                "instance_operator_identity_legal_notice_url_invalid"
            ]);
        await Assert.That(result.Failures!)
            .All(failure => !failure.Contains("Private Operator Name", StringComparison.Ordinal)
                && !failure.Contains("private.example.test", StringComparison.Ordinal));
    }

    [Test]
    public async Task ApplicationRegistrationBindsIdentityAndValidatesItAtStartup()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(CompleteConfiguration());
        builder.Services.ConfigureApplicationServices(builder.Configuration);
        using IHost host = builder.Build();

        host.Services.GetRequiredService<IStartupValidator>().Validate();
        IInstanceOperatorIdentity identity =
            host.Services.GetRequiredService<IInstanceOperatorIdentity>();

        await Assert.That(identity.PublicName).IsEqualTo("Independent Operator");
        await Assert.That(identity.LegalName).IsEqualTo("Independent Operator ASBL");
        await Assert.That(identity.JurisdictionCountryCode).IsEqualTo("BE");
    }

    private static InstanceOperatorIdentityOptions Complete() => new()
    {
        OperatorId = Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8"),
        PublicName = "Independent Operator",
        LegalName = "Independent Operator ASBL",
        IsOfficialInstance = false,
        OfficialOrigin = "https://example.test",
        OperatorKindCode = "registered_organization",
        JurisdictionCountryCode = "BE",
        RegistrationIdentifier = "BE 0123.456.789",
        PublicContactEmail = "contact@example.test",
        WebsiteUrl = "https://example.test",
        LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms",
        PrivacyUrl = "https://example.test/privacy"
    };

    private static Dictionary<string, string?> CompleteConfiguration() => new(StringComparer.Ordinal)
    {
        [$"{InstanceOperatorIdentityOptions.SectionName}:OperatorId"] =
            "0198e2a4-5340-7f89-8abc-b8bdf43e0ea8",
        [$"{InstanceOperatorIdentityOptions.SectionName}:PublicName"] = "Independent Operator",
        [$"{InstanceOperatorIdentityOptions.SectionName}:LegalName"] = "Independent Operator ASBL",
        [$"{InstanceOperatorIdentityOptions.SectionName}:IsOfficialInstance"] = "false",
        [$"{InstanceOperatorIdentityOptions.SectionName}:OfficialOrigin"] = "https://example.test",
        [$"{InstanceOperatorIdentityOptions.SectionName}:OperatorKindCode"] =
            "registered_organization",
        [$"{InstanceOperatorIdentityOptions.SectionName}:JurisdictionCountryCode"] = "BE",
        [$"{InstanceOperatorIdentityOptions.SectionName}:RegistrationIdentifier"] =
            "BE 0123.456.789",
        [$"{InstanceOperatorIdentityOptions.SectionName}:PublicContactEmail"] =
            "contact@example.test",
        [$"{InstanceOperatorIdentityOptions.SectionName}:WebsiteUrl"] = "https://example.test",
        [$"{InstanceOperatorIdentityOptions.SectionName}:LegalNoticeUrl"] =
            "https://example.test/legal",
        [$"{InstanceOperatorIdentityOptions.SectionName}:TermsUrl"] =
            "https://example.test/terms",
        [$"{InstanceOperatorIdentityOptions.SectionName}:PrivacyUrl"] =
            "https://example.test/privacy"
    };
}
