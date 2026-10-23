using System.Security.Claims;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Whole_instance_session_reloads_complete_reservation_verifies_artifact_and_retains_neutral_snapshot(bool mapped)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
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
            var (manager, previews, _) = FanoutSessionServices(database, context, protection, privacy);
            created = await previews.CreateInstanceAsync(ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(source),
                CancellationToken.None);
            var preview = await previews.PreviewInstanceAsync(created.SessionId, created.AccessToken, request, CancellationToken.None);
            await Assert.That(preview.IsApplyReady).IsTrue();
            reservation = (await manager.ReadArtifactForPreviewAsync(created.SessionId, ConfigurationImportTarget.ForInstance(),
                created.AccessToken, Now.UtcDateTime, CancellationToken.None)).ModerationReservation!;
            await previews.PreviewInstanceAsync(created.SessionId, created.AccessToken, request, CancellationToken.None);
            await Assert.That((await manager.ReadArtifactForPreviewAsync(created.SessionId, ConfigurationImportTarget.ForInstance(),
                created.AccessToken, Now.UtcDateTime, CancellationToken.None)).ModerationReservation!.Digest)
                .IsEqualTo(reservation.Digest);
        }
        await using var applyContext = database.Context();
        var (_, _, apply) = FanoutSessionServices(database, applyContext, protection, privacy);
        var receipt = await apply.ApplyInstanceAsync(created.SessionId, created.AccessToken, request, null, null, CancellationToken.None);
        await Assert.That(receipt.FidelityVerified).IsTrue();
        await Assert.That(receipt.EffectStatus).IsEqualTo(ConfigurationImportEffectStatus.Completed);
        await Assert.That(receipt.SnapshotAvailable).IsTrue();
        var actual = new ConfigurationImportArtifactParser().Parse((await CurrentExportReader(database, applyContext)
            .ReadAsync(ConfigurationManifestExportView.Overrides, CancellationToken.None)).Utf8Json).Manifest;
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
        await Assert.That(async () => await apply.ApplyInstanceAsync(created.SessionId, created.AccessToken,
            request, null, null, CancellationToken.None)).Throws<ConfigurationImportSessionException>();
    }

    private static (ConfigurationImportSessionManager Manager, ConfigurationImportSessionApplicationService Previews,
        ConfigurationImportApplyService Apply) FanoutSessionServices(
        Database database, ExploreDbContext context, IDataProtectionProvider protection,
        ModerationNoticePrivacyFixture privacy)
    {
        var unit = new EfCoreUnitOfWork(context);
        var sessions = new ConfigurationImportSessionRepository(context);
        var artifacts = new ConfigurationImportArtifactRepository(context, protection);
        var operations = new ConfigurationImportOperationRepository(context);
        var outbox = new OutboxRepository(context);
        var tenants = new TenantRepository(context);
        var legal = new LegalDocumentRepository(context);
        var manager = new ConfigurationImportSessionManager(sessions, artifacts, unit, new());
        var moderation = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        var clock = new ImportExportOwnerClock();
        var previews = new ConfigurationImportSessionApplicationService(manager, new(), CurrentExportReader(database, context),
            tenants, clock, moderation, privacy.Authority(context, clock), unit);
        // This witness selects native moderation and legal only: no setting/document mutation,
        // managed scheduling or cache invalidation service may participate.
        var sections = new ConfigurationImportSectionApplier(null!, null!, null!, null!, null!, null!, null!,
            tenants, new TenantSettingsDocumentRepository(context), legal);
        var currentUser = new CurrentUserService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, database.AdminId.ToString())], "fanout-session"))
            }
        });
        var delivery = new ConfigurationImportEffectDelivery(outbox, operations, null!, null!, tenants, []);
        return (manager, previews, new ConfigurationImportApplyService(manager, sessions, artifacts, operations,
            previews, new(), new(), sections, new RelationalSettingMutationLock(context, unit), unit,
            outbox, delivery, null!, currentUser, new ImportExportOwnerClock(),
            NullLogger<ConfigurationImportApplyService>.Instance, moderation, legal));
    }
}
