using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Event.Api.IntegrationTests.Fixtures;

internal static class TestInstanceOperatorIdentityConfiguration
{
    public static void Apply(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Instance:OperatorIdentity:OperatorId"] =
                    "0198e2a4-5340-7f89-8abc-b8bdf43e0ea8",
                ["Instance:OperatorIdentity:PublicName"] =
                    "Test Instance Operator",
                ["Instance:OperatorIdentity:LegalName"] =
                    "Test Instance Operator ASBL",
                ["Instance:OperatorIdentity:IsOfficialInstance"] = "false",
                ["Instance:OperatorIdentity:OfficialOrigin"] =
                    "https://instance.example.test",
                ["Instance:OperatorIdentity:OperatorKindCode"] =
                    "registered_organization",
                ["Instance:OperatorIdentity:JurisdictionCountryCode"] = "BE",
                ["Instance:OperatorIdentity:RegistrationIdentifier"] =
                    "BE 0123.456.789",
                ["Instance:OperatorIdentity:PublicContactEmail"] =
                    "contact@instance.example.test",
                ["Instance:OperatorIdentity:WebsiteUrl"] =
                    "https://instance.example.test",
                ["Instance:OperatorIdentity:LegalNoticeUrl"] =
                    "https://instance.example.test/legal",
                ["Instance:OperatorIdentity:TermsUrl"] =
                    "https://instance.example.test/terms",
                ["Instance:OperatorIdentity:PrivacyUrl"] =
                    "https://instance.example.test/privacy"
            });
        });
    }
}
