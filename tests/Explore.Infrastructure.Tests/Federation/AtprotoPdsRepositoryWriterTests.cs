using System.Net;
using System.Text.Json;
using CarpaNet;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Federation;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoPdsRepositoryWriterTests
{
    private const string Did = "did:plc:alice";
    private const string Collection = "community.lexicon.calendar.event";
    private const string RecordKey = "0198ab00000070008000000000000001";
    private const string Payload = "{\"name\":\"Community dinner\"}";

    [Test]
    public async Task Create_WhenRecordIsAbsent_PutsAtStableRecordKey()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<AtprotoGetRecordResponse>>(_ => throw MissingRecord());
        client.PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
                AtprotoPdsRepositoryWriter.PutRecordNsid,
                Arg.Any<AtprotoPutRecordInput>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoPutRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-created"));

        var result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Create),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Cid).IsEqualTo("bafy-created");
        await client.Received(1).PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            AtprotoPdsRepositoryWriter.PutRecordNsid,
            Arg.Is<AtprotoPutRecordInput>(input =>
                input.Repo == Did
                && input.Collection == Collection
                && input.RecordKey == RecordKey
                && input.Validate
                && input.SwapRecord == null
                && input.Record.GetProperty("name").GetString() == "Community dinner"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Create_WhenPriorAttemptAlreadyWroteSamePayload_ReconcilesWithoutSecondPut()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoGetRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-existing",
                JsonSerializer.Deserialize<JsonElement>(Payload)));

        var result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Create),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Cid).IsEqualTo("bafy-existing");
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            default!, default, default);
    }

    [Test]
    public async Task Create_WhenStableRecordKeyContainsDifferentPayload_FailsPermanently()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoGetRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-existing",
                JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Other\"}")));

        var result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Create),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Retryable).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("record_conflict");
    }

    [Test]
    public async Task Update_UsesExpectedCidAsSwapRecord()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoGetRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-old",
                JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Old\"}")));
        client.PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
                AtprotoPdsRepositoryWriter.PutRecordNsid,
                Arg.Any<AtprotoPutRecordInput>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoPutRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-new"));

        var result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Update, "bafy-old"),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await client.Received(1).PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            AtprotoPdsRepositoryWriter.PutRecordNsid,
            Arg.Is<AtprotoPutRecordInput>(input => input.SwapRecord == "bafy-old"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompensatingUpdate_UsesObservedCidWhenPriorCreateHadNotSettledLocally()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoGetRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-observed",
                JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Old\"}")));
        client.PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
                AtprotoPdsRepositoryWriter.PutRecordNsid,
                Arg.Any<AtprotoPutRecordInput>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoPutRecordResponse($"at://{Did}/{Collection}/{RecordKey}", "bafy-new"));

        AtprotoPdsDeliveryResult result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(
                PdsSyncOperation.Update,
                compensationBasePayloads: ["{\"name\":\"Old\"}"]),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await client.Received(1).PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            AtprotoPdsRepositoryWriter.PutRecordNsid,
            Arg.Is<AtprotoPutRecordInput>(input => input.SwapRecord == "bafy-observed"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompensatingUpdate_WhenPendingCreateNeverReachedPds_CreatesAtGroundedStableKey()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<AtprotoGetRecordResponse>>(_ => throw MissingRecord());
        client.PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
                AtprotoPdsRepositoryWriter.PutRecordNsid,
                Arg.Any<AtprotoPutRecordInput>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoPutRecordResponse($"at://{Did}/{Collection}/{RecordKey}", "bafy-created"));

        AtprotoPdsDeliveryResult result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Update, compensationBasePayloads: [Payload]),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await client.Received(1).PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            AtprotoPdsRepositoryWriter.PutRecordNsid,
            Arg.Is<AtprotoPutRecordInput>(input => input.RecordKey == RecordKey && input.SwapRecord == null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompensatingUpdate_WhenObservedCidIsCapturedEvidence_UsesObservedCid()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoGetRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-captured",
                JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Opaque predecessor\"}")));
        client.PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
                AtprotoPdsRepositoryWriter.PutRecordNsid,
                Arg.Any<AtprotoPutRecordInput>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoPutRecordResponse($"at://{Did}/{Collection}/{RecordKey}", "bafy-new"));

        AtprotoPdsDeliveryResult result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Update, compensationBaseCids: ["bafy-captured"]),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await client.Received(1).PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            AtprotoPdsRepositoryWriter.PutRecordNsid,
            Arg.Is<AtprotoPutRecordInput>(input => input.SwapRecord == "bafy-captured"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExpectedCidMutation_WhenRemoteCidChanged_FailsWithoutPutOrDelete()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoGetRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-user-edit",
                JsonSerializer.Deserialize<JsonElement>("{\"name\":\"User edit\"}")));

        AtprotoPdsDeliveryResult update = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Update, expectedCid: "bafy-planned"),
            CancellationToken.None);
        AtprotoPdsDeliveryResult delete = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Delete, expectedCid: "bafy-planned"),
            CancellationToken.None);

        await Assert.That(update.FailureCode).IsEqualTo("record_conflict");
        await Assert.That(delete.FailureCode).IsEqualTo("record_conflict");
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            default!, default, default);
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoDeleteRecordInput, AtprotoDeleteRecordResponse>(
            default!, default, default);
    }

    [Test]
    public async Task Delete_WhenRecordIsAlreadyMissing_ReconcilesAsSuccess()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<AtprotoGetRecordResponse>>(_ => throw MissingRecord());

        var result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Delete, "bafy-deleted"),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Cid).IsEqualTo("bafy-deleted");
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoDeleteRecordInput, AtprotoDeleteRecordResponse>(
            default!, default, default);
    }

    [Test]
    public async Task CompensatingDelete_WhenPriorCreateNeverReachedPds_IsIdempotentSuccess()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<AtprotoGetRecordResponse>>(_ => throw MissingRecord());

        AtprotoPdsDeliveryResult result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(PdsSyncOperation.Delete, compensationBasePayloads: [Payload]),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Uri).IsEqualTo($"at://{Did}/{Collection}/{RecordKey}");
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoDeleteRecordInput, AtprotoDeleteRecordResponse>(
            default!, default, default);
    }

    [Test]
    public async Task CidlessCompensation_WhenRemoteContainsUnrelatedState_FailsWithoutMutation()
    {
        var client = Client();
        client.GetAsync<AtprotoGetRecordResponse>(
                AtprotoPdsRepositoryWriter.GetRecordNsid,
                Arg.Any<IEnumerable<KeyValuePair<string, string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoGetRecordResponse(
                $"at://{Did}/{Collection}/{RecordKey}",
                "bafy-third",
                JsonSerializer.Deserialize<JsonElement>("{\"name\":\"Unrelated\"}")));

        AtprotoPdsDeliveryResult update = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(
                PdsSyncOperation.Update,
                compensationBasePayloads: [Payload],
                compensationBaseCids: ["bafy-old"]),
            CancellationToken.None);
        AtprotoPdsDeliveryResult delete = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(
                PdsSyncOperation.Delete,
                compensationBasePayloads: [Payload],
                compensationBaseCids: ["bafy-old"]),
            CancellationToken.None);

        await Assert.That(update.FailureCode).IsEqualTo("record_conflict");
        await Assert.That(delete.FailureCode).IsEqualTo("record_conflict");
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            default!, default, default);
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoDeleteRecordInput, AtprotoDeleteRecordResponse>(
            default!, default, default);
    }

    [Test]
    public async Task CidlessCompensation_WhenLineageIsIncomplete_FailsBeforeRemoteRead()
    {
        var client = Client();

        AtprotoPdsDeliveryResult result = await AtprotoPdsRepositoryWriter.DeliverAsync(
            client,
            Command(
                PdsSyncOperation.Update,
                compensationBasePayloads: [Payload],
                compensationEvidenceComplete: false),
            CancellationToken.None);

        await Assert.That(result.FailureCode).IsEqualTo("record_conflict");
        await client.DidNotReceiveWithAnyArgs().GetAsync<AtprotoGetRecordResponse>(
            default!, default!, default);
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoPutRecordInput, AtprotoPutRecordResponse>(
            default!, default, default);
        await client.DidNotReceiveWithAnyArgs().PostAsync<AtprotoDeleteRecordInput, AtprotoDeleteRecordResponse>(
            default!, default, default);
    }

    private static IATProtoClient Client()
    {
        var client = Substitute.For<IATProtoClient>();
        client.BaseUrl.Returns(new Uri("https://pds.example/"));
        client.AuthenticatedDid.Returns(Did);
        return client;
    }

    private static AtprotoPdsDeliveryCommand Command(
        PdsSyncOperation operation,
        string? expectedCid = null,
        IReadOnlyList<string>? compensationBasePayloads = null,
        IReadOnlyList<string>? compensationBaseCids = null,
        bool compensationEvidenceComplete = true) =>
        new(
            Guid.Parse("0198ab00-0000-7000-8000-000000000001"),
            Guid.Parse("0198ab00-0000-7000-8000-000000000002"),
            AtprotoDid.Parse(Did),
            new Uri("https://pds.example/"),
            Collection,
            RecordKey,
            operation,
            operation == PdsSyncOperation.Delete ? null : Payload,
            expectedCid,
            compensationBasePayloads,
            compensationBaseCids,
            compensationEvidenceComplete);

    private static ATProtoException MissingRecord() =>
        new("not found", "RecordNotFound", HttpStatusCode.BadRequest);
}
