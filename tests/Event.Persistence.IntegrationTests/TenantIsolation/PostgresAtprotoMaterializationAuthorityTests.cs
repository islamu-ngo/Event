using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresAtprotoMaterializationAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private const string ThumbnailCid = "bafkreibm6jg3ux5quca3po4nukm4m6xkfxzq4bgxjucfd4g6yuk3z7q7di";
    private const string ReplacementCid = "bafkreievoe7jzpor37fs2qeayjjx6qmnipfa3is7bv6wmmpu67exxco4i4";
    private const string ThumbnailChecksum = "2cf24dba5fb0a081b7bb8da299c67aea2df30e04d74d0451f0dec515bcfe1f1a";
    private const string ReplacementChecksum = "95713e9cbdd1dfcb2d4080c2537f418d43ca0da25f0d7d6631f4f7c97b89dc47";

    [Test]
    [Arguments(Scenario.Graph)]
    [Arguments(Scenario.Promotion)]
    [Arguments(Scenario.Replacement)]
    [Arguments(Scenario.ReferencedImage)]
    [Arguments(Scenario.Rollback)]
    public async Task SnapshotImportsPrivateGraphsWithoutGlobalPrivateTablePrivileges(Scenario scenario)
    {
        await fixture.ResetAsync();
        DateTime now = DateTime.UtcNow;
        Guid[] tenantIds = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        string did = "did:plc:" + Guid.CreateVersion7().ToString("N")[..24];
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.ExternalUnclassified,
            ActorType = null!,
            ExternalActorSubject = new ExternalActorSubject
            {
                Id = Guid.CreateVersion7(),
                FirstObservedAt = now,
                LastObservedAt = now,
                CreatedAt = now
            },
            Pii = new ActorPii { DisplayName = did },
            CreatedAt = now
        };
        actor.ExternalActorSubjectId = actor.ExternalActorSubject.Id;
        var identity = new AtprotoIdentity(AtprotoDid.Parse(did))
        {
            Id = Guid.CreateVersion7(),
            ActorId = actor.Id,
            Actor = actor,
            PdsHost = string.Empty,
            IsActive = false,
            LastResolvedAt = now,
            LastSeenAt = now,
            CreatedAt = now
        };
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = did,
            Collection = "community.lexicon.calendar.event",
            RecordKey = "3mprivate2222",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = 1,
            SourceCursor = 1,
            RecordJson = "{\"name\":\"Original\"}",
            RecordHash = new string('a', 64),
            IndexedAt = now,
            UpdatedAt = now
        };
        record.Uri = $"at://{did}/{record.Collection}/{record.RecordKey}";
        var binding = StorageProviderBinding.Local(Path.GetTempPath());
        var staged = new List<StagedStorageWrite>();
        string schema;
        string presentationTable;
        string actorPiiTable;
        string revisionTable;
        Dictionary<Guid, long> epochs;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Tenants.AddRange(tenantIds.Select(id => new Tenant
            {
                Id = id,
                Slug = "materialization-" + id.ToString("N"),
                FullName = "Materialization tenant",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            }));
            seed.AddRange(identity, record);
            if (scenario != Scenario.Graph)
            {
                seed.Add(binding);
                foreach (Guid tenantId in tenantIds)
                {
                    Guid operationId = Guid.CreateVersion7();
                    string key = "materialization/" + operationId.ToString("N");
                    var operation = StorageProducerOperation.Create(operationId, tenantId, binding, key, now);
                    operation.Settle(binding.Id, binding.Provider, key, null);
                    seed.Add(operation);
                    staged.Add(new(operationId, tenantId, binding.Id,
                        new FileStorageWriteResult(binding.Provider, key, 8, "image/png",
                            Sha256Checksum: ThumbnailChecksum)));
                    seed.StorageObjects.Add(new StorageObject
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = tenantId,
                        Tenant = null!,
                        ActorId = actor.Id,
                        Actor = actor,
                        FileTypeId = (int)FileTypeEnum.Image,
                        FileType = null!,
                        Provider = binding.Provider,
                        StorageProviderBindingId = binding.Id,
                        ObjectKey = "unrelated/" + Guid.CreateVersion7().ToString("N"),
                        FullName = "unrelated.png",
                        SafeDisplayName = "unrelated.png",
                        Extension = ".png",
                        ContentType = "image/png",
                        Size = 5,
                        Sha256Checksum = ThumbnailChecksum,
                        Purpose = StorageObjectPurposes.Attachment,
                        Visibility = StorageObjectVisibilities.AuthenticatedTenant,
                        LifecycleState = StorageObjectLifecycleStates.Active,
                        CreatedAt = now
                    });
                    seed.StorageUploadSessions.Add(new StorageUploadSession
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = tenantId,
                        Provider = binding.Provider,
                        StorageProviderBindingId = binding.Id,
                        PolicyMaxUploadBytes = 11,
                        ExpectedSizeBytes = 11,
                        ReservedBytes = 11,
                        ContentType = "application/octet-stream",
                        SafeDisplayName = "reservation.bin",
                        Purpose = StorageObjectPurposes.Attachment,
                        Visibility = StorageObjectVisibilities.AuthenticatedTenant,
                        Status = StorageUploadSessionStates.Reserved,
                        ExpiresAt = now.AddHours(1),
                        CreatedAt = now
                    });
                }
            }
            seed.AtprotoRecordTenantPresentations.AddRange(tenantIds.Select(id =>
                new AtprotoRecordTenantPresentation
                {
                    TenantId = id,
                    AtprotoRecordId = record.Id,
                    IsVisible = id == tenantIds[0],
                    SourceVersion = 1,
                    EvaluatedAt = now
                }));
            await seed.SaveChangesAsync();
            schema = seed.Model.FindEntityType(typeof(AtprotoRecord))!.GetSchema()!;
            presentationTable = $"\"{schema}\".\"{seed.Model.FindEntityType(typeof(AtprotoRecordTenantPresentation))!.GetTableName()}\"";
            actorPiiTable = $"\"{schema}\".\"{seed.Model.FindEntityType(typeof(ActorPii))!.GetTableName()}\"";
            revisionTable = $"\"{schema}\".\"{seed.Model.FindEntityType(typeof(EventDiscoveryRevision))!.GetTableName()}\"";
            epochs = await seed.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId))
                .ToDictionaryAsync(row => row.TenantId, row => row.DisclosureEpoch);
        }

        string role = "atproto_materialization_" + Guid.CreateVersion7().ToString("N");
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using var grant = administration.CreateCommand();
        grant.CommandText = $"""
            CREATE ROLE "{role}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            GRANT {PostgresAtprotoMaterializationContract.GlobalRole} TO "{role}";
            """;
        await grant.ExecuteNonQueryAsync();
        string? pooledConnection = null;
        string trigger = "materialization_failure_" + Guid.CreateVersion7().ToString("N");
        bool triggerInstalled = false;
        try
        {
            var configured = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
            var factory = new GlobalExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = configured.Host,
                Port = configured.Port,
                Database = configured.Database,
                Schema = schema,
                Username = role,
                Password = password,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            }, poolSize: 1);
            await using var writer = await factory.CreateDbContextAsync();
            pooledConnection = writer.Database.GetConnectionString();
            await writer.Database.OpenConnectionAsync();
            await using (var authority = writer.Database.GetDbConnection().CreateCommand())
            {
                authority.CommandText = $"""
                    SELECT current_user = '{role}', NOT rolsuper, NOT rolbypassrls,
                        NOT has_table_privilege(current_user, '{presentationTable}', 'SELECT'),
                        NOT has_table_privilege(current_user, '{presentationTable}', 'UPDATE'),
                        NOT has_table_privilege(current_user, '{actorPiiTable}', 'SELECT')
                    FROM pg_roles WHERE rolname = current_user;
                    """;
                await using var reader = await authority.ExecuteReaderAsync();
                await Assert.That(await reader.ReadAsync()).IsTrue();
                for (int index = 0; index < 6; index++)
                    await Assert.That(reader.GetBoolean(index)).IsTrue();
            }
            await Assert.That(await writer.AtprotoRecords.AsNoTracking()
                .AnyAsync(row => row.Id == record.Id)).IsTrue();
            var repository = new AtprotoJetstreamRepository((IAtprotoMaterializationDbContextFactory)factory);
            AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
                "https://jetstream.example/materialization", "materialization", now, TimeSpan.FromMinutes(5))
                ?? throw new InvalidOperationException("The restricted source role could not acquire its consumer.");
            await Assert.That(await repository.TryRenewAsync(claim, now, now.AddMinutes(5))).IsTrue();
            var projection = new AtprotoEventProjection
            {
                AtprotoRecordId = record.Id,
                Name = "Imported private graph",
                CreatedAt = new DateTimeOffset(now),
                StartsAt = new DateTimeOffset(now.AddDays(1)),
                EndsAt = new DateTimeOffset(now.AddDays(1).AddHours(1)),
                Mode = "#virtual",
                Status = "#scheduled",
                SourceUrl = "https://events.example/original",
                SourceVersion = 2,
                MaterializedAt = now
            };
            var supplied = new AtprotoRecord
            {
                Id = record.Id,
                Did = record.Did,
                Collection = record.Collection,
                RecordKey = record.RecordKey,
                Uri = record.Uri,
                RecordJson = "{\"name\":\"Imported private graph\"}",
                RecordHash = new string('b', 64),
                IndexedAt = now,
                UpdatedAt = now
            };
            var request = new AtprotoPdsSnapshotApplyRequest(
                claim, [did], [new AtprotoPdsSnapshot(did, [new(record.Collection, record.RecordKey)],
                    [new(supplied, projection)])], tenantIds, 2, now)
            {
                EventImports = tenantIds.Select(id => new AtprotoFederatedEventImportPlan(
                    id, record.Id, did, record.Uri!, projection.Name, projection.CreatedAt,
                    "Imported description", projection.SourceUrl, projection.StartsAt, projection.EndsAt,
                    projection.Mode, projection.Status, false)
                {
                    ParticipationConfiguration = new ConfigureEventParticipationDto
                    {
                        ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly,
                        AdvanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.NotApplicable
                    }
                }).ToArray()
            };

            if (scenario != Scenario.Graph)
                request = WithThumbnails(request, staged, ThumbnailCid);
            AtprotoPersistenceApplyResult result =
                await repository.TryReconcileWithResultAsync(request, CancellationToken.None);

            await Assert.That(result.Applied).IsTrue();
            await Assert.That(result.ConsumedStagedThumbnails).IsEquivalentTo(staged);
            await using var verify = fixture.CreateDbContext();
            Explore.Domain.Event[] imports = await verify.Events.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.AtprotoRecordId == record.Id).ToArrayAsync();
            await Assert.That(imports.Select(row => row.TenantId)).IsEquivalentTo(tenantIds);
            await Assert.That(imports.All(row => row.Title == projection.Name)).IsTrue();
            Guid[] ids = imports.Select(row => row.Id).ToArray();
            await Assert.That(await verify.EventSessions.IgnoreQueryFilters().CountAsync(row => ids.Contains(row.EventId)))
                .IsEqualTo(2);
            await Assert.That(await verify.EventParticipationConfigurations.IgnoreQueryFilters()
                .CountAsync(row => ids.Contains(row.Id))).IsEqualTo(2);
            await Assert.That(await verify.EventPublicActions.IgnoreQueryFilters()
                .CountAsync(row => ids.Contains(row.EventId))).IsEqualTo(2);
            await Assert.That(await verify.AtprotoJetstreamConsumerStates.Where(row => row.Id == claim.ConsumerStateId)
                .Select(row => row.Cursor).SingleAsync()).IsEqualTo(0);
            var revisions = await verify.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId))
                .ToDictionaryAsync(row => row.TenantId, row => row.DisclosureEpoch);
            foreach (Guid tenantId in tenantIds)
                await Assert.That(revisions[tenantId]).IsEqualTo(epochs[tenantId] + 1);
            await Assert.That(await verify.AtprotoRecords.Where(row => row.Id == record.Id)
                .Select(row => row.SourceVersion).SingleAsync()).IsEqualTo(2);
            if (scenario == Scenario.Graph)
                return;
            await Assert.That(await verify.Set<StorageProducerOperation>().CountAsync()).IsEqualTo(0);
            var initialUsage = await verify.StorageUsageCounters.IgnoreQueryFilters().AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId)).ToArrayAsync();
            await Assert.That(initialUsage.Length).IsEqualTo(2);
            foreach (var usage in initialUsage)
            {
                await Assert.That(usage.UsedBytes).IsEqualTo(13);
                await Assert.That(usage.ReservedBytes).IsEqualTo(11);
                await Assert.That(usage.ObjectCount).IsEqualTo(2);
            }
            if (scenario == Scenario.Promotion)
                return;

            Guid? preservedOwnerId = null;
            await using (var configure = fixture.CreateDbContext())
            {
                var preserved = await configure.Events.IgnoreQueryFilters()
                    .Where(row => ids.Contains(row.Id)).ToArrayAsync();
                foreach (var item in preserved)
                {
                    item.BackgroundColor = "#123456";
                    item.TotalViews = 37;
                }
                if (scenario == Scenario.ReferencedImage)
                {
                    preservedOwnerId = Guid.CreateVersion7();
                    configure.Events.Add(new Explore.Domain.Event(EventStatusEnum.Published)
                    {
                        Id = preservedOwnerId.Value,
                        TenantId = tenantIds[0],
                        Tenant = null!,
                        ActorId = actor.Id,
                        Actor = null!,
                        Title = "Independent owner",
                        EventStatus = null!,
                        EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                        VisibilityType = null!,
                        EventFormatId = (int)EventFormatEnum.Digital,
                        EventFormat = null!,
                        FeaturedImageId = staged[0].OperationId,
                        CreatedAt = now
                    });
                }
                await configure.SaveChangesAsync();
            }

            var replacementStages = new List<StagedStorageWrite>();
            Dictionary<Guid, Guid> replacementStamps;
            Dictionary<Guid, long> beforeReplacement;
            await using (var stage = fixture.CreateDbContext())
            {
                foreach (Guid tenantId in tenantIds)
                {
                    Guid operationId = Guid.CreateVersion7();
                    string key = "replacement/" + operationId.ToString("N");
                    var operation = StorageProducerOperation.Create(operationId, tenantId, binding, key, now);
                    operation.Settle(binding.Id, binding.Provider, key, null);
                    stage.Add(operation);
                    replacementStages.Add(new(operationId, tenantId, binding.Id,
                        new FileStorageWriteResult(binding.Provider, key, 8, "image/png",
                            Sha256Checksum: ReplacementChecksum)));
                }
                await stage.SaveChangesAsync();
                Guid[] operationIds = replacementStages.Select(value => value.OperationId).ToArray();
                replacementStamps = await stage.Set<StorageProducerOperation>().AsNoTracking()
                    .Where(row => operationIds.Contains(row.Id))
                    .ToDictionaryAsync(row => row.Id, row => row.ConcurrencyStamp);
                beforeReplacement = await stage.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
                    .Where(row => tenantIds.Contains(row.TenantId))
                    .ToDictionaryAsync(row => row.TenantId, row => row.DisclosureEpoch);
            }
            if (scenario == Scenario.Rollback)
            {
                grant.CommandText = $"""
                    CREATE FUNCTION "{schema}"."{trigger}"() RETURNS trigger LANGUAGE plpgsql AS $body$
                    BEGIN
                        IF NEW.tenant_id = '{tenantIds[0]}'::uuid THEN
                            RAISE EXCEPTION 'injected_materialization_epoch_failure' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END; $body$;
                    REVOKE ALL ON FUNCTION "{schema}"."{trigger}"() FROM PUBLIC;
                    CREATE TRIGGER "{trigger}" BEFORE UPDATE ON {revisionTable}
                    FOR EACH ROW EXECUTE FUNCTION "{schema}"."{trigger}"();
                    """;
                await grant.ExecuteNonQueryAsync();
                triggerInstalled = true;
            }
            supplied.RecordJson = "{\"name\":\"Replacement private graph\"}";
            var replacementProjection = new AtprotoEventProjection
            {
                AtprotoRecordId = record.Id,
                Name = "Replacement private graph",
                CreatedAt = projection.CreatedAt,
                StartsAt = projection.StartsAt,
                EndsAt = projection.EndsAt,
                Mode = projection.Mode,
                Status = projection.Status,
                SourceUrl = projection.SourceUrl,
                SourceVersion = 3,
                MaterializedAt = now.AddSeconds(1)
            };
            var replacement = WithThumbnails(request with
            {
                SnapshotVersion = 3,
                ObservedAt = now.AddSeconds(1),
                Snapshots = [new AtprotoPdsSnapshot(did, [new(record.Collection, record.RecordKey)],
                    [new(supplied, replacementProjection)])],
                EventImports = request.EventImports.Select(import => import with
                {
                    Name = replacementProjection.Name
                }).ToArray()
            }, replacementStages, ReplacementCid);
            bool rejected = false;
            AtprotoPersistenceApplyResult? replacementResult = null;
            try
            {
                replacementResult = await repository.TryReconcileWithResultAsync(replacement, CancellationToken.None);
            }
            catch (Exception exception) when (exception.GetBaseException() is PostgresException
            { SqlState: "23514", MessageText: "injected_materialization_epoch_failure" })
            {
                rejected = true;
            }
            bool rolledBack = scenario == Scenario.Rollback;
            await Assert.That(rejected).IsEqualTo(rolledBack);
            if (!rolledBack)
            {
                await Assert.That(replacementResult!.Applied).IsTrue();
                await Assert.That(replacementResult.ConsumedStagedThumbnails).IsEquivalentTo(replacementStages);
            }
            await using var after = fixture.CreateDbContext();
            var storedEvents = await after.Events.IgnoreQueryFilters().AsNoTracking()
                .Where(row => ids.Contains(row.Id)).ToArrayAsync();
            foreach (var item in storedEvents)
            {
                await Assert.That(item.BackgroundColor).IsEqualTo("#123456");
                await Assert.That(item.TotalViews).IsEqualTo(37);
                await Assert.That(item.Title).IsEqualTo(rolledBack ? projection.Name : replacementProjection.Name);
                Guid expectedImage = (rolledBack ? staged : replacementStages)
                    .Single(stage => stage.TenantId == item.TenantId).OperationId;
                await Assert.That(item.FeaturedImageId).IsEqualTo(expectedImage);
            }
            var custody = await after.StorageObjectDeletionTombstones.AsNoTracking().ToArrayAsync();
            await Assert.That(custody.Length).IsEqualTo(rolledBack ? 0 : scenario == Scenario.ReferencedImage ? 1 : 2);
            foreach (var target in custody)
            {
                var original = staged.Single(stage => stage.OperationId == target.Id);
                await Assert.That(target.ProviderBindingId).IsEqualTo(original.BindingId);
                await Assert.That(target.ObjectKey).IsEqualTo(original.Write.ObjectKey);
                await Assert.That(target.TenantId).IsEqualTo(original.TenantId);
                await Assert.That(target.State).IsEqualTo(StorageObjectDeletionState.Ready);
            }
            var retained = await after.StorageObjects.IgnoreQueryFilters().AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId)).ToArrayAsync();
            foreach (var original in staged)
                await Assert.That(retained.Any(row => row.Id == original.OperationId))
                    .IsEqualTo(rolledBack || scenario == Scenario.ReferencedImage && original.TenantId == tenantIds[0]);
            foreach (var stage in replacementStages)
                await Assert.That(retained.Any(row => row.Id == stage.OperationId)).IsEqualTo(!rolledBack);
            var remaining = await after.Set<StorageProducerOperation>().AsNoTracking().ToArrayAsync();
            await Assert.That(remaining.Length).IsEqualTo(rolledBack ? 2 : 0);
            foreach (var operation in remaining)
            {
                await Assert.That(operation.ProducerSettled).IsTrue();
                await Assert.That(operation.ConcurrencyStamp).IsEqualTo(replacementStamps[operation.Id]);
            }
            var usageAfter = await after.StorageUsageCounters.IgnoreQueryFilters().AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId)).ToArrayAsync();
            foreach (var usage in usageAfter)
            {
                bool sharedImage = scenario == Scenario.ReferencedImage && usage.TenantId == tenantIds[0];
                await Assert.That(usage.UsedBytes).IsEqualTo(sharedImage ? 21 : 13);
                await Assert.That(usage.ReservedBytes).IsEqualTo(11);
                await Assert.That(usage.ObjectCount).IsEqualTo(sharedImage ? 3 : 2);
            }
            var epochsAfter = await after.Set<EventDiscoveryRevision>().IgnoreQueryFilters().AsNoTracking()
                .Where(row => tenantIds.Contains(row.TenantId))
                .ToDictionaryAsync(row => row.TenantId, row => row.DisclosureEpoch);
            foreach (Guid tenantId in tenantIds)
                await Assert.That(epochsAfter[tenantId]).IsEqualTo(beforeReplacement[tenantId] + (rolledBack ? 0 : 1));
            await Assert.That(await after.AtprotoRecords.Where(row => row.Id == record.Id)
                .Select(row => row.SourceVersion).SingleAsync()).IsEqualTo(rolledBack ? 2 : 3);
            await Assert.That(await after.AtprotoJetstreamConsumerStates.Where(row => row.Id == claim.ConsumerStateId)
                .Select(row => row.Cursor).SingleAsync()).IsEqualTo(0);
            if (preservedOwnerId is { } ownerId)
                await Assert.That(await after.Events.Where(row => row.Id == ownerId)
                    .Select(row => row.FeaturedImageId).SingleAsync()).IsEqualTo(staged[0].OperationId);
        }
        finally
        {
            if (triggerInstalled)
            {
                grant.CommandText = $"""
                    DROP TRIGGER "{trigger}" ON {revisionTable};
                    DROP FUNCTION "{schema}"."{trigger}"();
                    """;
                await grant.ExecuteNonQueryAsync();
            }
            if (pooledConnection is not null)
            {
                using var connection = new NpgsqlConnection(pooledConnection);
                NpgsqlConnection.ClearPool(connection);
            }
            grant.CommandText = $"""
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                """;
            await grant.ExecuteNonQueryAsync();
        }
    }

    private static AtprotoPdsSnapshotApplyRequest WithThumbnails(
        AtprotoPdsSnapshotApplyRequest request, IReadOnlyList<StagedStorageWrite> stages, string cid) => request with
        {
            EventImports = request.EventImports.Select(import => import with
            {
                Thumbnail = new AtprotoThumbnailBlobCandidate(import.Did, cid, "image/png", 8),
                StagedThumbnail = stages.Single(stage => stage.TenantId == import.TenantId)
            }).ToArray()
        };

    public enum Scenario
    {
        Graph,
        Promotion,
        Replacement,
        ReferencedImage,
        Rollback
    }
}
