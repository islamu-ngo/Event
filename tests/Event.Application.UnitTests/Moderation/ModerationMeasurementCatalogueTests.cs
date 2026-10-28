using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Services.Moderation;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationMeasurementCatalogueTests
{
    [Test]
    public async Task ExactNativeIdentitiesRemainDistinctFromStableWireCaseIds()
    {
        var catalogue = ModerationMeasurementCatalogue.Parse(Bytes(Document()));
        await Assert.That(catalogue.Jobs.Length).IsEqualTo(3);
        await Assert.That(catalogue.Jobs[0].Cases[0].CaseId).IsEqualTo("domain.case");
        await Assert.That(catalogue.Jobs[0].Cases[0].JournalTestId).IsEqualTo("journal.domain");
        await Assert.That(catalogue.Jobs[0].Cases[0].NodeUid).IsEqualTo("node.domain");
        await Assert.That(catalogue.CaseIds).IsEquivalentTo(["api.case", "domain.case", "persistence.case"]);
    }

    [Test]
    [Arguments("native.operations")]
    [Arguments("resource.redirect")]
    [Arguments("queued.webhook")]
    public async Task CatalogueRetainsTheMeasuredConsumerWithoutInventingExecution(string consumerId)
    {
        var document = Document();
        document["consumerId"] = consumerId;
        var catalogue = ModerationMeasurementCatalogue.Parse(Bytes(document));
        await Assert.That(catalogue.ConsumerId).IsEqualTo(consumerId);
        await Assert.That(catalogue.CaseIds).IsEquivalentTo(["api.case", "domain.case", "persistence.case"]);
    }

    [Test]
    [Arguments("unknown")]
    [Arguments("missing")]
    [Arguments("null-job")]
    [Arguments("null-case")]
    [Arguments("empty-job")]
    [Arguments("reordered-bundles")]
    [Arguments("duplicate-case")]
    [Arguments("duplicate-node")]
    [Arguments("duplicate-journal")]
    [Arguments("duplicate-terminal")]
    [Arguments("non-ascii-case")]
    [Arguments("unknown-provider")]
    [Arguments("bad-digest")]
    [Arguments("empty-provider-version")]
    [Arguments("noncanonical-consumer")]
    [Arguments("oversized-consumer")]
    public async Task IncompleteOrAmbiguousCatalogueCannotSupplyMembership(string mutation)
    {
        var document = Document();
        var jobs = document["jobs"]!.AsArray();
        var cases = jobs[0]!["cases"]!.AsArray();
        switch (mutation)
        {
            case "unknown": document["passing"] = true; break;
            case "missing": document.Remove("productArtifactDigest"); break;
            case "null-job": jobs[0] = null; break;
            case "null-case": cases[0] = null; break;
            case "empty-job": cases.Clear(); break;
            case "reordered-bundles": jobs[0]!["bundle"] = "Event.API.IntegrationTests"; break;
            case "duplicate-case": jobs[1]!["cases"]![0]!["caseId"] = "domain.case"; break;
            case "duplicate-node":
            case "duplicate-journal":
            case "duplicate-terminal":
                var other = cases[0]!.DeepClone();
                other["caseId"] = "domain.other";
                if (mutation != "duplicate-node") other["nodeUid"] = "node.other";
                if (mutation != "duplicate-journal") other["journalTestId"] = "journal.other";
                if (mutation != "duplicate-terminal") other["mtpTestName"] = "other";
                cases.Add(other);
                break;
            case "non-ascii-case": cases[0]!["caseId"] = "\u00e9"; break;
            case "unknown-provider": document["providerCode"] = "invented"; break;
            case "bad-digest": document["configurationDigest"] = new string('A', 64); break;
            case "empty-provider-version": document["providerVersion"] = ""; break;
            case "noncanonical-consumer": document["consumerId"] = "Resource.Payload"; break;
            case "oversized-consumer": document["consumerId"] = new string('a', 65); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        await Assert.That(() => ModerationMeasurementCatalogue.Parse(Bytes(document))).Throws<JsonException>();
    }

    [Test]
    public async Task DuplicateJsonPropertiesAndOversizedBytesAreRejected()
    {
        var text = Document().ToJsonString();
        var duplicate = System.Text.Encoding.UTF8.GetBytes(text.Insert(1, "\"schemaVersion\":1,"));
        await Assert.That(() => ModerationMeasurementCatalogue.Parse(duplicate)).Throws<JsonException>();
        await Assert.That(() => ModerationMeasurementCatalogue.Parse(
            new byte[ModerationMeasurementCatalogue.MaximumBytes + 1])).Throws<JsonException>();
    }

    private static byte[] Bytes(JsonObject document) => JsonSerializer.SerializeToUtf8Bytes(document);

    private static JsonObject Document()
    {
        var jobs = new JsonArray();
        foreach (var (bundle, name) in new[]
        {
            ("Event.Domain.UnitTests", "domain"), ("Event.API.IntegrationTests", "api"),
            ("Event.Persistence.IntegrationTests", "persistence")
        })
        {
            jobs.Add(new JsonObject
            {
                ["bundle"] = bundle,
                ["treeFilter"] = "/*/*/MeasuredCase/*",
                ["cases"] = new JsonArray(new JsonObject
                {
                    ["caseId"] = name + ".case",
                    ["journalTestId"] = "journal." + name,
                    ["nodeUid"] = "node." + name,
                    ["className"] = "MeasuredCase",
                    ["methodName"] = "Outcome",
                    ["displayName"] = "Outcome",
                    ["mtpClassName"] = "Tests.MeasuredCase",
                    ["mtpMethodName"] = "Outcome",
                    ["mtpTestName"] = "Outcome"
                })
            });
        }
        return new()
        {
            ["schemaVersion"] = 1,
            ["consumerId"] = "resource.payload",
            ["runtimeVersion"] = "10.0.0",
            ["providerCode"] = "Sqlite",
            ["providerVersion"] = "3.49.0",
            ["configurationDigest"] = new string('a', 64),
            ["productArtifactDigest"] = new string('b', 64),
            ["jobs"] = jobs
        };
    }
}
