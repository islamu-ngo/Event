using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class AdmissionTicketApiRedContractTests
{
    [Test]
    public async Task PresentAndAbsentRecoveryIdentitiesReturnIdenticalAcceptedHttpShape()
    {
        await RequireRoute(RecoveryRequest);
        var scenario = new AdmissionApiScenario();
        await using var factory = new AdmissionApiFactory(scenario);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage present = await SendRecoveryRequest(
            client, scenario.PresentIdentity, "recovery-uniform-present");
        using HttpResponseMessage absent = await SendRecoveryRequest(
            client, scenario.AbsentIdentity, "recovery-uniform-absent");

        await Assert.That(present.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(await ResponseShape(present)).IsEqualTo(await ResponseShape(absent));
        await Assert.That(scenario.PresentRecoveryRequests).IsEqualTo(1);
        await Assert.That(scenario.AbsentRecoveryRequests).IsEqualTo(1);
    }

    [Test]
    [Arguments("malformed")]
    [Arguments("expired")]
    [Arguments("replayed")]
    [Arguments("wrong-purpose")]
    [Arguments("wrong-tenant")]
    public async Task InvalidRecoveryCapabilityReturnsCanonicalNotFoundProblem(string invalidState)
    {
        await RequireRoute(RecoveryConsume);
        var scenario = new AdmissionApiScenario();
        await using var factory = new AdmissionApiFactory(scenario);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await SendRecoveryConsume(
            client, scenario.CapabilityFor(invalidState));
        using HttpResponseMessage malformedBaseline = await SendRecoveryConsume(
            client, scenario.NewMalformedCapability());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(malformedBaseline.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
        await Assert.That(await ProblemFingerprint(response))
            .IsEqualTo(await ProblemFingerprint(malformedBaseline));
    }

    [Test]
    public async Task RecoveryConsumeReturnsCompleteNoLinkDocumentAndReplayLeaksNothing()
    {
        await RequireRoute(RecoveryConsume);
        var scenario = new AdmissionApiScenario();
        using var logs = new CapturingLoggerProvider();
        await using var factory = new AdmissionApiFactory(scenario, logs);
        using HttpClient client = factory.CreateClient();
        string capability = scenario.IssueValidCapability();

        using HttpResponseMessage success = await SendRecoveryConsume(client, capability);
        string successBody = await success.Content.ReadAsStringAsync();
        using HttpResponseMessage replay = await SendRecoveryConsume(client, capability);
        string replayBody = await replay.Content.ReadAsStringAsync();
        bool urlLeaks = success.RequestMessage!.RequestUri!.OriginalString.Contains(capability, StringComparison.Ordinal)
            || success.RequestMessage.RequestUri.OriginalString.Contains(
                scenario.RecoveryRecordId.ToString("D"), StringComparison.OrdinalIgnoreCase);
        bool bodyLeaks = successBody.Contains(capability, StringComparison.Ordinal)
            || successBody.Contains(scenario.RecoveryRecordId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            || replayBody.Contains(capability, StringComparison.Ordinal)
            || replayBody.Contains(scenario.RecoveryRecordId.ToString("D"), StringComparison.OrdinalIgnoreCase);
        bool logsLeak = logs.Messages.Any(message => message.Contains(capability, StringComparison.Ordinal)
            || message.Contains(scenario.RecoveryRecordId.ToString("D"), StringComparison.OrdinalIgnoreCase));

        await Assert.That(success.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateNoReferrer(success);
        await Assert.That(JsonGuid(successBody, "ticketId")).IsEqualTo(scenario.RecoveryTicketId);
        await Assert.That(JsonGuid(successBody, "eventId")).IsEqualTo(scenario.EventId);
        await Assert.That(JsonString(successBody, "statusCode")).IsEqualTo(scenario.ActiveStatusCode);
        await Assert.That(JsonString(successBody, "displayReference"))
            .IsEqualTo(scenario.RecoveryDisplayReference);
        await Assert.That(JsonString(successBody, "manualCode")).IsEqualTo(scenario.ManualCredential);
        await Assert.That(JsonString(successBody, "manualCodeClassificationCode"))
            .IsEqualTo(scenario.SensitiveClassification);
        await Assert.That(JsonString(successBody, "qrRepresentation"))
            .IsEqualTo(scenario.QrRepresentation);
        await Assert.That(JsonString(successBody, "printModel")).IsEqualTo(scenario.PrintModel);
        await Assert.That(HasHalLinksMember(successBody)).IsFalse();
        await Assert.That(Relations(successBody)).IsEmpty();
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(replay.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That(urlLeaks).IsFalse();
        await Assert.That(bodyLeaks).IsFalse();
        await Assert.That(logsLeak).IsFalse();
    }

    [Test]
    public async Task RecoveryRateLimitCannotBeEvadedByCallerVaryingIdentityOrForwardedAddress()
    {
        await RequireRoute(RecoveryRequest);
        var scenario = new AdmissionApiScenario();
        await using var factory = new AdmissionApiFactory(scenario, enableRecoveryRateLimit: true);
        using HttpClient client = factory.CreateClient();
        IConfiguration configuration = factory.Services.GetRequiredService<IConfiguration>();

        await Assert.That(configuration.GetValue<int>(
            "RateLimiting:AdmissionTicketRecovery:PermitLimit")).IsEqualTo(1);

        using HttpResponseMessage first = await SendRecoveryRequest(
            client, "first@invalid.example", "recovery-budget-1", "198.51.100.10");
        using HttpResponseMessage second = await SendRecoveryRequest(
            client, "second@invalid.example", "recovery-budget-2", "203.0.113.20");

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(second.Headers.Contains("Retry-After")).IsTrue();
    }

    [Test]
    public async Task RecoveryNormalizedIdentityBudgetMapsToSafeRetryableProblem()
    {
        await RequireRoute(RecoveryRequest);
        var scenario = new AdmissionApiScenario
        {
            RecoveryRateLimitRetryAfterSeconds = 37
        };
        await using var factory = new AdmissionApiFactory(scenario);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await SendRecoveryRequest(
            client,
            scenario.PresentIdentity,
            "recovery-identity-budget",
            "203.0.113.30");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(response.Headers.GetValues("Retry-After").Single()).IsEqualTo("37");
        await Assert.That(body).Contains("\"code\":\"rate_limited\"");
        await Assert.That(body).DoesNotContain(scenario.PresentIdentity);
        await Assert.That(body).DoesNotContain("Capability");
        await Assert.That(body).DoesNotContain("Digest");
        await Assert.That(body).DoesNotContain("Credential");
    }
}
