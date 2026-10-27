using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Whole_instance_reservation_applies_instance_request_and_foreign_drafts_with_shared_epoch()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var artifact = await FanoutArtifactAsync(database, context);
        var adapter = new ModerationConfigurationImportAdapter(
            database.Writer(context), new EfCoreUnitOfWork(context), database.Inventory);
        var preview = await adapter.PreviewSetAsync(ConfigurationImportTarget.ForInstance(),
            artifact, FanoutRequest(), null, CancellationToken.None);
        var reservation = ModerationConfigurationImportReservationSet.Parse(preview.Reservation.Serialize());
        var repeated = await adapter.PreviewSetAsync(ConfigurationImportTarget.ForInstance(),
            artifact, FanoutRequest(), reservation, CancellationToken.None);
        await Assert.That(repeated.Reservation.Digest).IsEqualTo(reservation.Digest);
        await Assert.That(reservation.Entries.Count).IsEqualTo(3);
        await Assert.That(reservation.Entries.Select(entry => entry.OperationId).Distinct().Count()).IsEqualTo(3);
        var originalContext = context.TenantContext;
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            var transaction = context.Database.CurrentTransaction;
            var completion = await adapter.ApplySetInCurrentTransactionAsync(
                ConfigurationImportTarget.ForInstance(), artifact, FanoutRequest(), reservation, token);
            await completion.VerifyBeforeCommitAsync(token);
            await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(ReferenceEquals(context.TenantContext, originalContext)).IsTrue();
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
            await Assert.That(context.ModerationTrustDomainId).IsNull();
            await Assert.That(context.IsTenantFilterBypassed).IsFalse();
            return true;
        });
        await using var actual = database.Context();
        var exported = await CurrentExportReader(database, actual).ReadAsync(
            ConfigurationManifestExportView.Overrides, CancellationToken.None);
        var manifest = new ConfigurationImportArtifactParser().Parse(exported.Utf8Json).Manifest;
        await Assert.That(manifest.Spec.Instance.Moderation).IsNotNull();
        await Assert.That(manifest.Spec.Tenants.Count(tenant => tenant.Spec.Moderation is not null)).IsEqualTo(2);
        var destinations = await new EfCoreUnitOfWork(actual).ExecuteSerializableAsync(async token => new[]
        {
            await database.Writer(actual).AcquireConfigurationImportDestinationAsync(
                ConfigurationImportTarget.ForInstance(), null, token),
            await database.Writer(actual).AcquireInstanceConfigurationImportDestinationAsync(database.TenantId, null, token),
            await database.Writer(actual).AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, null, token)
        });
        foreach (var destination in destinations)
        {
            var reserved = reservation.Entries.Single(entry =>
                entry.TargetTenantId == (destination.Jurisdiction.Kind == ISLAMU.Moderation.Domain.ModerationScopeKind.Tenant
                    ? destination.Jurisdiction.Id : (Guid?)null));
            await Assert.That(destination.Current!.Id).IsEqualTo(reserved.Definition.PublicationId);
            await Assert.That(destination.Current.OperationId).IsEqualTo(reserved.OperationId);
            await Assert.That(destination.Current.PublishedActive).IsFalse();
            await Assert.That(destination.Selection!.ActivePublicationId).IsNull();
        }
        await Assert.That(destinations[0].Epoch.Revision).IsEqualTo(3);
        await Assert.That(destinations[1].Epoch.Revision).IsEqualTo(3);
        await Assert.That(destinations[2].Epoch.Revision).IsEqualTo(3);
    }

    [Test]
    [Arguments("mapping")]
    [Arguments("selection")]
    [Arguments("epoch")]
    [Arguments("root")]
    [Arguments("role-replacement")]
    [Arguments("inactive")]
    [Arguments("slug")]
    [Arguments("missing")]
    public async Task Whole_instance_repreview_rejects_external_reservation_drift(string drift)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var artifact = await FanoutArtifactAsync(database, context);
        var adapter = new ModerationConfigurationImportAdapter(
            database.Writer(context), new EfCoreUnitOfWork(context), database.Inventory);
        var preview = await adapter.PreviewSetAsync(ConfigurationImportTarget.ForInstance(),
            artifact, FanoutRequest(), null, CancellationToken.None);
        var request = FanoutRequest();
        if (drift == "mapping")
            request = FanoutRequest(new Dictionary<string, string>
            { [$"export-{foreign.TenantId:N}"] = $"publication-{database.TenantId:N}" });
        if (drift == "missing")
            request = FanoutRequest(new Dictionary<string, string>
            { [$"export-{foreign.TenantId:N}"] = "missing-native-tenant" });
        if (drift == "selection")
            request = new()
            {
                SelectedSectionKeys = [ModerationConfigurationImportSections.Tenant],
                Mappings = new Dictionary<string, string>(),
                ApplyMode = ConfigurationImportApplyMode.ApplySelected,
                GrantedApprovalCodes = []
            };
        if (drift == "root") await DemoteExportAdministratorAsync(database);
        if (drift == "role-replacement")
        {
            await using var changed = database.Context();
            var prior = await changed.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdminId);
            changed.PlatformUserRoles.Remove(prior);
            await changed.SaveChangesAsync();
            changed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = database.AdminId,
                RoleId = prior.RoleId,
                User = await changed.Users.SingleAsync(row => row.Id == database.AdminId),
                Role = await changed.Roles.SingleAsync(row => row.Id == prior.RoleId)
            });
            await changed.SaveChangesAsync();
        }
        if (drift is "epoch" or "inactive" or "slug")
        {
            await using var changed = database.Context();
            if (drift == "epoch")
            {
                var epoch = await changed.ModerationQualificationEpochs.SingleAsync();
                epoch.Advance(epoch.Revision);
            }
            else
            {
                var tenant = await changed.Tenants.SingleAsync(row => row.Id == foreign.TenantId);
                if (drift == "slug") tenant.Slug = $"renamed-{foreign.TenantId:N}";
                else tenant.TenantStatusId = await changed.TenantStatuses.Where(row => !row.IsActiveState)
                    .Select(row => row.Id).FirstAsync();
            }
            await changed.SaveChangesAsync();
        }
        await Assert.That(async () => await adapter.PreviewSetAsync(ConfigurationImportTarget.ForInstance(),
            artifact, request, preview.Reservation, CancellationToken.None)).Throws<ModerationConfigurationImportException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Fanout_later_failure_or_terminal_authority_loss_rolls_back_earlier_legal_and_drafts(bool terminal)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        await PublishExportTenantAsync(database);
        ModerationConfigurationImportReservationSet reservation;
        ReadOnlyMemory<byte> artifact;
        await using (var previewContext = database.Context())
        {
            artifact = await FanoutArtifactAsync(database, previewContext);
            reservation = (await new ModerationConfigurationImportAdapter(database.Writer(previewContext),
                new EfCoreUnitOfWork(previewContext), database.Inventory).PreviewSetAsync(
                ConfigurationImportTarget.ForInstance(), artifact, FanoutRequest(), null, CancellationToken.None)).Reservation;
        }
        await using var context = terminal ? database.Context() : database.Context(new RejectSecondFanoutWrite());
        var originalName = (await context.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName;
        var legal = LegalImportDraft(database.TenantId);
        async Task Apply() => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            (await context.Tenants.SingleAsync(row => row.Id == database.TenantId, token)).FullName = "Earlier settings";
            await context.SaveChangesAsync(token);
            var repository = new LegalDocumentRepository(context);
            var transaction = repository.CaptureConfigurationImportTransaction();
            await repository.AddAsync(legal, token);
            await repository.FlushConfigurationImportInCurrentTransactionAsync(transaction,
                ConfigurationImportTarget.ForInstance(), [database.TenantId], [legal], token);
            var completion = await new ModerationConfigurationImportAdapter(database.Writer(context),
                new EfCoreUnitOfWork(context), database.Inventory).ApplySetInCurrentTransactionAsync(
                ConfigurationImportTarget.ForInstance(), artifact, FanoutRequest(), reservation, token);
            if (terminal)
            {
                context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(
                    row => row.UserId == database.AdminId, token));
                await context.SaveChangesAsync(token);
            }
            await completion.VerifyBeforeCommitAsync(token);
            return true;
        });
        if (terminal)
            await Assert.That(Apply).Throws<ModerationConfigurationImportException>();
        else
            await Assert.That(Apply).Throws<PublicationWriteInterrupted>();
        await using var actual = database.Context();
        await Assert.That((await actual.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName).IsEqualTo(originalName);
        await Assert.That(await new LegalDocumentRepository(actual).GetByIdForUpdateAsync(
            legal.Id, LegalDocumentScope.Tenant, database.TenantId, CancellationToken.None)).IsNull();
        var exported = await Exporter(database, actual).ReadAsync(ConfigurationImportTarget.ForInstance(), CancellationToken.None);
        await Assert.That(exported.Instance).IsNull();
        await Assert.That(exported.Tenants.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Unreserved_native_epoch_advance_in_same_transaction_is_not_owned_fanout_progress()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var artifact = await FanoutArtifactAsync(database, context);
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        var preview = await adapter.PreviewSetAsync(ConfigurationImportTarget.ForInstance(),
            artifact, FanoutRequest(), null, CancellationToken.None);
        await Assert.That(async () => await unit.ExecuteSerializableAsync(async token =>
        {
            var completion = await adapter.ApplySetInCurrentTransactionAsync(
                ConfigurationImportTarget.ForInstance(), artifact, FanoutRequest(), preview.Reservation, token);
            var extraTarget = ConfigurationImportTarget.ForTenant(database.TenantId);
            var document = ImportDocument(database);
            var extraPreview = await adapter.PreviewInCurrentTransactionAsync(extraTarget, document, null, token);
            await adapter.ApplyInCurrentTransactionAsync(Guid.CreateVersion7(), extraTarget, document, extraPreview, token);
            await completion.VerifyBeforeCommitAsync(token);
            return true;
        })).Throws<ModerationConfigurationImportException>();
        await using var reader = database.Context();
        var actual = await Exporter(database, reader).ReadAsync(ConfigurationImportTarget.ForInstance(), CancellationToken.None);
        await Assert.That(actual.Instance).IsNull();
        await Assert.That(actual.Tenants.Count).IsEqualTo(1);
    }

    private static ConfigurationImportPreviewRequest FanoutRequest(IReadOnlyDictionary<string, string>? mappings = null) =>
        new()
        {
            SelectedSectionKeys = [ModerationConfigurationImportSections.Instance, ModerationConfigurationImportSections.Tenant],
            Mappings = mappings ?? new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };

    private static async Task<ReadOnlyMemory<byte>> FanoutArtifactAsync(Database database, ExploreDbContext context)
    {
        await SeedExportPaidPolicyAsync(context);
        var exported = await CurrentExportReader(database, context).ReadAsync(
            ConfigurationManifestExportView.Overrides, CancellationToken.None);
        var manifest = new ConfigurationImportArtifactParser().Parse(exported.Utf8Json).Manifest;
        var document = ModerationConfigurationImportSections.ToWire(ImportDocument(database));
        manifest = manifest with
        {
            Spec = manifest.Spec with
            {
                Instance = manifest.Spec.Instance with { Moderation = document },
                Tenants = manifest.Spec.Tenants.Select(tenant => tenant with
                { Spec = tenant.Spec with { Moderation = document } }).ToArray()
            }
        };
        return ConfigurationPortabilityJsonCodec.SerializeConfigurationManifest(manifest);
    }

    private sealed class RejectSecondFanoutWrite : SaveChangesInterceptor
    {
        private int writes;
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationPolicyPublication>().Count() > writes
                && ++writes == 2)
                throw new PublicationWriteInterrupted();
            return ValueTask.FromResult(result);
        }
    }
}
