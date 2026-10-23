using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Actual_instance_artifact_exports_instance_and_each_authorized_tenant_legal_content_without_impersonation()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var instance = LegalDocument.CreateImportedDraft(LegalDocumentScope.Instance, null,
            LegalDocumentKind.TermsOfService, LegalDocumentAudience.Public,
            [LegalDocumentLocalizedSource.Create("en", "Instance terms", "Instance meaning", "Instance content.")],
            null, "private-instance-origin", false, Now.UtcDateTime);
        context.LegalDocuments.AddRange(instance, LegalImportDraft(database.TenantId), LegalImportDraft(foreign.TenantId));
        await context.SaveChangesAsync();
        await SeedExportPaidPolicyAsync(context);
        var originalTrustDomain = context.ModerationTrustDomainId;
        var artifact = await CurrentExportReader(database, context).ReadAsync(
            ConfigurationManifestExportView.Overrides, CancellationToken.None);
        var manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(artifact.Utf8Json);
        await Assert.That(manifest.Spec.Instance.LegalDocuments["TermsOfService"].Localizations.Single().Markdown)
            .IsEqualTo("Instance content.");
        foreach (var slug in new[] { $"publication-{database.TenantId:N}", $"export-{foreign.TenantId:N}" })
        {
            var tenant = manifest.Spec.Tenants.Single(row => row.Metadata.Name == slug);
            await Assert.That(tenant.Spec.LegalDocuments.Keys.Single()).IsEqualTo("TenantTerms");
            await Assert.That(tenant.Spec.LegalDocuments["TenantTerms"].Localizations.Single().Markdown)
                .IsEqualTo("Imported terms.");
            await Assert.That(tenant.Spec.LegalDocuments["TenantTerms"].AccountableIdentityReference).IsNull();
        }
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.ModerationTrustDomainId).IsEqualTo(originalTrustDomain);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    [Arguments("jurisdiction")]
    [Arguments("summary")]
    public async Task Unstored_legal_metadata_rejects_the_import_and_rolls_back_earlier_sections(string field)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        context.LegalDocuments.Add(LegalImportDraft(database.TenantId));
        await context.SaveChangesAsync();
        await SeedExportPaidPolicyAsync(context);
        var artifact = await CurrentExportReader(database, context).ReadTenantAsync(database.TenantId,
            ConfigurationManifestExportView.Overrides, CancellationToken.None);
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(artifact.Utf8Json);
        var terms = package.Spec.LegalDocuments["TenantTerms"] with
        {
            JurisdictionAssumptions = field == "jurisdiction" ? ["Unsupported jurisdiction"] : [],
            ChangeSummary = field == "summary" ? "Unsupported revision summary" : null
        };
        package = package with
        {
            Spec = package.Spec with
            {
                LegalDocuments = new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>
                {
                    ["TenantTerms"] = terms
                }
            }
        };
        var bytes = ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package);
        var preview = ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(package)
            .Single(section => section.SectionKey == "tenant.legal_documents");
        await Assert.That(preview.BlockingReasonCode)
            .IsEqualTo("configuration_import_legal_metadata_not_supported");
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(),
            new ImportExportOwnerClock());
        await human.InitializeAsync();
        await human.PrepareAsync();
        var applier = human.Services.GetRequiredService<ConfigurationImportSectionApplier>();
        var subject = human.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject
            ?? throw new InvalidOperationException("Legal import requires the authenticated original human.");
        var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
        var originalName = tenant.FullName;
        async Task Apply() => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            tenant.FullName = "Earlier imported section";
            await context.SaveChangesAsync(token);
            return await applier.ApplyAsync(ConfigurationImportTarget.ForTenant(database.TenantId), bytes,
                new ConfigurationImportPreviewRequest
                {
                    SelectedSectionKeys = ["tenant.legal_documents"],
                    Mappings = new Dictionary<string, string>(),
                    ApplyMode = ConfigurationImportApplyMode.ApplySelected,
                    GrantedApprovalCodes = []
                }, subject, database.AdminId, Now.UtcDateTime, ConfigurationImportDigest.ComputeBytes(bytes), new(), token);
        });
        await Assert.That(Apply).Throws<ConfigurationImportSessionException>();
        await using var actual = database.Context();
        await Assert.That((await actual.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName)
            .IsEqualTo(originalName);
        var document = await new LegalDocumentRepository(actual).GetForUpdateAsync(
            LegalDocumentScope.Tenant, database.TenantId, LegalDocumentKind.TenantTerms, CancellationToken.None);
        await Assert.That(document!.CurrentVersion).IsEqualTo(1);
    }

    [Test]
    public async Task Valid_enum_key_legal_section_applies_and_flushes_under_the_captured_original_transaction()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var reader = CurrentExportReader(database, context);
        var current = await reader.ReadTenantAsync(database.TenantId,
            ConfigurationManifestExportView.Overrides, CancellationToken.None);
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(current.Utf8Json);
        package = package with
        {
            Spec = package.Spec with
            {
                LegalDocuments = new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>
                {
                    ["TenantTerms"] = new()
                    {
                        Kind = "TenantTerms",
                        Audience = "Public",
                        LifecycleIntent = "ReviewRequired",
                        AccountableIdentityReference = "Source author",
                        ProposedEffectiveAt = Now.UtcDateTime,
                        JurisdictionAssumptions = [],
                        Localizations = [new()
                        {
                            LanguageTag = " FR ", Title = " Conditions ",
                            Summary = " Sens importe ", Markdown = "Contenu importe.\r\n"
                        }, new()
                        {
                            LanguageTag = " EN ", Title = " Terms ",
                            Summary = " Imported meaning ", Markdown = "Actual imported content.\r\n"
                        }]
                    }
                }
            }
        };
        var bytes = ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package);
        var repository = new LegalDocumentRepository(context);
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(),
            new ImportExportOwnerClock());
        await human.InitializeAsync();
        await human.PrepareAsync();
        var applier = human.Services.GetRequiredService<ConfigurationImportSectionApplier>();
        var subject = human.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject
            ?? throw new InvalidOperationException("Legal import requires the authenticated original human.");
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            var original = repository.CaptureConfigurationImportTransaction();
            await applier.ApplyAsync(ConfigurationImportTarget.ForTenant(database.TenantId), bytes,
                new ConfigurationImportPreviewRequest
                {
                    SelectedSectionKeys = ["tenant.legal_documents"],
                    Mappings = new Dictionary<string, string>(),
                    ApplyMode = ConfigurationImportApplyMode.ApplySelected,
                    GrantedApprovalCodes = []
                }, subject, database.AdminId, Now.UtcDateTime, ConfigurationImportDigest.ComputeBytes(bytes),
                new(), token);
            await Assert.That(repository.CaptureConfigurationImportTransaction()).IsEqualTo(original);
            var actual = await reader.ReadTenantInCurrentTransactionAsync(database.TenantId,
                ConfigurationManifestExportView.Overrides, token);
            var restored = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(actual.Utf8Json);
            var restoredTerms = restored.Spec.LegalDocuments["TenantTerms"];
            await Assert.That(restoredTerms.Localizations[0].Markdown)
                .IsEqualTo("Actual imported content.\n");
            await Assert.That(restoredTerms.Localizations[0].Title).IsEqualTo("Terms");
            await Assert.That(restoredTerms.Localizations[1].LanguageTag).IsEqualTo("fr");
            await Assert.That(restoredTerms.AccountableIdentityReference).IsNull();
            await Assert.That(restoredTerms.ProposedEffectiveAt).IsNull();
            var expectedSection = ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(package)
                .Single(section => section.SectionKey == "tenant.legal_documents");
            var actualSection = ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(restored)
                .Single(section => section.SectionKey == "tenant.legal_documents");
            await Assert.That(actualSection.NormalizedDigest).IsEqualTo(expectedSection.NormalizedDigest);
            var owned = await repository.GetForUpdateAsync(LegalDocumentScope.Tenant, database.TenantId,
                LegalDocumentKind.TenantTerms, token);
            await Assert.That(owned!.State).IsEqualTo(LegalDocumentLifecycleState.ReviewRequired);
            return actual.Utf8Json;
        });
    }

    [Test]
    public async Task Current_tenant_artifact_contains_unpublished_legal_meaning_without_borrowing_publication_authority()
    {
        await using var database = await Database.CreateAsync();
        var document = LegalImportDraft(database.TenantId);
        await using (var writer = database.Context())
        {
            await new LegalDocumentRepository(writer).AddAsync(document, CancellationToken.None);
            await writer.SaveChangesAsync();
        }
        await DemoteExportAdministratorAsync(database);
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var artifact = await CurrentExportReader(database, context).ReadTenantAsync(
            database.TenantId, ConfigurationManifestExportView.Overrides, CancellationToken.None);
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(artifact.Utf8Json);
        var terms = package.Spec.LegalDocuments["TenantTerms"];
        await Assert.That(terms.LifecycleIntent).IsEqualTo("Draft");
        await Assert.That(terms.Localizations.Single().Markdown).IsEqualTo("Imported terms.");
        await Assert.That(terms.AccountableIdentityReference).IsNull();
        await Assert.That(terms.ProposedEffectiveAt).IsNull();
        await Assert.That(document.State).IsEqualTo(LegalDocumentLifecycleState.ReviewRequired);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
    }
}
