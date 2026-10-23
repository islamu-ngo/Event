using System.Collections.Immutable;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using ISLAMU.Moderation.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationConsumerMeasurementReceiptObservationTests
{
    [Test]
    [Arguments("exact")]
    [Arguments("absent-product")]
    [Arguments("changed-product")]
    [Arguments("receipt-product")]
    [Arguments("primary-fingerprint")]
    [Arguments("receipt-provider")]
    [Arguments("receipt-version")]
    [Arguments("partial-cases")]
    [Arguments("different-cases")]
    [Arguments("extra-case")]
    [Arguments("reordered-cases")]
    [Arguments("catalogue-runtime")]
    [Arguments("catalogue-provider")]
    [Arguments("catalogue-version")]
    [Arguments("catalogue-product")]
    [Arguments("catalogue-configuration")]
    [Arguments("missing-catalogue")]
    [Arguments("mutated-catalogue")]
    [Arguments("final-catalogue")]
    [Arguments("final-runtime")]
    [Arguments("final-source")]
    [Arguments("final-measurement")]
    [Arguments("final-expiry")]
    [Arguments("already-expired")]
    [Arguments("foreign-transaction")]
    [Arguments("foreign-publication")]
    public async Task AuthenticReceipt_RegistersOnlyAnExactCurrentNativePayloadJoin(string change)
    {
        await using var database = await SourceDatabase.CreateAsync();
        using var catalogue = await DeployedCatalogue.CreateAsync(database, change);
        using var fixture = await Fixture.CreateAsync(database, catalogue.Artifact, receipt => change switch
        {
            "receipt-product" => receipt with { ProductArtifactDigest = new string('9', 64) },
            "primary-fingerprint" => receipt with { PrimaryConfigurationDigest = new string('9', 64) },
            "receipt-provider" => receipt with { ProviderCode = "sqlite" },
            "receipt-version" => receipt with { ProviderVersion = "different-primary-version" },
            "partial-cases" => receipt with { CaseIds = receipt.CaseIds.RemoveAt(0), ExecutedCount = 2 },
            "different-cases" => receipt with
            {
                CaseIds = receipt.CaseIds.SetItem(0, "case.foreign").Order(StringComparer.Ordinal).ToImmutableArray()
            },
            "extra-case" => receipt with
            {
                CaseIds = receipt.CaseIds.Add("case.foreign"),
                ExecutedCount = 4
            },
            "reordered-cases" => receipt with { CaseIds = receipt.CaseIds.Reverse().ToImmutableArray() },
            _ => receipt
        });
        var clock = new ObservationClock();
        await using var context = database.Context();
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, fixture.Decision.Subject,
            database.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(), fixture.Decision.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationCapabilityRegistration? registration = null;
        bool entered = false;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            entered = true;
            await Assert.That((await participant.ResolveAsync(session, operation, token)).Outcome)
                .IsEqualTo(ModerationOperationOutcome.Known);
            var original = context.Database.CurrentTransaction!;
            using var evidence = await BorrowPublicationEvidenceAsync(database, context, fixture, clock, token);
            // This is actual native receipt requalification, not just a signature-only verifier result.
            await Assert.That(await participant.AcquireConsumerMeasurementReceiptAsync(original.TransactionId,
                fixture.Receipt.Binding, "resource.payload", fixture.Receipt.ReceiptId, Now, token)).IsNotNull();
            var authentic = await evidence.AcquireMeasurementAsync(fixture.Receipt.Binding, "resource.payload", Now, token);
            await Assert.That(authentic).IsNotNull();
            await Assert.That(authentic!.TransactionId).IsEqualTo(original.TransactionId);
            await Assert.That(authentic.Runtime.ProductArtifactDigest).IsEqualTo(new string('d', 64));
            await RecordActualPrimaryDatabaseAsync(evidence, token);
            if (change == "absent-product") fixture.Runtime.ProductArtifactDigest = null;
            if (change == "changed-product") fixture.Runtime.ProductArtifactDigest = new string('9', 64);
            if (change == "missing-catalogue") catalogue.Remove();
            if (change == "mutated-catalogue") await catalogue.MutateAsync();
            if (change == "already-expired") clock.UtcNow = fixture.Receipt.ValidUntil;
            var boundary = new ChangingCatalogueBoundary(async (read, cancellation) =>
            {
                if (read == 2 && change == "final-catalogue") await catalogue.MutateAsync();
                if (read == 2 && change == "final-runtime") fixture.Runtime.ProductArtifactDigest = new string('9', 64);
                if (read == 2 && change == "final-expiry") clock.UtcNow = fixture.Receipt.ValidUntil;
                if (read == 2 && change == "final-measurement")
                {
                    context.ModerationConsumerMeasurementReceipts.Remove(
                        await context.ModerationConsumerMeasurementReceipts.SingleAsync(
                            row => row.Id == fixture.Receipt.ReceiptId, cancellation));
                    await context.SaveChangesAsync(cancellation);
                }
                if (read == 1 && change == "final-source")
                {
                    context.ModerationQualificationMutationReceipts.Remove(
                        await context.ModerationQualificationMutationReceipts.SingleAsync(
                            row => row.SourceId == fixture.Receipt.InstallationId, cancellation));
                    await context.SaveChangesAsync(cancellation);
                }
            });
            registration = await new ModerationConsumerConformanceReader(database.Inventory, boundary, clock)
                .ReadAsync(evidence, fixture.Runtime,
                    change == "foreign-transaction" ? Guid.CreateVersion7() : original.TransactionId,
                    change == "foreign-publication"
                        ? fixture.Receipt.Binding with { PublicationId = Guid.CreateVersion7() } : fixture.Receipt.Binding,
                    Now, token);
            await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, original)).IsTrue();
            await Assert.That(evidence.TransactionId).IsEqualTo(original.TransactionId);
            if (change == "exact")
            {
                await Assert.That(registration).IsNotNull();
                await Assert.That(registration!.Conformance).IsEqualTo(ModerationNativeConformance.Passed);
                await Assert.That(registration.Binding).IsEqualTo(fixture.Receipt.Binding);
                await Assert.That(registration.SourceId).IsEqualTo(fixture.Receipt.InstallationId);
                await Assert.That(registration.SourceRevision).IsEqualTo(1L);
                await Assert.That(registration.WriterGeneration).IsEqualTo(fixture.Runtime.Generation);
                await Assert.That(registration.InventoryFingerprint).IsEqualTo(database.Inventory.Fingerprint);
                await Assert.That(registration.Contract).IsEqualTo(database.Inventory.Consumers["resource.payload"].Contract);
                await Assert.That(registration.VerifiedAt).IsEqualTo(Now);
                await Assert.That(registration.ValidUntil).IsEqualTo(fixture.Receipt.ValidUntil);
            }
            else await Assert.That(registration).IsNull();
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        await Assert.That(entered).IsTrue();
        await AssertOriginalCustodyAsync(database, fixture);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisposedOrReplacedOriginalTransaction_CannotRegisterPreviouslyAuthenticPayload(bool replace)
    {
        await using var database = await SourceDatabase.CreateAsync();
        using var catalogue = await DeployedCatalogue.CreateAsync(database, "exact");
        using var fixture = await Fixture.CreateAsync(database, catalogue.Artifact);
        await using var context = database.Context();
        var clock = new ObservationClock();
        await using var original = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var evidence = await BorrowPublicationEvidenceAsync(database, context, fixture, clock, CancellationToken.None);
        var reader = new ModerationConsumerConformanceReader(database.Inventory,
            new ModerationMeasurementCatalogueSource(), clock);
        await Assert.That(await reader.ReadAsync(evidence, fixture.Runtime, original.TransactionId,
            fixture.Receipt.Binding, Now, CancellationToken.None)).IsNotNull();
        await RecordActualPrimaryDatabaseAsync(evidence, CancellationToken.None);
        if (replace)
        {
            await original.RollbackAsync();
            await original.DisposeAsync();
            await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            // Passing the old identity must also fail: the native borrow checks the actual current transaction.
            await Assert.That(await reader.ReadAsync(evidence, fixture.Runtime, original.TransactionId,
                fixture.Receipt.Binding, Now, CancellationToken.None)).IsNull();
            await Assert.That(await reader.ReadAsync(evidence, fixture.Runtime, replacement.TransactionId,
                fixture.Receipt.Binding, Now, CancellationToken.None)).IsNull();
            await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, replacement)).IsTrue();
        }
        else
        {
            evidence.Dispose();
            await Assert.That(await reader.ReadAsync(evidence, fixture.Runtime, original.TransactionId,
                fixture.Receipt.Binding, Now, CancellationToken.None)).IsNull();
            await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, original)).IsTrue();
        }
        await AssertOriginalCustodyAsync(database, fixture);
    }

    private static async Task RecordActualPrimaryDatabaseAsync(
        IModerationConsumerEvidenceSession evidence, CancellationToken token)
    {
        var primary = await evidence.AcquirePrimaryDatabaseAsync(Now, token);
        await Assert.That(primary).IsNotNull();
        Event.Tests.Shared.Moderation.ModerationMeasurementJournalAttribute.RecordDatabaseObservation(
            TUnit.Core.TestContext.Current!, primary!.ProviderCode, primary.ProviderVersion, primary.ConfigurationDigest);
    }

    private static (string Digest, string Provider, string Version) ReadPrimaryFacts(ExploreDbContext context)
    {
        var identity = context.GetService<IDbContextOptions>().FindExtension<PrimaryDatabasePublicIdentityOptionsExtension>()
            ?? throw new InvalidOperationException("The primary fixture must use native structured provider composition.");
        return (identity.PublicDigest, identity.Provider.ToString(), context.Database.GetDbConnection().ServerVersion);
    }

    private static async Task<IModerationConsumerEvidenceSession> BorrowPublicationEvidenceAsync(
        SourceDatabase database, ExploreDbContext context, Fixture fixture, TimeProvider clock, CancellationToken token)
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, database.AdministratorId.ToString())], "native-conformance"))
            }
        };
        var admin = new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        var source = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, fixture.Receipt.Binding.PublicationId, token)
            ?? throw new InvalidOperationException("The original primary transaction must acquire actual source authority.");
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory,
            null, clock, database.ConsumerRuntimeSources, new ModerationMeasurementReceiptVerifier());
        var selected = await publications.AcquireSelectedAsync(context.Database.CurrentTransaction!.TransactionId,
            database.HostId, database.Scope, fixture.Receipt.Binding.PolicyId, false, token)
            ?? throw new InvalidOperationException("The native draft publication must remain selected.");
        var policy = ModerationPolicyPublicationProjection.Restore(selected, database.Inventory)
            ?? throw new InvalidOperationException("The native publication must retain its complete inventory.");
        return publications.BorrowConsumerEvidence(source, policy);
    }

    private static async Task AssertOriginalCustodyAsync(SourceDatabase database, Fixture fixture)
    {
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var accepted = await context.ModerationConsumerMeasurementReceipts.SingleAsync();
        await Assert.That(accepted.SignedToken).IsEqualTo(fixture.Token);
        await Assert.That(accepted.Id).IsEqualTo(fixture.Receipt.ReceiptId);
        await Assert.That(accepted.IssuerEnrollmentId).IsEqualTo(fixture.EnrollmentId);
        await Assert.That(accepted.WriterGeneration).IsEqualTo(6L);
        await Assert.That(accepted.AcceptedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(accepted.TokenDigest).IsEqualTo(
            Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(fixture.Token))));
        await Assert.That(await context.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.SourceId == fixture.Receipt.InstallationId)).IsTrue();
        await Assert.That(await context.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.SourceId == fixture.Receipt.ReceiptId
                && row.PrincipalId == database.AdministratorId && row.QualificationRevision == 6)).IsTrue();
        await Assert.That((await context.ModerationMeasurementIssuerEnrollments.SingleAsync()).CatalogueDigest)
            .IsEqualTo(fixture.Receipt.CatalogueDigest);
        var installed = await context.ModerationConsumerInstallations.SingleAsync();
        await Assert.That(installed.Id).IsEqualTo(fixture.Receipt.InstallationId);
        await Assert.That(installed.Revision).IsEqualTo(1L);
        await Assert.That(installed.WithdrawnAt).IsNull();
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(8L);
        await Assert.That(await context.ModerationSanctions.CountAsync()).IsEqualTo(0);
        var selected = await context.ModerationPolicySelections.SingleAsync();
        await Assert.That(selected.ActivePublicationId).IsNull();
    }

    /// <summary>Changes only the external catalogue boundary; all reads still use the deployed-file adapter.</summary>
    private sealed class ChangingCatalogueBoundary(Func<int, CancellationToken, Task> beforeRead)
        : IModerationMeasurementCatalogueSource
    {
        private readonly ModerationMeasurementCatalogueSource source = new();
        private int reads;
        public async Task<ModerationMeasurementCatalogueArtifact?> ReadAsync(
            string approvedDigest, CancellationToken cancellationToken = default)
        {
            await beforeRead(++reads, cancellationToken);
            return await source.ReadAsync(approvedDigest, cancellationToken);
        }
    }

    /// <summary>
    /// Controlled externally approved measurement-host bytes, not proof of deployed consumer execution.
    /// Native issuer enrollment approves their digest; native signature and source checks remain mandatory.
    /// </summary>
    private sealed class DeployedCatalogue(byte[] bytes, ModerationMeasurementCatalogueArtifact artifact) : IDisposable
    {
        public ModerationMeasurementCatalogueArtifact Artifact { get; } = artifact;
        private string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "moderation", "catalogues", Artifact.Digest);
        private string FilePath => Path.Combine(DirectoryPath, "catalogue.v1.json");

        public static async Task<DeployedCatalogue> CreateAsync(SourceDatabase database, string change,
            string consumerId = "resource.payload")
        {
            await using var context = database.Context();
            var facts = ReadPrimaryFacts(context);
            var identity = Guid.CreateVersion7().ToString("N");
            var catalogue = new ModerationMeasurementCatalogue(1, consumerId, Environment.Version.ToString(),
                PrimaryDatabaseProvider.Sqlite.ToString(), facts.Version, new string('e', 64), new string('d', 64),
                ModerationMeasurementCatalogue.Bundles.Select((bundle, index) => new ModerationMeasurementCatalogueJob(
                    bundle, "/*/*/ControlledMeasurementHost/*",
                    ImmutableArray.Create(new ModerationMeasurementCatalogueCase(
                        $"case.{index}", $"journal.{index}", $"node.{index}", "ControlledMeasurementHost",
                        "Outcome", "Outcome", "Tests.ControlledMeasurementHost", "Outcome", identity))))
                    .ToImmutableArray());
            catalogue = change switch
            {
                "catalogue-runtime" => catalogue with { RuntimeVersion = "different-runtime" },
                "catalogue-provider" => catalogue with { ProviderCode = PrimaryDatabaseProvider.PostgreSql.ToString() },
                "catalogue-version" => catalogue with { ProviderVersion = "different-primary-version" },
                "catalogue-product" => catalogue with { ProductArtifactDigest = new string('9', 64) },
                "catalogue-configuration" => catalogue with { ConfigurationDigest = new string('9', 64) },
                _ => catalogue
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(catalogue,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var fixture = new DeployedCatalogue(bytes, new(digest, ModerationMeasurementCatalogue.Parse(bytes)));
            if (Directory.Exists(fixture.DirectoryPath))
                throw new InvalidOperationException("The invocation must own a unique deployed catalogue directory.");
            Directory.CreateDirectory(fixture.DirectoryPath);
            await using var file = new FileStream(fixture.FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.WriteAsync(bytes);
            return fixture;
        }

        public Task MutateAsync() => File.WriteAllBytesAsync(FilePath, [.. bytes, (byte)' ']);
        public void Remove() => File.Delete(FilePath);
        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
