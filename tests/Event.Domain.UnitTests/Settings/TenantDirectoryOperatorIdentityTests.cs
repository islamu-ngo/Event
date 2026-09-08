namespace Event.Domain.UnitTests.Settings;

using System.Text.Json;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;

public sealed class TenantDirectoryOperatorIdentityTests
{
    [Test]
    public async Task Evaluate_NormalizesCapabilityValidActivationIdentity()
    {
        TenantDirectoryOperatorIdentityReadiness readiness =
            TenantDirectoryOperatorIdentity.Evaluate(
                CompleteSettings() with
                {
                    PublicName = "  Community Events  ",
                    LegalName = "  Community Events ASBL  ",
                    OperatorKindCode = " REGISTERED_ORGANIZATION ",
                    JurisdictionCountryCode = " be ",
                    RegistrationIdentifier = "  BE 0123.456.789  ",
                    PublicContactEmail = "  CONTACT@EXAMPLE.TEST ",
                    LegalNoticeUrl = "HTTPS://EXAMPLE.TEST/legal",
                    PrivacyUrl = "https://EXAMPLE.TEST/privacy"
                },
                TenantDirectoryOperatorIdentityCapability.Activation);

        await Assert.That(readiness.IsReady).IsTrue();
        await Assert.That(readiness.ReasonCodes).IsEmpty();
        await Assert.That(readiness.Identity).IsNotNull();
        await Assert.That(readiness.Identity!.PublicName).IsEqualTo("Community Events");
        await Assert.That(readiness.Identity.LegalName).IsEqualTo("Community Events ASBL");
        await Assert.That(readiness.Identity.OperatorKindCode)
            .IsEqualTo(TenantDirectoryOperatorKinds.RegisteredOrganization);
        await Assert.That(readiness.Identity.JurisdictionCountryCode).IsEqualTo("BE");
        await Assert.That(readiness.Identity.RegistrationIdentifier).IsEqualTo("BE 0123.456.789");
        await Assert.That(readiness.Identity.PublicContactEmail).IsEqualTo("contact@example.test");
        await Assert.That(readiness.Identity.LegalNoticeUrl).IsEqualTo("https://example.test/legal");
        await Assert.That(readiness.Identity.PrivacyUrl).IsEqualTo("https://example.test/privacy");
        await Assert.That(readiness.Identity.TermsUrl).IsEqualTo("https://example.test/terms");
    }

    [Test]
    public async Task Evaluate_PaidCommerceRequiresTermsUrlButActivationDoesNot()
    {
        TenantDirectoryOperatorIdentitySettings settings = CompleteSettings() with { TermsUrl = null };

        TenantDirectoryOperatorIdentityReadiness activation =
            TenantDirectoryOperatorIdentity.Evaluate(
                settings,
                TenantDirectoryOperatorIdentityCapability.Activation);
        TenantDirectoryOperatorIdentityReadiness paid =
            TenantDirectoryOperatorIdentity.Evaluate(
                settings,
                TenantDirectoryOperatorIdentityCapability.PaidCommerce);

        await Assert.That(activation.IsReady).IsTrue();
        await Assert.That(paid.IsReady).IsFalse();
        await Assert.That(paid.Identity).IsNull();
        await Assert.That(paid.ReasonCodes)
            .IsEquivalentTo([TenantDirectoryOperatorIdentityReasonCodes.MissingTermsUrl]);
    }

    [Test]
    public async Task Evaluate_ReturnsDeterministicPayloadFreeReasonsForMalformedFields()
    {
        var settings = new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = null,
            LegalName = null,
            OperatorKindCode = "unknown",
            JurisdictionCountryCode = "BEL",
            RegistrationIdentifier = new string('x', 121),
            PublicContactEmail = "not-an-email",
            LegalNoticeUrl = "http://example.test/legal",
            TermsUrl = "https://user@example.test/terms",
            PrivacyUrl = "https://example.test/privacy#fragment"
        };

        TenantDirectoryOperatorIdentityReadiness readiness =
            TenantDirectoryOperatorIdentity.Evaluate(
                settings,
                TenantDirectoryOperatorIdentityCapability.PaidCommerce);

        string[] expected =
        [
            TenantDirectoryOperatorIdentityReasonCodes.MissingPublicName,
            TenantDirectoryOperatorIdentityReasonCodes.MissingLegalName,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidOperatorKind,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidJurisdictionCountry,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidRegistrationIdentifier,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidPublicContactEmail,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidLegalNoticeUrl,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidTermsUrl,
            TenantDirectoryOperatorIdentityReasonCodes.InvalidPrivacyUrl
        ];

        await Assert.That(readiness.IsReady).IsFalse();
        await Assert.That(readiness.Identity).IsNull();
        await Assert.That(readiness.ReasonCodes).IsEquivalentTo(expected);
        await Assert.That(readiness.ReasonCodes)
            .All(reason => !reason.Contains("example.test", StringComparison.Ordinal));
    }

    [Test]
    public async Task Defaults_SeedOnlyTrimmedPublicNameAndRegisterNonSecretDocumentKey()
    {
        Guid tenantId = Guid.CreateVersion7();
        var document = TenantDirectoryOperatorIdentityDocumentDefaults.Create(
            tenantId,
            "  Community Events  ");
        TenantDirectoryOperatorIdentitySettings? payload =
            JsonSerializer.Deserialize<TenantDirectoryOperatorIdentitySettings>(
                document.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(document.TenantId).IsEqualTo(tenantId);
        await Assert.That(document.DocumentKey)
            .IsEqualTo(SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity);
        await Assert.That(SettingsDocumentTaxonomy.IsNonSecretTenantDocument(document.DocumentKey)).IsTrue();
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.PublicName).IsEqualTo("Community Events");
        await Assert.That(payload.LegalName).IsNull();
        await Assert.That(payload.OperatorKindCode).IsNull();
        await Assert.That(payload.RegistrationIdentifier).IsNull();
    }

    private static TenantDirectoryOperatorIdentitySettings CompleteSettings() => new()
    {
        PublicName = "Community Events",
        LegalName = "Community Events ASBL",
        OperatorKindCode = TenantDirectoryOperatorKinds.RegisteredOrganization,
        JurisdictionCountryCode = "BE",
        RegistrationIdentifier = null,
        PublicContactEmail = "contact@example.test",
        LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms",
        PrivacyUrl = "https://example.test/privacy"
    };
}
