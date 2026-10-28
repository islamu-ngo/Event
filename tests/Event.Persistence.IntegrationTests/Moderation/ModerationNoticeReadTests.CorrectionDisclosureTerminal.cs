namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correction_output_expiring_inside_the_consumer_propagates_without_replay_or_completion(bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Database.Context();
        await using var colocatedPrivacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(reader) : null;
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, colocatedPrivacy);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var privacy = (colocatedPrivacy ?? fixture.Privacy).Authority(reader, fixture.Clock);
        var repository = CorrectionDisclosureReader(reader, fixture, fixture.Database.ReviewerId, privacy);
        ReadOnlyMemory<byte> borrowed = default;
        Exception? failure = null;
        int calls = 0;

        try
        {
            await repository.DiscloseCorrectionAsync(CorrectionPreparationRequest(pending, grant), async (bytes, token) =>
            {
                calls++;
                borrowed = bytes;
                await AssertCorrectionExplanationAsync(bytes, pending.Request.Intent);
                fixture.Clock.UtcNow = pending.Request.Intent.ValidUntil;
            });
        }
        catch (Exception exception) { failure = exception; }

        // The consumer observed bytes: a failed final check cannot become an undisclosed/false result.
        await Assert.That(failure).IsNotNull();
        await Assert.That(calls).IsEqualTo(1);
        await AssertCorrectionDisclosureBufferClearedAsync(borrowed);
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, fixture.Database.ReviewerId);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }
}
