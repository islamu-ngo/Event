using System.Text.Json;

namespace Event.Distributed.IntegrationTests;

[Category("Harness")]
public sealed class K6LoadHarnessTests
{
    private const string CompleteSummary = """
        {"schema":1,"mode":"open","attempts":40,"responses":40,"correct":40,"iterations":40,"httpRequests":40,"correctnessRate":1,"httpFailureRate":0,"dropped":0,"p95Milliseconds":12.5}
        """;

    [Test]
    [Arguments("https://example.org:4321/api/language")]
    [Arguments("http://localhost:4321/api/language")]
    [Arguments("http://127.0.0.1:4321/api/actor")]
    [Arguments("http://127.0.0.1:4321/api/language?target=external")]
    [Arguments("http://unadmitted@127.0.0.1:4321/api/language")]
    [Arguments("http://127.0.0.1:4321/api/language#fragment")]
    [Arguments("http://127.0.0.1:4322/api/language")]
    public async Task UnadmittedTargetsAreRejected(string target)
    {
        await Assert.That(() => K6LoadRunner.ValidateTarget(
            new Uri("http://127.0.0.1:4321/"), new Uri(target))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OnlyTheOwnedLoopbackLookupIsAdmitted()
    {
        var owned = new Uri("http://127.0.0.1:4321/");
        var admitted = K6LoadRunner.ValidateTarget(owned, new Uri(owned, "/api/language"));
        await Assert.That(admitted.AbsolutePath).IsEqualTo("/api/language");
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"schema\":2}")]
    [Arguments("{\"schema\":1,\"schema\":1}")]
    [Arguments("[]")]
    public async Task MissingOrUnrecognizedSummariesFailClosed(string summary)
    {
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, "open"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("responses", "39")]
    [Arguments("correct", "39")]
    [Arguments("iterations", "39")]
    [Arguments("httpRequests", "41")]
    [Arguments("correctnessRate", "0.99")]
    [Arguments("httpFailureRate", "0.01")]
    [Arguments("dropped", "1")]
    [Arguments("attempts", "0")]
    [Arguments("p95Milliseconds", "-1")]
    public async Task IncompleteOrIncorrectRunsCannotProduceCapacityEvidence(string field, string value)
    {
        using var document = JsonDocument.Parse(CompleteSummary);
        var changed = document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name, property => property.Value.Clone());
        using var replacement = JsonDocument.Parse(value);
        changed[field] = replacement.RootElement.Clone();
        string summary = JsonSerializer.Serialize(changed);
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, "open"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ACompleteOpenRunMustMeetItsDeclaredOfferedCount()
    {
        var summary = K6LoadRunner.ParseSummary(CompleteSummary, "open");
        await Assert.That(summary.Attempts).IsEqualTo(40L);
        await Assert.That(summary.P95Milliseconds).IsEqualTo(12.5);
        await Assert.That(() => K6LoadRunner.ParseSummary(CompleteSummary, "closed"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AnUnexpectedSummaryFieldCannotCarryUnreviewedArtifacts()
    {
        string summary = CompleteSummary.TrimEnd().TrimEnd('}') + ",\"responseBody\":\"unreviewed\"}";
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, "open"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("[]")]
    [Arguments("{\"items\":[]}")]
    [Arguments("[{\"id\":1,\"masterCode\":\"en\",\"fullName\":\"English\"},{\"id\":1,\"masterCode\":\"fr\",\"fullName\":\"French\"}]")]
    [Arguments("[{\"id\":0,\"masterCode\":\"en\",\"fullName\":\"English\"}]")]
    [Arguments("[{\"id\":1,\"masterCode\":\"\",\"fullName\":\"English\"}]")]
    public async Task AHealthyStatusCannotReplaceTheLookupContract(string body)
    {
        await Assert.That(() => K6LoadRunner.ValidateLanguageResponse(body))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GeneratorPressureAndMissingSamplesInvalidateMeasurements()
    {
        await Assert.That(() => K6LoadRunner.ValidateGenerator(0, 0.1, 10_000_000))
            .Throws<InvalidOperationException>();
        await Assert.That(() => K6LoadRunner.ValidateGenerator(3, 0.81, 10_000_000))
            .Throws<InvalidOperationException>();
        await Assert.That(() => K6LoadRunner.ValidateGenerator(3, 0.1, 600_000_000))
            .Throws<InvalidOperationException>();
    }
}

[Category("Load")]
[NotInParallel]
public sealed class K6LoadTests
{
    [Test]
    [Arguments("Split")]
    [Arguments("Standalone")]
    public async Task OwnedPublicLookupRemainsCorrectUnderClosedAndOpenLoad(string topology)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        await using var fixture = await OwnedAppHostFixture.CreateAsync(topology, deadline.Token);
        await fixture.StartAsync(deadline.Token);
        await K6LoadRunner.RunAsync(fixture, topology, deadline.Token);
    }
}
