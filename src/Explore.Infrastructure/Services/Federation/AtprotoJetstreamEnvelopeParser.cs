using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarpaNet;
using CarpaNet.Jetstream;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using CalendarEvent = CommunityLexicon.Calendar.Event;
using CalendarRsvp = CommunityLexicon.Calendar.Rsvp;

namespace Explore.Infrastructure.Services.Federation;

internal static class AtprotoJetstreamConstants
{
    public const string EventCollection = "community.lexicon.calendar.event";
    public const string RsvpCollection = "community.lexicon.calendar.rsvp";
    public static readonly IReadOnlyList<string> Collections = [EventCollection, RsvpCollection];
}

/// <summary>
/// Outcome of parsing one v2 envelope. Jetstream v2 separates two axes that v1 conflated in a single
/// microsecond timestamp: <paramref name="Cursor"/> is the resume token (<c>seq</c>) and
/// <paramref name="SourceVersion"/> is the ordering key (<c>time_us</c>) compared against PDS snapshot
/// versions during recovery. They must not be swapped.
/// </summary>
internal sealed record AtprotoJetstreamParsedEnvelope(
    long Cursor,
    long SourceVersion,
    AtprotoRecord? Record,
    AtprotoJetstreamQuarantine? Quarantine,
    bool AdvanceCursor = true,
    AtprotoEventProjection? EventProjection = null,
    AtprotoEventProjectionInvalidation? EventProjectionInvalidation = null)
{
    /// <summary>
    /// Set for envelopes that carry no ingestible evidence at all — <c>identity</c> and <c>sync</c> kinds,
    /// and account events that are not deactivations. v2 collection filters never suppress these kinds, so
    /// they are skipped locally: never written as quarantine evidence, never round-tripped to the store.
    /// </summary>
    public bool Ignored { get; init; }

    /// <summary>Set when an upstream account was deactivated or deleted and its records must be retired.</summary>
    public AtprotoAccountPurge? AccountPurge { get; init; }
}

internal static class AtprotoJetstreamEnvelopeParser
{
    private static readonly HashSet<string> RsvpStatuses =
    [
        "community.lexicon.calendar.rsvp#interested",
        "community.lexicon.calendar.rsvp#going",
        "community.lexicon.calendar.rsvp#notgoing"
    ];

    public static AtprotoJetstreamParsedEnvelope Parse(
        JetstreamV2Event envelope,
        long currentCursor,
        IReadOnlyCollection<string> allowedDids,
        DateTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The observation timestamp must be UTC.", nameof(observedAt));
        }

        bool didIsValid = AtprotoDid.TryParse(envelope.Did, out AtprotoDid parsedDid)
            && parsedDid.Value.Length <= 255;
        string recordJson = envelope.Commit?.Record?.GetRawText() ?? string.Empty;
        string envelopeHash = Hash(BuildEnvelopeFingerprint(envelope, recordJson));
        string? identityHash = BuildIdentityHash(envelope);
        bool sourceVersionIsInRange = TryFromUnixMicroseconds(envelope.TimeUs, out DateTime parsedAt);
        DateTime eventAt = sourceVersionIsInRange ? parsedAt : observedAt;
        AtprotoJetstreamParsedEnvelope Reject(
            string reason,
            bool advanceCursor = true,
            bool invalidateEventProjection = false) => new(
                envelope.Seq,
                envelope.TimeUs,
                null,
                new AtprotoJetstreamQuarantine
                {
                    Id = Guid.CreateVersion7(),
                    ReasonCode = reason,
                    EnvelopeHash = envelopeHash,
                    RecordIdentityHash = identityHash,
                    EventAt = eventAt,
                    QuarantinedAt = observedAt
                },
                advanceCursor,
                EventProjectionInvalidation: invalidateEventProjection
                    ? new AtprotoEventProjectionInvalidation(
                        envelope.Did,
                        envelope.Commit!.Collection!,
                        envelope.Commit.Rkey!,
                        envelope.TimeUs)
                    : null);

        if (envelope.Seq <= currentCursor || envelope.Seq <= 0)
        {
            return Reject("invalid_cursor", advanceCursor: false);
        }

        // Non-commit kinds are never ingestible record evidence, and v2 delivers them regardless of the
        // collection filter. Skipping them locally keeps the whole network's identity/account/sync
        // traffic out of the quarantine table — except account deactivations, which are the network's
        // purge signal and must retire anything already federated in from that repository.
        if (envelope.Kind != JetstreamV2EventKind.Commit || envelope.Commit is null)
        {
            AtprotoJetstreamParsedEnvelope ignored = new(envelope.Seq, envelope.TimeUs, null, null)
            {
                Ignored = true
            };
            if (envelope.Kind != JetstreamV2EventKind.Account
                || envelope.Account is not { Active: false }
                || envelope.TimeUs <= 0
                || !sourceVersionIsInRange
                || !didIsValid)
            {
                return ignored;
            }

            // Honour the curated allowlist when one is configured; in public-collection mode any deleted
            // account may own records we ingested, so all of them are actionable.
            if (allowedDids.Count > 0 && !allowedDids.Contains(envelope.Did, StringComparer.Ordinal))
            {
                return ignored;
            }

            return new(envelope.Seq, envelope.TimeUs, null, null)
            {
                AccountPurge = new AtprotoAccountPurge(
                    parsedDid.Value,
                    envelope.TimeUs,
                    envelope.Account.Status)
            };
        }

        // time_us is the cross-source ordering key: a value outside DateTime range would corrupt
        // last-writer-wins against PDS snapshot versions, so it is rejected rather than clamped.
        if (envelope.TimeUs <= 0 || !sourceVersionIsInRange)
        {
            return Reject("invalid_source_timestamp");
        }

        if (!didIsValid)
        {
            return Reject("invalid_did");
        }

        if (allowedDids.Count > 0 && !allowedDids.Contains(envelope.Did, StringComparer.Ordinal))
        {
            return Reject("did_not_allowed");
        }

        JetstreamV2Commit commit = envelope.Commit;
        if (commit.Collection is not AtprotoJetstreamConstants.EventCollection and not AtprotoJetstreamConstants.RsvpCollection)
        {
            return Reject("collection_not_allowed");
        }

        if (commit.Operation is not (JetstreamV2CommitOperation.Create
            or JetstreamV2CommitOperation.Update
            or JetstreamV2CommitOperation.Delete))
        {
            return Reject("operation_not_supported");
        }

        if (!IsValidRecordKey(commit.Rkey))
        {
            return Reject("invalid_record_key");
        }

        string uri = $"at://{parsedDid.Value}/{commit.Collection}/{commit.Rkey}";
        if (uri.Length > 500)
        {
            return Reject("record_identity_too_large");
        }

        if (commit.Operation == JetstreamV2CommitOperation.Delete)
        {
            if (commit.Record.HasValue && commit.Record.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return Reject(
                    "invalid_delete_shape",
                    invalidateEventProjection: commit.Collection == AtprotoJetstreamConstants.EventCollection);
            }

            return new(
                envelope.Seq,
                envelope.TimeUs,
                CreateRecord(envelope, parsedDid, commit, uri, null, null, null, observedAt, eventAt),
                null);
        }

        if (!commit.Record.HasValue || commit.Record.Value.ValueKind != JsonValueKind.Object)
        {
            return Reject(
                "record_missing",
                invalidateEventProjection: commit.Collection == AtprotoJetstreamConstants.EventCollection);
        }

        if (Encoding.UTF8.GetByteCount(recordJson) > AtprotoRecordSizeValidator.MaximumJsonBytes)
        {
            return Reject(
                "record_too_large",
                invalidateEventProjection: commit.Collection == AtprotoJetstreamConstants.EventCollection);
        }

        JsonElement record = commit.Record.Value;
        if (!record.TryGetProperty("$type", out JsonElement type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), commit.Collection, StringComparison.Ordinal))
        {
            return Reject(
                "record_type_mismatch",
                invalidateEventProjection: commit.Collection == AtprotoJetstreamConstants.EventCollection);
        }

        if (commit.Cid is not { Length: > 0 and <= 255 } || !new ATCid(commit.Cid).IsAtProtoBlessedFormat)
        {
            return Reject(
                "invalid_record_cid",
                invalidateEventProjection: commit.Collection == AtprotoJetstreamConstants.EventCollection);
        }

        if (!TryValidateRecord(
                commit.Collection,
                record,
                out string? subjectUri,
                out string? subjectCid,
                out CalendarEvent? calendarEvent))
        {
            return Reject(
                "invalid_record_shape",
                invalidateEventProjection: commit.Collection == AtprotoJetstreamConstants.EventCollection);
        }

        AtprotoRecord canonicalRecord = CreateRecord(
            envelope,
            parsedDid,
            commit,
            uri,
            recordJson,
            subjectUri,
            subjectCid,
            observedAt,
            eventAt);
        return new(
            envelope.Seq,
            envelope.TimeUs,
            canonicalRecord,
            null,
            EventProjection: calendarEvent is null
                ? null
                : AtprotoCalendarEventProjectionMapper.Map(
                    calendarEvent,
                    canonicalRecord.Id,
                    envelope.TimeUs,
                    observedAt));
    }

    private static bool TryValidateRecord(
        string collection,
        JsonElement record,
        out string? subjectUri,
        out string? subjectCid,
        out CalendarEvent? calendarEvent)
    {
        subjectUri = null;
        subjectCid = null;
        calendarEvent = null;
        try
        {
            if (collection == AtprotoJetstreamConstants.EventCollection)
            {
                calendarEvent = CalendarEvent.FromJson(record);
                return calendarEvent is not null && AtprotoCalendarEventRecordValidator.Validate(calendarEvent).IsValid;
            }

            CalendarRsvp? rsvp = CalendarRsvp.FromJson(record);
            string? rsvpUri = rsvp?.Subject?.Uri.Value;
            string? rsvpCid = rsvp?.Subject?.Cid;
            if (rsvp?.Subject is null
                || !RsvpStatuses.Contains(rsvp.Status)
                || rsvp.Subject.Uri.Collection != AtprotoJetstreamConstants.EventCollection
                || string.IsNullOrWhiteSpace(rsvp.Subject.Uri.RecordKey)
                || rsvpUri is not { Length: > 0 and <= 500 }
                || rsvpCid is not { Length: > 0 and <= 255 }
                || !new ATCid(rsvpCid).IsAtProtoBlessedFormat
                || !AtprotoRecordSizeValidator.Validate(rsvp).IsValid)
            {
                return false;
            }

            subjectUri = rsvpUri;
            subjectCid = rsvpCid;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static AtprotoRecord CreateRecord(
        JetstreamV2Event envelope,
        AtprotoDid did,
        JetstreamV2Commit commit,
        string uri,
        string? recordJson,
        string? subjectUri,
        string? subjectCid,
        DateTime observedAt,
        DateTime eventAt) => new()
        {
            Id = Guid.CreateVersion7(),
            Did = did.Value,
            Collection = commit.Collection!,
            RecordKey = commit.Rkey!,
            Cid = commit.Cid,
            Uri = uri,
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            // time_us, not seq: this is compared against ToUnixMicroseconds(SnapshotStartedAt) when PDS
            // recovery reconciles the same record.
            SourceVersion = envelope.TimeUs,
            SourceCursor = envelope.Seq,
            RecordJson = recordJson,
            RecordHash = recordJson is null ? null : Hash(recordJson),
            SubjectUri = subjectUri,
            SubjectCid = subjectCid,
            IndexedAt = eventAt,
            UpdatedAt = observedAt,
            TombstonedAt = commit.Operation == JetstreamV2CommitOperation.Delete ? observedAt : null
        };

    private static bool IsValidRecordKey(string? recordKey) =>
        recordKey is { Length: > 0 and <= 255 }
        && recordKey.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':' or '~');

    private static bool TryFromUnixMicroseconds(long value, out DateTime result)
    {
        try
        {
            result = DateTime.UnixEpoch.AddTicks(checked(value * 10));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = default;
            return false;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static string BuildEnvelopeFingerprint(JetstreamV2Event envelope, string recordJson) =>
        string.Join('\n', envelope.Did, envelope.Seq, envelope.TimeUs, envelope.Kind, envelope.Commit?.Operation,
            envelope.Commit?.Collection, envelope.Commit?.Rkey, envelope.Commit?.Cid, recordJson);

    private static string? BuildIdentityHash(JetstreamV2Event envelope) =>
        envelope.Commit?.Collection is null || envelope.Commit.Rkey is null
            ? null
            : Hash(string.Join('\n', envelope.Did, envelope.Commit.Collection, envelope.Commit.Rkey));

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
