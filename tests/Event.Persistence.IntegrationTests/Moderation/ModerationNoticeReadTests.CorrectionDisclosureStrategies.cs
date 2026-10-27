using System.Data;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retry_enabled_provider_can_disclose_once_inside_the_owned_native_transaction(bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var template = fixture.Database.Context();
        var options = (DbContextOptions<ExploreDbContext>)template.GetService<IDbContextOptions>();
        await using var reader = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .ReplaceService<IExecutionStrategyFactory, CorrectionDisclosureRetryStrategyFactory>().Options)
        { TenantContext = fixture.Database };
        await using var colocatedPrivacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(reader) : null;
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, colocatedPrivacy);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var privacy = (colocatedPrivacy ?? fixture.Privacy).Authority(reader, fixture.Clock);
        var repository = CorrectionDisclosureReader(reader, fixture, fixture.Database.ReviewerId, privacy);
        ReadOnlyMemory<byte> borrowed = default;
        int calls = 0;

        await Assert.That(await repository.DiscloseCorrectionAsync(CorrectionPreparationRequest(pending, grant), async (bytes, _) =>
        {
            calls++;
            borrowed = bytes;
            await Assert.That(reader.Database.CurrentTransaction).IsNotNull();
            await Assert.That(reader.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(IsolationLevel.Serializable);
            await Assert.That(reader.IdentityFenceOwnsTransaction).IsEqualTo(colocated);
            await AssertCorrectionExplanationAsync(bytes, pending.Request.Intent);
        })).IsTrue();
        await Assert.That(calls).IsEqualTo(1);
        await AssertCorrectionDisclosureBufferClearedAsync(borrowed);
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, fixture.Database.ReviewerId);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    public async Task Retrying_outer_operation_cannot_replay_correction_output_or_lose_its_ambient_strategy()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        await using var template = fixture.Database.Context();
        var options = (DbContextOptions<ExploreDbContext>)template.GetService<IDbContextOptions>();
        await using var reader = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .ReplaceService<IExecutionStrategyFactory, CorrectionDisclosureRetryStrategyFactory>().Options)
        { TenantContext = fixture.Database };
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        var repository = CorrectionDisclosureReader(reader, fixture, fixture.Database.ReviewerId, privacy);
        var strategy = reader.Database.CreateExecutionStrategy();
        Exception? observed = null;
        int calls = 0;
        await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await repository.DiscloseCorrectionAsync(CorrectionPreparationRequest(pending, grant), (_, _) =>
                {
                    calls++;
                    return Task.CompletedTask;
                });
            }
            catch (Exception exception) { observed = exception; }
            await Assert.That(ExecutionStrategy.Current).IsSameReferenceAs(strategy);
        });
        await Assert.That(observed is InvalidOperationException).IsTrue();
        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(ExecutionStrategy.Current).IsNull();
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, fixture.Database.ReviewerId);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
    }
}
