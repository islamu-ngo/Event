using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Explore.Domain;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Refreshed_reservation_retains_adopted_baseline_and_local_only_profile_across_requests()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var first = database.Request();
        var initial = await database.PublishAsync(first);
        var incoming = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(initial.Publication!)!);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        ModerationConfigurationImportReservation reservation;
        await using (var preview = database.Context())
            reservation = (await new ModerationConfigurationImportAdapter(database.Writer(preview),
                new EfCoreUnitOfWork(preview), database.Inventory)
                .PreviewAsync(target, incoming, null, CancellationToken.None)).Reserve();
        var next = database.Next(first);
        var priorProfile = next.Proposed.Profiles[0];
        var added = priorProfile with
        {
            Id = Guid.CreateVersion7(),
            Rule = ISLAMU.Moderation.Domain.ModerationRuleReference.Create(
                database.Scope.Id, priorProfile.Rule.Code, 2)
        };
        next = next with
        {
            Proposed = next.Proposed with
            { Profiles = [priorProfile, added], SupportedProfiles = [added.Rule] }
        };
        await database.PublishAsync(next);
        await using var apply = database.Context();
        var unit = new EfCoreUnitOfWork(apply);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(apply), unit, database.Inventory);
        var refreshed = await adapter.PreviewReservedAsync(target, incoming,
            ModerationConfigurationImportReservation.Parse(reservation.Serialize()), CancellationToken.None);
        await Assert.That(refreshed.BaselinePublicationId).IsEqualTo(first.Proposed.PublicationId);
        await Assert.That(refreshed.CanApply).IsTrue();
        await Assert.That(refreshed.Mapping.ProfileIds[new(added.Rule.Code, 2)]).IsEqualTo(added.Id);
        var publication = await unit.ExecuteSerializableAsync(token =>
            adapter.ApplyReservedInCurrentTransactionAsync(Guid.CreateVersion7(), target, incoming,
                refreshed.Reserve(), token));
        await Assert.That(publication.PreviousPublicationId).IsEqualTo(next.Proposed.PublicationId);
        await Assert.That(publication.Profiles.Select(profile => profile.ProfileId))
            .IsEquivalentTo(new[] { priorProfile.Id, added.Id });
        await database.AssertCountsAsync(3, 1, 4);
    }

    [Test]
    public async Task Preview_only_reads_stored_typed_artifact_and_persists_private_reservation_without_publication()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var incoming = ImportDocument(database);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        var package = new TenantConfigurationPackageV1Alpha2
        {
            Schema = TenantConfigurationPackageContractMetadata.SchemaId,
            ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
            Kind = TenantConfigurationPackageContractMetadata.Kind,
            Metadata = new() { Name = "import-policy", Source = new() { TenantName = "source-tenant" } },
            Spec = new()
            {
                DisplayName = "Imported policy",
                Settings = new Dictionary<string, System.Text.Json.JsonElement>(),
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(),
                LegalDocuments = new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(),
                Moderation = ModerationConfigurationImportSections.ToWire(incoming)
            }
        };
        var bytes = TenantConfigurationPackageSerializer.Serialize(package);
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var protection = new EphemeralDataProtectionProvider();
        Guid sessionId;
        string accessToken;
        await using (var context = database.Context())
        {
            var unit = new EfCoreUnitOfWork(context);
            var manager = new ConfigurationImportSessionManager(new ConfigurationImportSessionRepository(context),
                new ConfigurationImportArtifactRepository(context, protection), unit, new());
            var created = await manager.CreateAsync(target, bytes, now, TimeSpan.FromMinutes(30), CancellationToken.None);
            sessionId = created.Session.SessionId;
            accessToken = created.AccessToken;
            var stored = await manager.ReadArtifactForPreviewAsync(sessionId, target, accessToken, now, CancellationToken.None);
            var request = new ConfigurationImportPreviewRequest
            {
                SelectedSectionKeys = [ModerationConfigurationImportSections.Tenant],
                Mappings = new Dictionary<string, string>(),
                ApplyMode = ConfigurationImportApplyMode.PreviewOnly,
                GrantedApprovalCodes = []
            };
            var preview = await new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory)
                .PreviewSetAsync(target, stored.Bytes, request, stored.ModerationReservation, CancellationToken.None);
            var source = ConfigurationImportArtifactSnapshotFactory.FromTenantPackage(new ConfigurationImportArtifactParser()
                .ParseTenantPackage(stored.Bytes).Package);
            var input = new ConfigurationImportPreviewInput(target, stored.Digest,
                ConfigurationImportDigest.Compute([]), source, [], [ModerationConfigurationImportSections.Tenant],
                [], ConfigurationImportApplyMode.PreviewOnly, [], [], stored.ExpiresAt, preview);
            var result = await manager.PreparePreviewAsync(sessionId, target, accessToken, input, now, CancellationToken.None);
            await Assert.That(result.IsApplyReady).IsTrue();
            await Assert.That(result.Binding.ModerationReservationDigest).IsEqualTo(preview.Reservation.Digest);
        }
        await using var reader = database.Context();
        var restored = await new EfCoreUnitOfWork(reader).ExecuteSerializableAsync(token =>
            new ConfigurationImportSessionRepository(reader).GetForUpdateAsync(sessionId, target, token));
        await Assert.That(restored!.ModerationReservation).IsNotNull();
        await Assert.That(restored.PreviewApplyMode).IsEqualTo(ConfigurationImportApplyMode.PreviewOnly);
        var readManager = new ConfigurationImportSessionManager(new ConfigurationImportSessionRepository(reader),
            new ConfigurationImportArtifactRepository(reader, protection), new EfCoreUnitOfWork(reader), new());
        var retained = await readManager.ReadArtifactForPreviewAsync(sessionId, target, accessToken, now, CancellationToken.None);
        await Assert.That(retained.Bytes.ToArray()).IsEquivalentTo(bytes.ToArray());
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reservation_flushes_only_exact_owned_legal_graph_and_rolls_back_on_foreign_work(bool foreign)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var incoming = ImportDocument(database);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        var reservation = (await adapter.PreviewAsync(target, incoming, null, CancellationToken.None)).Reserve();
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var document = LegalDocument.CreateImportedDraft(LegalDocumentScope.Tenant, database.TenantId,
            LegalDocumentKind.TenantTerms, LegalDocumentAudience.Public,
            [LegalDocumentLocalizedSource.Create("en", "Terms", "Imported terms", "Imported terms.")],
            null, "sha256:" + new string('a', 64), false, now);
        async Task Apply()
        {
            await unit.ExecuteSerializableAsync(async token =>
            {
                await new LegalDocumentRepository(context).AddAsync(document, token);
                if (foreign)
                    (await context.Tenants.SingleAsync(row => row.Id == database.TenantId, token)).FullName = "Foreign pending";
                return await adapter.ApplyReservedInCurrentTransactionAsync(
                    Guid.CreateVersion7(), target, incoming, reservation, token, [document]);
            });
        }
        if (foreign)
            await Assert.That(Apply).Throws<ModerationConfigurationImportException>();
        else
            await Apply();
        await using var reader = database.Context();
        var retained = await new LegalDocumentRepository(reader).GetByIdForUpdateAsync(
            document.Id, LegalDocumentScope.Tenant, database.TenantId, CancellationToken.None);
        await Assert.That(retained is not null).IsEqualTo(!foreign);
        await database.AssertCountsAsync(foreign ? 0 : 1, foreign ? 0 : 1, foreign ? 1 : 2);
    }

    [Test]
    public async Task Session_reservation_survives_fresh_context_and_preserves_exact_native_identities()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        var incoming = ImportDocument(database);
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        ConfigurationImportSession session;
        ModerationConfigurationImportReservation reservation;
        ModerationConfigurationImportReservationSet reservationSet;
        await using (var context = database.Context())
        {
            var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
                new EfCoreUnitOfWork(context), database.Inventory);
            var preview = await adapter.PreviewAsync(target, incoming, null, CancellationToken.None);
            reservation = preview.Reserve();
            var digest = ConfigurationImportDigest.ComputeBytes(
                ModerationConfigurationDocumentProjection.Serialize(incoming));
            session = ConfigurationImportSession.Create(Guid.CreateVersion7(), target,
                new(new(Guid.CreateVersion7()), digest, 1, now.AddHours(1)),
                ConfigurationImportDigest.ComputeBytes(Guid.NewGuid().ToByteArray()), now, TimeSpan.FromMinutes(30));
            reservationSet = new(target.AuthorityKey, digest, digest, digest,
                [new(ModerationConfigurationImportSections.Tenant, "source-tenant",
                    database.TenantId.ToString("N"), database.TenantId, Guid.CreateVersion7(), reservation)]);
            var binding = new ConfigurationImportPreviewBinding(target, digest, digest, digest, digest,
                ConfigurationImportApplyMode.ApplySelected, digest, now.AddMinutes(30), reservationSet.Digest);
            session.MarkPreviewReady(binding, now, reservationSet);
            await new ConfigurationImportSessionRepository(context).AddAsync(session, CancellationToken.None);
        }

        await using var apply = database.Context();
        var restored = await new EfCoreUnitOfWork(apply).ExecuteSerializableAsync(token =>
            new ConfigurationImportSessionRepository(apply).GetForUpdateAsync(session.SessionId, target, token));
        await Assert.That(restored!.ModerationReservation!.Digest).IsEqualTo(reservationSet.Digest);
        var unit = new EfCoreUnitOfWork(apply);
        var native = new ModerationConfigurationImportAdapter(database.Writer(apply), unit, database.Inventory);
        var publication = await unit.ExecuteSerializableAsync(token =>
            native.ApplyReservedInCurrentTransactionAsync(Guid.CreateVersion7(), target, incoming,
                restored.ModerationReservation!.Entries.Single().Definition, token));
        await Assert.That(publication.Id).IsEqualTo(reservation.PublicationId);
        await Assert.That(publication.PolicyId).IsEqualTo(reservation.PolicyId);
        await Assert.That(publication.Profiles.Single().ProfileId)
            .IsEqualTo(reservation.Profiles.Single().ProfileId);
        await Assert.That(publication.PublishedActive).IsFalse();
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task Reserved_apply_rejects_changed_incoming_and_rolls_back_earlier_import_work()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var incoming = ImportDocument(database);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        ModerationConfigurationImportReservation reservation;
        await using (var preview = database.Context())
            reservation = (await new ModerationConfigurationImportAdapter(database.Writer(preview),
                new EfCoreUnitOfWork(preview), database.Inventory).PreviewAsync(
                    target, incoming, null, CancellationToken.None)).Reserve();
        var changed = new ModerationConfigurationDocument(incoming.SchemaVersion, incoming.CatalogueRevision,
            incoming.Modes, incoming.Classes.Select(item => item with { Meaning = "Changed incoming meaning." }).ToArray(),
            incoming.Reasons, incoming.Profiles, incoming.SupportedProfiles);
        await using var context = database.Context();
        var original = (await context.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName;
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        await Assert.That(async () => await unit.ExecuteSerializableAsync(async token =>
        {
            (await context.Tenants.SingleAsync(row => row.Id == database.TenantId, token)).FullName = "Earlier section";
            await context.SaveChangesAsync(token);
            return await adapter.ApplyReservedInCurrentTransactionAsync(
                Guid.CreateVersion7(), target, changed, reservation, token);
        })).Throws<ModerationConfigurationImportException>();
        await using var reader = database.Context();
        await Assert.That((await reader.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName)
            .IsEqualTo(original);
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reserved_apply_requalifies_current_native_authority_after_new_request(bool changeHead)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var incoming = ImportDocument(database);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        ModerationConfigurationImportReservation reservation;
        await using (var preview = database.Context())
            reservation = (await new ModerationConfigurationImportAdapter(database.Writer(preview),
                new EfCoreUnitOfWork(preview), database.Inventory).PreviewAsync(
                    target, incoming, null, CancellationToken.None)).Reserve();
        if (changeHead)
            await database.PublishAsync(database.Request());
        else
        {
            await using var authority = database.Context();
            authority.PlatformUserRoles.Remove(
                await authority.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdminId));
            await authority.SaveChangesAsync();
        }
        await using var apply = database.Context();
        var unit = new EfCoreUnitOfWork(apply);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(apply), unit, database.Inventory);
        await Assert.That(async () => await unit.ExecuteSerializableAsync(token =>
            adapter.ApplyReservedInCurrentTransactionAsync(Guid.CreateVersion7(), target, incoming,
                reservation, token))).Throws<ModerationConfigurationImportException>();
        await database.AssertCountsAsync(changeHead ? 1 : 0, changeHead ? 1 : 0, changeHead ? 2 : 1);
    }
}
