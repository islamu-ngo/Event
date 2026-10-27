using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Whole_instance_session_reloads_complete_reservation_verifies_artifact_and_retains_neutral_snapshot(bool mapped)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        await database.PublishAsync(database.Request());
        var foreign = await PublishExportTenantAsync(database);
        var protection = new EphemeralDataProtectionProvider();
        ConfigurationImportSessionCreatedResult created;
        ModerationConfigurationImportReservationSet reservation;
        ConfigurationManifestV1Alpha2 original;
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = mapped
                ? [ModerationConfigurationImportSections.Instance, ModerationConfigurationImportSections.Tenant]
                : [ModerationConfigurationImportSections.Instance, ModerationConfigurationImportSections.Tenant, "tenant.legal_documents"],
            Mappings = mapped ? new Dictionary<string, string> { ["source-alias"] = $"tenant:export-{foreign.TenantId:N}" }
                : new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = ["legal-review"]
        };
        await using (var context = database.Context())
        {
            context.LegalDocuments.AddRange(LegalImportDraft(database.TenantId), LegalImportDraft(foreign.TenantId));
            await context.SaveChangesAsync();
            await SeedExportPaidPolicyAsync(context);
            var reader = CurrentExportReader(database, context);
            original = new ConfigurationImportArtifactParser().Parse((await reader.ReadAsync(
                ConfigurationManifestExportView.Overrides, CancellationToken.None)).Utf8Json).Manifest;
            var source = new ConfigurationImportArtifactParser().Parse(await FanoutArtifactAsync(database, context)).Manifest;
            source = source with
            {
                Spec = source.Spec with
                {
                    Tenants = source.Spec.Tenants.Select(tenant =>
            {
                var document = ModerationConfigurationImportSections.FromWire(tenant.Spec.Moderation!);
                var changed = new ModerationConfigurationDocument(document.SchemaVersion, document.CatalogueRevision,
                    document.Modes, document.Classes.Select(value => value with { Meaning = "Imported complete meaning." }).ToArray(),
                    document.Reasons, document.Profiles, document.SupportedProfiles);
                return tenant with
                {
                    Metadata = mapped && tenant.Metadata.Name == $"export-{foreign.TenantId:N}"
                        ? tenant.Metadata with { Name = "source-alias" } : tenant.Metadata,
                    Spec = tenant.Spec with { Moderation = ModerationConfigurationImportSections.ToWire(changed) }
                };
            }).ToArray()
                }
            };
            await using var human = ImportHuman(database, context, protection, new ImportExportOwnerClock());
            await human.InitializeAsync();
            var imports = human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
            created = await imports.CreateAsync(ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(source),
                CancellationToken.None);
            var preview = await imports.PreviewAsync(created.SessionId, created.AccessToken, request, CancellationToken.None);
            await Assert.That(preview.IsApplyReady).IsTrue();
            reservation = (await ReadImportSessionAsync(context, created.SessionId,
                ConfigurationImportTarget.ForInstance())).ModerationReservation!;
            await imports.PreviewAsync(created.SessionId, created.AccessToken, request, CancellationToken.None);
            await Assert.That((await ReadImportSessionAsync(context, created.SessionId,
                ConfigurationImportTarget.ForInstance())).ModerationReservation!.Digest)
                .IsEqualTo(reservation.Digest);
        }
        await using var applyContext = database.Context();
        await using var applyHuman = ImportHuman(database, applyContext, protection, new ImportExportOwnerClock());
        await applyHuman.InitializeAsync();
        var apply = applyHuman.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
        var receipt = await apply.ApplyAsync(created.SessionId, created.AccessToken, request, null, null, CancellationToken.None);
        await Assert.That(receipt.FidelityVerified).IsTrue();
        await Assert.That(receipt.EffectStatus).IsEqualTo(ConfigurationImportEffectStatus.Completed);
        await Assert.That(receipt.SnapshotAvailable).IsTrue();
        var actual = new ConfigurationImportArtifactParser().Parse((await apply.ReadSnapshotAsync(
            ConfigurationManifestExportView.Overrides, CancellationToken.None)).Utf8Json).Manifest;
        foreach (var tenant in actual.Spec.Tenants)
            await Assert.That(tenant.Spec.Moderation!.Classes.Single().Meaning).IsEqualTo("Imported complete meaning.");
        var operation = await new ConfigurationImportOperationRepository(applyContext).GetByIdAsync(
            receipt.OperationId, "instance", CancellationToken.None);
        var snapshot = await new ConfigurationImportArtifactRepository(applyContext, protection).ReadAsync(
            new(operation!.SnapshotArtifactHandleId!.Value), CancellationToken.None);
        var rollback = new ConfigurationImportArtifactParser().Parse(snapshot).Manifest;
        await Assert.That(rollback.Spec.Instance.Moderation).IsNull();
        foreach (var tenant in original.Spec.Tenants)
        {
            var retained = rollback.Spec.Tenants.Single(value => value.Metadata.Name == tenant.Metadata.Name);
            await Assert.That(ModerationConfigurationImportSections.Digest(
                ModerationConfigurationImportSections.FromWire(retained.Spec.Moderation!))).IsEqualTo(
                ModerationConfigurationImportSections.Digest(
                    ModerationConfigurationImportSections.FromWire(tenant.Spec.Moderation!)));
            await Assert.That(retained.Spec.LegalDocuments["TenantTerms"].Localizations.Single().Markdown)
                .IsEqualTo("Imported terms.");
        }
        await Assert.That(applyContext.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(applyContext.ModerationTrustDomainId).IsNull();
        await Assert.That(async () => await apply.ApplyAsync(created.SessionId, created.AccessToken,
            request, null, null, CancellationToken.None)).Throws<ConfigurationImportSessionException>();
    }
}
