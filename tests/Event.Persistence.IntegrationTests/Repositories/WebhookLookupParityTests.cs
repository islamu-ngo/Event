// ABOUTME: EF model and source parity tests for normalized webhook lookup tables.
// ABOUTME: Verifies stable enum IDs/codes, runtime seeds, literal migration rows, relational FKs, and DTO metadata.

using System.Text;
using System.Text.RegularExpressions;
using Explore.Application.DTOs.Webhooks;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class WebhookLookupParityTests
{
    private static readonly LookupCase[] LookupCases =
    [
        new(
            typeof(WebhookConsumerKindLookup),
            typeof(WebhookConsumerKind),
            "webhook_consumer_kinds",
            [new(typeof(WebhookConsumer), "ConsumerKindId")],
            RuntimeSeedOnlyIds: [(int)WebhookConsumerKind.Instance]),
        new(typeof(WebhookConsumerStatusLookup), typeof(WebhookConsumerStatus), "webhook_consumer_statuses", [new(typeof(WebhookConsumer), "StatusId")]),
        new(typeof(WebhookProviderModeLookup), typeof(WebhookProviderMode), "webhook_provider_modes", [new(typeof(WebhookConsumer), "ProviderModeId"), new(typeof(WebhookDeliveryPlanSnapshot), "ProviderModeId"), new(typeof(WebhookProviderPublication), "ModeSnapshotId")]),
        new(typeof(WebhookProviderKindLookup), typeof(WebhookProviderKind), "webhook_provider_kinds", [new(typeof(WebhookConsumerProviderBinding), "ProviderKindId"), new(typeof(WebhookProviderPublication), "ProviderKindId")]),
        new(typeof(WebhookProviderCapabilityLookup), typeof(WebhookProviderCapability), "webhook_provider_capabilities", [], IncludeZero: false, IndividualFlagsOnly: true, RequiresLiteralMigrationRows: false),
        new(typeof(WebhookEndpointStatusLookup), typeof(WebhookEndpointStatus), "webhook_endpoint_statuses", [new(typeof(WebhookEndpoint), "StatusId")]),
        new(typeof(WebhookLocalDeliveryStatusLookup), typeof(WebhookLocalDeliveryStatus), "webhook_local_delivery_statuses", [new(typeof(WebhookLocalTargetSnapshot), "DeliveryStatusId")]),
        new(typeof(WebhookBulkReplayStatusLookup), typeof(WebhookBulkReplayStatus), "webhook_bulk_replay_statuses", [new(typeof(WebhookBulkReplayOperation), "StatusId")], RequiresLiteralMigrationRows: false),
        new(typeof(WebhookPendingWorkDecisionLookup), typeof(WebhookPendingWorkDecision), "webhook_pending_work_decisions", [], RequiresLiteralMigrationRows: false),
        new(typeof(WebhookRetentionSubjectKindLookup), typeof(WebhookRetentionSubjectKind), "webhook_retention_subject_kinds", [new(typeof(WebhookRetentionHold), "SubjectKindId")], RequiresLiteralMigrationRows: false),
        new(typeof(WebhookAuditActionLookup), typeof(WebhookAuditAction), "webhook_audit_actions", [new(typeof(WebhookAuditEvent), "ActionId")], RequiresLiteralMigrationRows: false),
        new(typeof(WebhookAuditOutcomeLookup), typeof(WebhookAuditOutcome), "webhook_audit_outcomes", [new(typeof(WebhookAuditEvent), "OutcomeId")], RequiresLiteralMigrationRows: false),
        new(typeof(WebhookAuditPrincipalKindLookup), typeof(WebhookAuditPrincipalKind), "webhook_audit_principal_kinds", [new(typeof(WebhookAuditEvent), "PrincipalKindId")], RequiresLiteralMigrationRows: false),
        new(typeof(WebhookAuditScopeKindLookup), typeof(WebhookAuditScopeKind), "webhook_audit_scope_kinds", [new(typeof(WebhookAuditEvent), "EffectiveScopeKindId")], RequiresLiteralMigrationRows: false),
        new(typeof(WebhookAuditTargetKindLookup), typeof(WebhookAuditTargetKind), "webhook_audit_target_kinds", [new(typeof(WebhookAuditEvent), "TargetKindId")], RequiresLiteralMigrationRows: false),
        new(typeof(WebhookDeliveryAttemptOutcomeLookup), typeof(WebhookDeliveryAttemptOutcome), "webhook_delivery_attempt_outcomes", [new(typeof(WebhookDeliveryAttempt), "OutcomeId")]),
        new(typeof(WebhookProviderBindingVerificationStateLookup), typeof(WebhookProviderBindingVerificationState), "webhook_provider_binding_verification_states", [new(typeof(WebhookConsumerProviderBinding), "VerificationStateId")]),
        new(typeof(IncomingWebhookMessageStatusLookup), typeof(IncomingWebhookMessageStatus), "incoming_webhook_message_statuses", [new(typeof(IncomingWebhookMessage), "StatusId")]),
        new(typeof(IncomingWebhookProcessingAttemptOutcomeLookup), typeof(IncomingWebhookProcessingAttemptOutcome), "incoming_webhook_processing_attempt_outcomes", [new(typeof(IncomingWebhookProcessingAttempt), "OutcomeId")]),
        new(typeof(IncomingWebhookSettlementSourceLookup), typeof(IncomingWebhookSettlementSource), "incoming_webhook_settlement_sources", [new(typeof(IncomingWebhookMessage), "SettlementSourceId")], IsRequired: false, IncludeZero: false),
        new(typeof(IncomingWebhookRedriveResultLookup), typeof(IncomingWebhookRedriveResult), "incoming_webhook_redrive_results", [new(typeof(IncomingWebhookRedriveRecord), "ResultId")]),
        new(typeof(WebhookProviderPublicationStatusLookup), typeof(WebhookProviderPublicationStatus), "webhook_provider_publication_statuses", [new(typeof(WebhookProviderPublication), "StatusId")]),
        new(typeof(WebhookProviderPublicationAttemptOutcomeLookup), typeof(WebhookProviderPublicationAttemptOutcome), "webhook_provider_publication_attempt_outcomes", [new(typeof(WebhookProviderPublicationAttempt), "OutcomeId")]),
        new(typeof(WebhookPayloadProvenanceLookup), typeof(WebhookPayloadProvenance), "webhook_payload_provenances", [new(typeof(WebhookMessage), "PayloadProvenanceId"), new(typeof(IncomingWebhookMessage), "PayloadProvenanceId")])
    ];

    [Test]
    public async Task EfModel_UsesIndependentStableLookupTablesAndRequiredForeignKeys()
    {
        await using var context = CreateModelContext();
        var model = context.Model;

        foreach (var lookupCase in LookupCases)
        {
            var lookup = model.FindEntityType(lookupCase.LookupType)!;
            await Assert.That(lookup.GetTableName()).IsEqualTo(lookupCase.TableName);
            await Assert.That(lookup.FindPrimaryKey()!.Properties.Single().ClrType).IsEqualTo(typeof(int));
            await Assert.That(lookup.FindProperty("Id")!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
            await Assert.That(lookup.FindProperty("MasterCode")!.GetMaxLength()).IsEqualTo(100);
            await Assert.That(lookup.FindProperty("FullName")!.GetMaxLength()).IsEqualTo(200);
            await Assert.That(lookup.FindProperty("Description")!.GetMaxLength()).IsEqualTo(500);
            await Assert.That(lookup.GetIndexes().Any(index =>
                index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["MasterCode"]))).IsTrue();

            foreach (var owner in lookupCase.Owners)
            {
                var ownerEntity = model.FindEntityType(owner.OwnerType)!;
                var foreignKey = ownerEntity.GetForeignKeys().Single(candidate =>
                    candidate.PrincipalEntityType.ClrType == lookupCase.LookupType &&
                    candidate.Properties.Select(property => property.Name).SequenceEqual([owner.ForeignKeyProperty]));
                await Assert.That(foreignKey.IsRequired).IsEqualTo(lookupCase.IsRequired);
                await Assert.That(foreignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
            }
        }
    }

    [Test]
    public async Task EnumRuntimeSeederAndCurrentBaseline_ContainExactStableLookups()
    {
        var root = FindRepositoryRoot();
        var seeder = await File.ReadAllTextAsync(
            Path.Combine(root, "src/Explore.Persistence/Seed/LookupTableSeeder.cs"));
        var migrationSource = await File.ReadAllTextAsync(Directory
            .EnumerateFiles(Path.Combine(root, "src/Explore.Persistence/Migrations"), "*_Init.cs")
            .Single());

        foreach (var lookupCase in LookupCases)
        {
            foreach (var value in Enum.GetValues(lookupCase.EnumType))
            {
                var name = Enum.GetName(lookupCase.EnumType, value)!;
                var code = ToMasterCode(name);
                var id = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                if (id == 0 && !lookupCase.IncludeZero)
                {
                    continue;
                }

                if (lookupCase.IndividualFlagsOnly && (id <= 0 || (id & (id - 1)) != 0))
                {
                    continue;
                }

                var seedPattern = $"Id = (int){lookupCase.EnumType.Name}.{name}, MasterCode = \"{code}\"";
                await Assert.That(seeder).Contains(seedPattern);
                await Assert.That(migrationSource).Contains($"name: \"{lookupCase.TableName}\"");
            }
        }
    }

    [Test]
    public async Task WebhookPersistence_UsesNoModelSeedOrNativeEnumAndDtosExposeLookupTriples()
    {
        var root = FindRepositoryRoot();
        var configurationSource = string.Join('\n', Directory.GetFiles(
                Path.Combine(root, "src/Explore.Persistence/Configurations/Entities"),
                "*Webhook*.cs")
            .Select(File.ReadAllText));
        var persistenceSource = string.Join('\n', Directory.GetFiles(
                Path.Combine(root, "src/Explore.Persistence"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains("/Migrations/", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        await Assert.That(configurationSource).DoesNotContain("HasData(");
        await Assert.That(persistenceSource).DoesNotContain("HasPostgresEnum<Webhook");

        await AssertLookupTripleAsync(typeof(WebhookConsumerDto), "ConsumerKind");
        await AssertLookupTripleAsync(typeof(WebhookConsumerDto), "Status");
        await AssertLookupTripleAsync(typeof(WebhookConsumerDto), "ProviderMode");
        await AssertLookupTripleAsync(typeof(WebhookProviderCapabilityDto), "Capability");
        await AssertLookupTripleAsync(typeof(WebhookEndpointDto), "Status");
        await AssertLookupTripleAsync(typeof(WebhookEndpointDto), "ProviderMode");
        await AssertLookupTripleAsync(typeof(WebhookDeliveryAttemptDto), "Outcome");
        await AssertLookupTripleAsync(typeof(WebhookBulkReplayOperationDto), "Status");
    }

    private static async Task AssertLookupTripleAsync(Type dtoType, string prefix)
    {
        await Assert.That(dtoType.GetProperty(prefix + "Id")?.PropertyType).IsEqualTo(typeof(int));
        await Assert.That(dtoType.GetProperty(prefix + "Code")?.PropertyType).IsEqualTo(typeof(string));
        await Assert.That(dtoType.GetProperty(prefix + "Name")?.PropertyType).IsEqualTo(typeof(string));
    }

    private static ExploreDbContext CreateModelContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=webhook_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root containing Explore.slnx was not found.");
    }

    private static string ToMasterCode(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (index > 0 && char.IsUpper(character) &&
                (char.IsLower(name[index - 1]) ||
                 (index + 1 < name.Length && char.IsLower(name[index + 1]))))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    private sealed record LookupCase(
        Type LookupType,
        Type EnumType,
        string TableName,
        IReadOnlyList<LookupOwner> Owners,
        bool IsRequired = true,
        bool IncludeZero = true,
        bool IndividualFlagsOnly = false,
        bool RequiresLiteralMigrationRows = true,
        IReadOnlyList<int>? RuntimeSeedOnlyIds = null);

    private sealed record LookupOwner(Type OwnerType, string ForeignKeyProperty);
}
