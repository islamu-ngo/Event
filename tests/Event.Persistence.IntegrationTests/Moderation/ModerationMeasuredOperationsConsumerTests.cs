using System.Data;
using System.Security.Claims;
using Explore.Application.Services.Moderation;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationConsumerMeasurementReceiptObservationTests
{
    [Test]
    [Arguments("exact")]
    [Arguments("changed-product")]
    [Arguments("missing-catalogue")]
    [Arguments("missing-native-receipt")]
    [Arguments("disposed")]
    public async Task Measured_operations_reuses_authentic_payload_measurements_but_does_not_fill_other_consumer_gaps(string change)
    {
        await using var database = await SourceDatabase.CreateAsync();
        using var catalogue = await DeployedCatalogue.CreateAsync(database, "exact");
        using var fixture = await Fixture.CreateAsync(database, catalogue.Artifact);
        await using var context = database.Context();
        var clock = new ObservationClock();
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, database.AdministratorId.ToString())], "measured-operations"))
            }
        };
        var admin = new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var evidence = await BorrowPublicationEvidenceAsync(database, context, fixture, clock, default);
        var session = (ModerationConsumerEvidenceSession)evidence;
        var native = await session.AcquireNativeAsync(transaction.TransactionId, Now, default);
        await Assert.That(native).IsNotNull();
        var profile = native!.Policy.Profiles.Single(row => row.Definition.Rule == fixture.Receipt.Binding.Rule).Definition.Id;
        var owner = new ModerationMeasuredOperationsSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, clock),
            database.Inventory, clock,
            new ModerationConsumerConformanceReader(database.Inventory, new ModerationMeasurementCatalogueSource(), clock),
            database.ConsumerRuntimeSources);
        if (change == "changed-product") fixture.Runtime.ProductArtifactDigest = new string('9', 64);
        if (change == "missing-catalogue") catalogue.Remove();
        if (change == "missing-native-receipt")
            await context.ModerationQualificationMutationReceipts.Where(row => row.SourceId == fixture.Receipt.ReceiptId).ExecuteDeleteAsync();
        if (change == "disposed") evidence.Dispose();
        var observed = await owner.ObserveAsync(session, transaction.TransactionId, profile, fixture.Receipt.Binding,
            database.AdministratorId, database.ReviewerId, database.ReviewerId, database.AdministratorId, Now, default);
        if (change == "disposed") await Assert.That(observed).IsNull();
        else
        {
            await Assert.That(observed).IsNotNull();
            var payload = observed!.Evidence.Consumers.Single(row => row.ConsumerId == "resource.payload");
            await Assert.That(payload.Measurement is not null).IsEqualTo(change == "exact");
            if (change == "exact") await Assert.That(payload.Measurement!.Id).IsEqualTo(fixture.Receipt.ReceiptId);
            await Assert.That(observed.Evidence.Consumers.Where(row => row.ConsumerId != "resource.payload")
                .All(row => row.Measurement is null)).IsTrue();
            await Assert.That(observed.Evidence.IsComplete(
                database.Inventory.RequiredConsumers(fixture.Receipt.Binding.Capability, fixture.Receipt.Binding.Mode),
                (int)fixture.Receipt.Binding.Mode, observed.Evidence.RecognitionPurpose, Now)).IsFalse();
            await Assert.That(observed.Evidence.Recovery).IsNull();
        }
        await transaction.RollbackAsync();
        await AssertOriginalCustodyAsync(database, fixture);
    }
}
