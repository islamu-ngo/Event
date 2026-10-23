using System.Reflection;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.Moderation.Notices;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationNoticeCorrectionQueryTests
{
    [Test]
    [Arguments(1L)]
    [Arguments(long.MaxValue - 1)]
    public async Task ValidCoordinatesAcceptTheUsableRevisionRange(long revision)
    {
        var query = Request() with { ExpectedCaseRevision = revision };
        await Assert.That((await new ReadModerationNoticeCorrectionQueryValidator().ValidateAsync(query)).IsValid).IsTrue();
    }

    [Test]
    [Arguments("8")]
    [Arguments("9")]
    [Arguments("a")]
    [Arguments("b")]
    public async Task EveryNativeUuid7VariantIsAcceptedForAllFiveSelectors(string variant)
    {
        var id = Guid.Parse($"018e4e5c-7f00-7000-{variant}000-000000000001");
        var query = Request() with
        {
            PendingEffectId = id, ApprovalId = id, GrantId = id, CaseId = id, OriginalNoticeId = id
        };
        await Assert.That((await new ReadModerationNoticeCorrectionQueryValidator().ValidateAsync(query)).IsValid).IsTrue();
    }

    [Test]
    [Arguments("pending")]
    [Arguments("approval")]
    [Arguments("grant")]
    [Arguments("case")]
    [Arguments("notice")]
    public async Task EverySelectorRejectsEmptyWrongVersionAndNonNativeVariants(string selector)
    {
        Guid[] invalid =
        [
            Guid.Empty,
            Guid.Parse("018e4e5c-7f00-4000-8000-000000000001"),
            Guid.Parse("018e4e5c-7f00-7000-0000-000000000001"),
            Guid.Parse("018e4e5c-7f00-7000-c000-000000000001"),
            Guid.Parse("018e4e5c-7f00-7000-e000-000000000001")
        ];
        foreach (var id in invalid)
        {
            var query = Request();
            query = selector switch
            {
                "pending" => query with { PendingEffectId = id },
                "approval" => query with { ApprovalId = id },
                "grant" => query with { GrantId = id },
                "case" => query with { CaseId = id },
                _ => query with { OriginalNoticeId = id }
            };
            var validation = await new ReadModerationNoticeCorrectionQueryValidator().ValidateAsync(query);
            await Assert.That(validation.IsValid).IsFalse();
        }
    }

    [Test]
    [Arguments("snapshot")]
    [Arguments("binding")]
    public async Task BothHashesRejectMissingWrongLengthUppercaseAndNonAsciiHex(string selector)
    {
        string?[] invalid =
        [
            null, "", new('a', 63), new('a', 65), new('A', 64), new('g', 64),
            new('ａ', 64), new('١', 64), new('/', 64), new(':', 64), new(' ', 64)
        ];
        foreach (var hash in invalid)
        {
            var query = selector == "snapshot"
                ? Request() with { ExpectedPendingSnapshotHash = hash! }
                : Request() with { ExpectedPendingEffectBindingHash = hash! };
            await Assert.That((await new ReadModerationNoticeCorrectionQueryValidator().ValidateAsync(query)).IsValid).IsFalse();
        }
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(long.MaxValue)]
    public async Task NonPositiveAndExhaustedRevisionsAreRejected(long revision)
    {
        var query = Request() with { ExpectedCaseRevision = revision };
        await Assert.That((await new ReadModerationNoticeCorrectionQueryValidator().ValidateAsync(query)).IsValid).IsFalse();
    }

    [Test]
    public async Task QueryAndDisclosureRequestDiagnosticsNeverRenderCoordinatesOrHashes()
    {
        var query = Request();
        var request = new ModerationNoticeCorrectionReadRequest(
            query.PendingEffectId, query.ExpectedPendingSnapshotHash, query.ExpectedPendingEffectBindingHash,
            query.ApprovalId, query.GrantId, query.CaseId, query.ExpectedCaseRevision, query.OriginalNoticeId);
        await Assert.That(query.ToString()).IsEqualTo(nameof(ReadModerationNoticeCorrectionQuery));
        await Assert.That(request.ToString()).IsEqualTo(nameof(ModerationNoticeCorrectionReadRequest));
        await Assert.That((query with { GrantId = Guid.CreateVersion7() }) == query).IsFalse();
        await Assert.That((request with { GrantId = Guid.CreateVersion7() }) == request).IsFalse();
    }

    [Test]
    public async Task PendingResultIsClosedNonCopyableAndCannotExposePreparedOutputOrAuthority()
    {
        var type = typeof(ModerationNoticeCorrectionReadResult);
        await Assert.That(type.IsSealed).IsTrue();
        await Assert.That(type.GetConstructors().Length).IsEqualTo(0);
        await Assert.That(type.GetMethod("<Clone>$") is null).IsTrue();
        await Assert.That(typeof(ICloneable).IsAssignableFrom(type)).IsFalse();
        await Assert.That(type.GetProperties().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "IsValid", "ValidationErrors", "AuthenticationRequired" });
        await Assert.That(type.GetProperties().Any(property => property.SetMethod is not null)).IsFalse();
        var disclose = type.GetMethod(nameof(ModerationNoticeCorrectionReadResult.DiscloseAsync),
            BindingFlags.Public | BindingFlags.Instance)!;
        await Assert.That(disclose.ReturnType == typeof(Task<bool>)).IsTrue();
        await Assert.That(disclose.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { typeof(Func<ReadOnlyMemory<byte>, CancellationToken, Task>), typeof(CancellationToken) });
    }

    private static ReadModerationNoticeCorrectionQuery Request() => new()
    {
        PendingEffectId = Guid.CreateVersion7(),
        ExpectedPendingSnapshotHash = string.Concat(Enumerable.Repeat("0123456789abcdef", 4)),
        ExpectedPendingEffectBindingHash = new string('b', 64),
        ApprovalId = Guid.CreateVersion7(),
        GrantId = Guid.CreateVersion7(),
        CaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 1,
        OriginalNoticeId = Guid.CreateVersion7()
    };
}
