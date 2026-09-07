using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Hateoas;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Settings;

namespace Event.Application.UnitTests.Contracts;

public sealed class PublishedContractSnapshotTests
{
    [Test]
    public async Task BinaryContracts_CopyCallerOwnedBuffersAndKeepBase64Json()
    {
        byte[] source = [1, 2, 3];
        var manifest = new PolicyPackageManifest("package", "v1", "hash", DateTimeOffset.UnixEpoch, []);
        var archive = new PolicyPackageArchive("package.zip", "application/zip", source, manifest);
        var webhook = new WebhookPayloadBuildResult(true, null, source, "hash", DateTimeOffset.UnixEpoch, null, null);
        var current = new AtprotoCurrentOAuthSession(Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:test"), new Uri("https://pds.test"), "key", source);
        var verification = new AtprotoOAuthVerificationInput(Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:test"), new Uri("https://pds.test"), "key", source);
        var verified = new AtprotoVerifiedOAuthSession(Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:test"), "test.test", new Uri("https://pds.test"), "key", source);
        var prepared = new AtprotoPreparedOAuthSession(source, "key", 1, Guid.NewGuid(), Guid.NewGuid(), Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:test"), "pds.test", "key", null);
        var command = new BootstrapAtprotoSessionCommand(
            Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:test"),
            "https://pds.test/",
            "key",
            AtprotoSubjectClassification.Person,
            source);
        var image = new EventOpenGraphImageRenderResult(source, "etag");

        source[0] = 99;

        await Assert.That(archive.Content.Span[0]).IsEqualTo((byte)1);
        await Assert.That(webhook.PayloadBytes!.Value.Span[0]).IsEqualTo((byte)1);
        await Assert.That(current.OAuthSessionPayload.Span[0]).IsEqualTo((byte)1);
        await Assert.That(verification.OAuthSessionPayload.Span[0]).IsEqualTo((byte)1);
        await Assert.That(verified.OAuthSessionPayload.Span[0]).IsEqualTo((byte)1);
        await Assert.That(prepared.SessionCiphertext.Span[0]).IsEqualTo((byte)1);
        await Assert.That(command.OAuthSessionPayload.Span[0]).IsEqualTo((byte)1);
        await Assert.That(image.PngBytes.Span[0]).IsEqualTo((byte)1);

        string json = JsonSerializer.Serialize(command);
        await Assert.That(json).Contains("\"OAuthSessionPayload\":\"AQID\"");
        BootstrapAtprotoSessionCommand replay = JsonSerializer.Deserialize<BootstrapAtprotoSessionCommand>(json)!;
        await Assert.That(replay.OAuthSessionPayload.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Test]
    public async Task TranslationAndRoleContracts_CopyCallerOwnedCollections()
    {
        var translations = new Dictionary<string, string> { ["en"] = "Original" };
        string[] roles = ["Admin"];
        var translation = new TranslationKeyImport("key", translations);
        LinkDefinition link = LinkDefinition.Edit("Update", roles: roles);

        translations["en"] = "Mutated";
        roles[0] = "Mutated";

        await Assert.That(translation.Translations["en"]).IsEqualTo("Original");
        await Assert.That(link.RequiredRoles![0]).IsEqualTo("Admin");
    }

    [Test]
    public async Task NotificationContracts_CopyCallerOwnedArraysAndRemainJsonRoundTrippable()
    {
        NotificationFanoutChangeField[] fields = [NotificationFanoutChangeField.StartTime];
        var originalSession = new NotificationFanoutSessionDisplayTimeV1(
            Guid.NewGuid(),
            "Original",
            DateTimeOffset.UnixEpoch,
            null);
        NotificationFanoutSessionDisplayTimeV1[] sessions = [originalSession];
        var changes = new NotificationFanoutChangeSetV1(fields);
        var snapshot = new NotificationFanoutSnapshotV1(
            "Event",
            null,
            null,
            null,
            "UTC",
            null,
            sessions);

        fields[0] = NotificationFanoutChangeField.Cancelled;
        sessions[0] = originalSession with { SessionTitle = "Mutated" };

        await Assert.That(changes.Fields[0]).IsEqualTo(NotificationFanoutChangeField.StartTime);
        await Assert.That(snapshot.SessionDisplayTimes![0].SessionTitle).IsEqualTo("Original");

        string changesJson = NotificationFanoutTemplateJson.Serialize(changes);
        string snapshotJson = NotificationFanoutTemplateJson.Serialize(snapshot);
        NotificationFanoutChangeSetV1 replayChanges = JsonSerializer.Deserialize(
            changesJson,
            NotificationFanoutTemplateJsonContext.Default.NotificationFanoutChangeSetV1)!;
        NotificationFanoutSnapshotV1 replaySnapshot = JsonSerializer.Deserialize(
            snapshotJson,
            NotificationFanoutTemplateJsonContext.Default.NotificationFanoutSnapshotV1)!;
        await Assert.That(replayChanges.Fields[0]).IsEqualTo(NotificationFanoutChangeField.StartTime);
        await Assert.That(replaySnapshot.SessionDisplayTimes![0].SessionTitle).IsEqualTo("Original");
    }

    [Test]
    public async Task SettingDefinition_CopiesCallerOwnedAllowedValues()
    {
        var allowedValues = new List<string> { "original" };
        var definition = new SettingDefinition(
            "test.key",
            SettingValueType.String,
            "\"original\"",
            "Test",
            "Test definition",
            AllowedValues: allowedValues);

        allowedValues[0] = "mutated";

        await Assert.That(definition.AllowedValues![0]).IsEqualTo("original");
    }
}
