namespace Event.Architecture.Tests;

public sealed class LegalIdentityDeploymentContractTests
{
    private static readonly string RepoRoot = ContextSystemHelpers.RepoRoot;

    private static readonly string[] InstanceIdentityKeys =
    [
        "OPERATORID",
        "PUBLICNAME",
        "LEGALNAME",
        "ISOFFICIALINSTANCE",
        "OFFICIALORIGIN",
        "OPERATORKINDCODE",
        "JURISDICTIONCOUNTRYCODE",
        "REGISTRATIONIDENTIFIER",
        "PUBLICCONTACTEMAIL",
        "WEBSITEURL",
        "LEGALNOTICEURL",
        "TERMSURL",
        "PRIVACYURL"
    ];

    private static readonly string[] PaymentOperationsKeys =
    [
        "COMPLAINTOWNER",
        "REFUNDOWNER",
        "DISPUTEOWNER",
        "RECONCILIATIONOWNER",
        "ACTIVATIONSTATUS",
        "REFUNDPOLICYLANGUAGETAG",
        "STATEMENTDESCRIPTOR",
        "CHARGETYPE"
    ];

    [Test]
    public async Task ComposeMapsCanonicalLegalIdentityAndPaymentOperationsIntoApi()
    {
        string compose = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "docker-compose.yml"));

        await Assert.That(compose).Contains("x-instance-operator-identity-env: &instance-operator-identity-env");
        await Assert.That(compose).Contains("x-payment-operations-env: &payment-operations-env");
        await Assert.That(compose).Contains("*instance-operator-identity-env");
        await Assert.That(compose).Contains("*payment-operations-env");

        foreach (string key in InstanceIdentityKeys)
        {
            string environmentKey = $"INSTANCE__OPERATORIDENTITY__{key}";
            await Assert.That(compose).Contains($"{environmentKey}: ${{{environmentKey}:-");
        }

        foreach (string key in PaymentOperationsKeys)
        {
            string environmentKey = $"PAYMENTS__CHECKOUTGOVERNANCE__{key}";
            await Assert.That(compose).Contains($"{environmentKey}: ${{{environmentKey}:-");
        }
    }

    [Test]
    public async Task ComposeWaitsForApiStartupValidationBeforeLaunchingSplitBlazor()
    {
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(RepoRoot, "docker-compose.yml"));
        string api = ExtractComposeService(lines, "islamu-event-api");
        string ui = ExtractComposeService(lines, "islamu-event-ui");

        await Assert.That(api).Contains("healthcheck:");
        await Assert.That(api).Contains("/dev/tcp/localhost/8080");
        await Assert.That(ui).Contains("islamu-event-api:");
        await Assert.That(ui).Contains("condition: service_healthy");
    }

    private static string ExtractComposeService(IReadOnlyList<string> lines, string serviceName)
    {
        int start = -1;
        for (int index = 0; index < lines.Count; index++)
        {
            if (string.Equals(lines[index], $"  {serviceName}:", StringComparison.Ordinal))
            {
                start = index;
                break;
            }
        }

        if (start < 0)
        {
            throw new InvalidOperationException($"Compose service '{serviceName}' was not found.");
        }

        int end = lines.Count;
        for (int index = start + 1; index < lines.Count; index++)
        {
            string line = lines[index];
            if (line.StartsWith("  ", StringComparison.Ordinal)
                && !line.StartsWith("    ", StringComparison.Ordinal)
                && line.EndsWith(':'))
            {
                end = index;
                break;
            }
        }

        return string.Join(Environment.NewLine, lines.Skip(start).Take(end - start));
    }
}
