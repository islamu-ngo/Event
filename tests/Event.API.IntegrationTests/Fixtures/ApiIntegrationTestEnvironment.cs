using TUnit.Core;

namespace Event.Api.IntegrationTests.Fixtures;

public static class ApiIntegrationTestEnvironment
{
    private static readonly IReadOnlyDictionary<string, string> Values =
        new Dictionary<string, string>
        {
            ["Instance__OperatorIdentity__OperatorId"] =
                "0198e2a4-5340-7f89-8abc-b8bdf43e0ea8",
            ["Instance__OperatorIdentity__PublicName"] =
                "Test Instance Operator",
            ["Instance__OperatorIdentity__LegalName"] =
                "Test Instance Operator ASBL",
            ["Instance__OperatorIdentity__IsOfficialInstance"] = "false",
            ["Instance__OperatorIdentity__OfficialOrigin"] =
                "https://instance.example.test",
            ["Instance__OperatorIdentity__OperatorKindCode"] =
                "registered_organization",
            ["Instance__OperatorIdentity__JurisdictionCountryCode"] = "BE",
            ["Instance__OperatorIdentity__RegistrationIdentifier"] =
                "BE 0123.456.789",
            ["Instance__OperatorIdentity__PublicContactEmail"] =
                "contact@instance.example.test",
            ["Instance__OperatorIdentity__WebsiteUrl"] =
                "https://instance.example.test",
            ["Instance__OperatorIdentity__LegalNoticeUrl"] =
                "https://instance.example.test/legal",
            ["Instance__OperatorIdentity__TermsUrl"] =
                "https://instance.example.test/terms",
            ["Instance__OperatorIdentity__PrivacyUrl"] =
                "https://instance.example.test/privacy"
        };

    private static readonly Dictionary<string, string?> PreviousValues = [];

    [Before(TestSession)]
    public static void Configure()
    {
        foreach ((string key, string value) in Values)
        {
            PreviousValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    [After(TestSession)]
    public static void Restore()
    {
        foreach ((string key, string? value) in PreviousValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
