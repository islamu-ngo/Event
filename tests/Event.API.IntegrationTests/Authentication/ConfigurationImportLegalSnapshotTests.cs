using System.Text.Json;
using EventVisitorCapabilitySqliteFixture = Event.Api.IntegrationTests.Authentication.ResourceVisitorAuthenticatedFixture;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class ConfigurationImportLegalSnapshotTests
{
    [Test]
    public async Task AppliedLegalDraftIsVisibleToFidelityBeforeCommitAndRollsBackTogether()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var document = LegalDocument.CreateImportedDraft(
            LegalDocumentScope.Instance, null, LegalDocumentKind.TermsOfService,
            LegalDocumentAudience.Public,
            [LegalDocumentLocalizedSource.Create("en", "Imported terms", "Portable summary", "Native legal source")],
            null, $"sha256:{new string('a', 64)}", true, DateTime.UtcNow);
        fixture.Context.LegalDocuments.Add(document);
        fixture.Context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync(
                policy => policy.TenantId == null && policy.IsActive))
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await fixture.Context.SaveChangesAsync();
        var source = await fixture.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides, default);
        fixture.Context.ChangeTracker.Clear();
        await fixture.Context.LegalDocumentLocalizedSources
            .Where(row => row.LegalDocumentVersionId == document.Versions[0].Id).ExecuteDeleteAsync();
        await fixture.Context.LegalDocumentVersions
            .Where(row => row.LegalDocumentId == document.Id).ExecuteDeleteAsync();
        await fixture.Context.LegalDocuments.Where(row => row.Id == document.Id).ExecuteDeleteAsync();
        var parser = fixture.Services.GetRequiredService<ConfigurationImportArtifactParser>();
        string digest = parser.Parse(source.Utf8Json).Sha256Digest;
        var selection = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.legal_documents"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = ["legal-review"]
        };

        var preparation = fixture.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await preparation.PrepareAsync(CancellationToken.None);
        await using var owner = fixture.CreateScope();
        var context = await fixture.Services.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        context.BindConfigurationInstanceImportOwner();
        var lease = owner.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.Bind(context, null);
        lease.BindHuman(preparation.Subject!, HumanAdministratorCommitPurpose.ConfigurationImport, null, fixture.Lifetime);
        await using (var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            await Assert.That(await owner.ServiceProvider.GetRequiredService<IInstanceAdministratorCommitAuthority>()
                .IsInstanceAdministratorForCommitAsync(fixture.UserId, CancellationToken.None)).IsTrue();
            await owner.ServiceProvider.GetRequiredService<ConfigurationImportSectionApplier>().ApplyAsync(
                ConfigurationImportTarget.ForInstance(), source.Utf8Json, selection,
                preparation.Subject!, fixture.UserId, DateTime.UtcNow, digest, parser, default);
            var actual = await owner.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>()
                .ReadImportSnapshotAsync(ConfigurationManifestExportView.Overrides, null, default);
            using JsonDocument artifact = JsonDocument.Parse(actual.Utf8Json);
            JsonElement legal = artifact.RootElement.GetProperty("spec").GetProperty("instance")
                .GetProperty("legalDocuments").GetProperty(nameof(LegalDocumentKind.TermsOfService));
            await Assert.That(legal.GetProperty("localizations").EnumerateArray().Single()
                .GetProperty("markdown").GetString()).IsEqualTo("Native legal source");
            await using var observerScope = fixture.CreateScope();
            var observer = observerScope.ServiceProvider.GetRequiredService<Explore.Persistence.ExploreDbContext>();
            await Assert.That(await observer.LegalDocuments.AnyAsync(
                row => row.Kind == LegalDocumentKind.TermsOfService)).IsFalse();
            await transaction.RollbackAsync();
        }
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await fixture.Context.LegalDocuments.AnyAsync(
            row => row.Kind == LegalDocumentKind.TermsOfService)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ImportSnapshotIncludesCurrentUnpublishedLegalSource(bool tenantScoped)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        LegalDocumentKind kind = tenantScoped
            ? LegalDocumentKind.TenantTerms : LegalDocumentKind.TermsOfService;
        var document = LegalDocument.CreateImportedDraft(
            tenantScoped ? LegalDocumentScope.Tenant : LegalDocumentScope.Instance,
            tenantScoped ? fixture.TenantId : null, kind, LegalDocumentAudience.Public,
            [LegalDocumentLocalizedSource.Create("en", "Imported terms", "Portable summary", "Native legal source")],
            null, $"sha256:{new string('a', 64)}", true, DateTime.UtcNow);
        fixture.Context.LegalDocuments.Add(document);
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync(
                policy => policy.TenantId == null && policy.IsActive))
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var snapshot = await fixture.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides, default);

        using JsonDocument artifact = JsonDocument.Parse(snapshot.Utf8Json);
        JsonElement spec = artifact.RootElement.GetProperty("spec");
        JsonElement owner = tenantScoped
            ? spec.GetProperty("tenants").EnumerateArray().Single().GetProperty("spec")
            : spec.GetProperty("instance");
        string code = kind.ToString();
        JsonElement legal = owner.GetProperty("legalDocuments").GetProperty(code);
        await Assert.That(legal.GetProperty("audience").GetString()).IsEqualTo("Public");
        await Assert.That(legal.GetProperty("requiresFreshAcceptance").GetBoolean()).IsTrue();
        JsonElement source = legal.GetProperty("localizations").EnumerateArray().Single();
        await Assert.That(source.GetProperty("languageTag").GetString()).IsEqualTo("en");
        await Assert.That(source.GetProperty("title").GetString()).IsEqualTo("Imported terms");
        await Assert.That(source.GetProperty("summary").GetString()).IsEqualTo("Portable summary");
        await Assert.That(source.GetProperty("markdown").GetString()).IsEqualTo("Native legal source");
        var persisted = await fixture.Context.LegalDocuments.AsNoTracking()
            .Include(row => row.Publications).SingleAsync(row => row.Id == document.Id);
        await Assert.That(persisted.State).IsEqualTo(LegalDocumentLifecycleState.ReviewRequired);
        await Assert.That(persisted.Publications).IsEmpty();
    }
}
