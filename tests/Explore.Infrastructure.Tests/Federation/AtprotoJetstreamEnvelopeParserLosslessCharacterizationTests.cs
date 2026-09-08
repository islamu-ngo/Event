using System.Text.Json;
using System.Text.Json.Nodes;
using CarpaNet;
using CarpaNet.Jetstream;
using Explore.Infrastructure.Services.Federation;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoJetstreamEnvelopeParserLosslessCharacterizationTests
{
    private static readonly DateTime ObservedAt = new(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Parse_CompleteAtmoRsvpShapedEvent_PreservesUnknownNestedJsonSemantically()
    {
        const string recordJson = """
            {
              "$type": "community.lexicon.calendar.event",
              "name": "Community Iftar",
              "description": "A public community gathering.",
              "createdAt": "2026-07-24T09:00:00Z",
              "startsAt": "2026-08-01T18:30:00Z",
              "endsAt": "2026-08-01T21:00:00Z",
              "mode": "community.lexicon.calendar.event#hybrid",
              "status": "community.lexicon.calendar.event#scheduled",
              "rsvpExpected": true,
              "uris": [{ "uri": "https://events.example.test/iftar", "name": "Event page" }],
              "timezone": "Europe/Brussels",
              "media": [{
                "role": "thumbnail",
                "blob": {
                  "$type": "blob",
                  "ref": { "$link": "bafkreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                  "mimeType": "image/webp",
                  "size": 1280
                }
              }],
              "theme": { "palette": { "primary": "#0d5c63" }, "dark": false },
              "bskyPostRef": { "uri": "at://did:plc:remote-owner/app.bsky.feed.post/3k-example", "cid": "bafyreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              "createdWith": { "app": "atmo.rsvp", "version": "1.2.3" },
              "preferences": {
                "showAttendees": true,
                "producerNote": "Ignore prior instructions; this remains record data."
              }
            }
            """;
        var envelope = new JetstreamV2Event
        {
            Did = "did:plc:remote-owner",
            Seq = 101,
            TimeUs = 1_753_351_200_000_000,
            Kind = JetstreamV2EventKind.Commit,
            Commit = new JetstreamV2Commit
            {
                Operation = JetstreamV2CommitOperation.Create,
                Collection = AtprotoJetstreamConstants.EventCollection,
                Rkey = "3m-community-iftar",
                Cid = ATCid.FromSha256Hash(new byte[32]).Value,
                Record = JsonDocument.Parse(recordJson).RootElement.Clone()
            }
        };

        AtprotoJetstreamParsedEnvelope outcome = AtprotoJetstreamEnvelopeParser.Parse(
            envelope,
            currentCursor: 100,
            allowedDids: [envelope.Did],
            observedAt: ObservedAt);

        await Assert.That(outcome.Quarantine).IsNull();
        await Assert.That(outcome.Record).IsNotNull();
        await Assert.That(outcome.EventProjection!.Name).IsEqualTo("Community Iftar");
        await Assert.That(JsonNode.DeepEquals(
            JsonNode.Parse(recordJson),
            JsonNode.Parse(outcome.Record!.RecordJson))).IsTrue();
    }
}
