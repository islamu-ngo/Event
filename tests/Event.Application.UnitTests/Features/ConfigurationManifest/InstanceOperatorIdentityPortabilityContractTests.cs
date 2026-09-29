namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.Handlers.Queries;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Settings.Documents.Payloads;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using NSubstitute;

public sealed class InstanceOperatorIdentityPortabilityContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ExportImportPreservesTargetAuthorityRotatesRevisionAndAuditsOnlyDigests()
    {
        using var target = new Scenario();
        await target.SeedAsync();
        OperatorIdentityManifest before = await target.ExportAsync();
        InstanceOperatorIdentitySettings stored = target.Document;
        OperatorIdentityManifest source = Manifest(stored with
        {
            OperatorId = Guid.CreateVersion7(),
            LegalName = "Replacement Association"
        });

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> result = await target.Import.ExecuteAsync(
            new(source, before.RevisionHash), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        OperatorIdentityManifest after = await target.ExportAsync();
        await Assert.That(after.Document.GetProperty("legalName").GetString()).IsEqualTo("Replacement Association");
        await Assert.That(result.Id!.OperatorId).IsEqualTo(stored.OperatorId!.Value);
        await Assert.That(result.Id.Revision).IsNotEqualTo(stored.Revision!.Value);
        await Assert.That(result.Id.PaidCommerce.IsReady).IsTrue();
        await Assert.That(target.Store.Events.Count).IsEqualTo(1);
        OutboxMessage message = target.Store.Events.Single();
        OperatorIdentityImportAudit audit = JsonSerializer.Deserialize<OperatorIdentityImportAudit>(message.Payload!)!;
        await Assert.That(audit.ActorUserId).IsEqualTo(target.ActorId);
        await Assert.That(message.EventType).IsEqualTo(OperatorIdentityImportAudit.EventType);
        await Assert.That(audit.ExpectedRevisionHash).IsEqualTo(before.RevisionHash);
        await Assert.That(audit.ContentDigest).IsEqualTo(source.ContentDigest);
        await Assert.That(audit.CommittedRevision).IsEqualTo(result.Id.Revision);
        await Assert.That(JsonSerializer.Serialize(audit).Contains("Replacement", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task InvalidLegalIdentityCannotReplaceCurrentDocumentOrEmitSuccessAudit()
    {
        using var target = new Scenario();
        await target.SeedAsync();
        OperatorIdentityManifest before = await target.ExportAsync();
        InstanceOperatorIdentitySettings candidate = target.Document;
        InstanceOperatorIdentitySettings[] invalid =
        [
            candidate with { LegalName = null },
            candidate with { PublicContactEmail = "invalid-address" },
            candidate with { RegistrationIdentifier = "" },
            candidate with { JurisdictionCountryCode = "ZZ" },
            candidate with { TermsUrl = null }
        ];
        foreach (InstanceOperatorIdentitySettings value in invalid)
        {
            BaseCommandResponse<InstanceOperatorIdentitySavedDocument> result = await target.Import.ExecuteAsync(
                new(Manifest(value), before.RevisionHash), CancellationToken.None);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That((await target.ExportAsync()).ContentDigest).IsEqualTo(before.ContentDigest);
        }
        await Assert.That(target.Store.Events).IsEmpty();
    }

    [Test]
    public async Task StaleRevisionAndCompetingImportsCannotOverwriteWinningDocument()
    {
        using var target = new Scenario();
        await target.SeedAsync();
        OperatorIdentityManifest before = await target.ExportAsync();
        var command = new ImportInstanceOperatorIdentityCommand(
            Manifest(target.Document with { LegalName = "Winning Association" }), before.RevisionHash);
        Task<bool>[] attempts = [Attempt(), Attempt()];
        bool[] outcomes = await Task.WhenAll(attempts);
        await Assert.That(outcomes.Count(success => success)).IsEqualTo(1);
        await Assert.That(target.Store.Events.Count).IsEqualTo(1);
        await Assert.That((await target.ExportAsync()).Document.GetProperty("legalName").GetString())
            .IsEqualTo("Winning Association");

        async Task<bool> Attempt()
        {
            try { return (await target.Import.ExecuteAsync(command, CancellationToken.None)).IsSuccess; }
            catch (ConcurrencyConflictException exception)
            {
                await Assert.That(exception.Code).IsEqualTo("concurrent_update");
                return false;
            }
        }
    }

    [Test]
    public async Task TamperingOfficialClaimsAndNonAdminCallersFailClosed()
    {
        using var target = new Scenario();
        await target.SeedAsync();
        OperatorIdentityManifest before = await target.ExportAsync();
        OperatorIdentityManifest[] invalid =
        [
            before with { ContentDigest = new string('0', 64) },
            before with { RevisionHash = new string('0', 64) },
            Manifest(target.Document with { IsOfficialInstance = true })
        ];
        foreach (OperatorIdentityManifest value in invalid)
        {
            await Assert.That((await target.Import.ExecuteAsync(new(value, before.RevisionHash),
                CancellationToken.None)).IsSuccess).IsFalse();
        }
        target.Admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        await Assert.That((await target.Import.ExecuteAsync(new(before, before.RevisionHash),
            CancellationToken.None)).IsSuccess).IsFalse();
        await Assert.That((await target.Export.QueryAsync(new(), CancellationToken.None)).IsSuccess).IsFalse();
        await Assert.That(target.Store.Events).IsEmpty();
        await Assert.That(target.Document.IsOfficialInstance).IsFalse();
    }

    [Test]
    public async Task PersistenceFailureRollsBackWithoutAuditAndMissingTargetUsesExplicitRevision()
    {
        using var target = new Scenario();
        OperatorIdentityManifest source = Manifest(Complete());
        target.Store.FailWrites = true;
        _ = await Assert.ThrowsAsync<IOException>(() => target.Import.ExecuteAsync(
            new(source, OperatorIdentityManifestJson.RevisionHash(null)), CancellationToken.None));
        await Assert.That((await target.Identity.GetCurrentAsync()).Settings).IsNull();
        await Assert.That(target.Store.Events).IsEmpty();
        target.Store.FailWrites = false;
        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> result = await target.Import.ExecuteAsync(
            new(source, OperatorIdentityManifestJson.RevisionHash(null)), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.OperatorId).IsNotEqualTo(source.Document.GetProperty("operatorId").GetGuid());
    }

    [Test]
    public async Task AuditPersistenceFailureAfterIdentityWriteRollsBackBothChanges()
    {
        using var target = new Scenario();
        await target.SeedAsync();
        OperatorIdentityManifest before = await target.ExportAsync();
        target.Store.FailAudit = true;
        _ = await Assert.ThrowsAsync<IOException>(() => target.Import.ExecuteAsync(
            new(Manifest(target.Document with { LegalName = "Must Roll Back" }), before.RevisionHash),
            CancellationToken.None));
        await Assert.That(target.Store.AuditSawUpdatedIdentity).IsTrue();
        await Assert.That((await target.ExportAsync()).ContentDigest).IsEqualTo(before.ContentDigest);
        await Assert.That(target.Store.Events).IsEmpty();
    }

    private static OperatorIdentityManifest Manifest(InstanceOperatorIdentitySettings settings) =>
        OperatorIdentityManifestJson.Create(JsonSerializer.SerializeToElement(settings, JsonOptions));

    private static InstanceOperatorIdentitySettings Complete() => new()
    {
        OperatorId = Guid.CreateVersion7(), Revision = Guid.CreateVersion7(),
        PublicName = "Independent Operator", LegalName = "Independent ASBL",
        OperatorKindCode = "registered_organization", JurisdictionCountryCode = "BE",
        RegistrationIdentifier = "BE 0123.456.789", PublicContactEmail = "contact@example.test",
        WebsiteUrl = "https://example.test", LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms", PrivacyUrl = "https://example.test/privacy",
        OfficialOrigin = "https://example.test", IsOfficialInstance = false
    };

    private sealed class Scenario : IDisposable
    {
        public Scenario()
        {
            IOutboxRepository outbox = Substitute.For<IOutboxRepository>();
            outbox.CreateRange(Arg.Any<IReadOnlyCollection<OutboxMessage>>(), Arg.Any<CancellationToken>())
                .Returns(call => Store.RecordAudit(call.Arg<IReadOnlyCollection<OutboxMessage>>()));
            Identity = new(Store, Store, outbox);
            Admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
            Admin.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(ActorId);
            Export = new(Identity, Admin);
            Import = new(Identity, Admin);
        }
        public Guid ActorId { get; } = Guid.CreateVersion7();
        public Store Store { get; } = new();
        public IAdminContext Admin { get; } = Substitute.For<IAdminContext>();
        public InstanceOperatorIdentityService Identity { get; }
        public ExportInstanceOperatorIdentityQueryHandler Export { get; }
        public ImportInstanceOperatorIdentityCommandHandler Import { get; }
        public InstanceOperatorIdentitySettings Document =>
            JsonSerializer.Deserialize<InstanceOperatorIdentitySettings>(Store.Current!.Value, JsonOptions)!;
        public async Task SeedAsync() => _ = await Identity.SaveAsync(Complete(), null);
        public async Task<OperatorIdentityManifest> ExportAsync() =>
            (await Export.QueryAsync(new(), CancellationToken.None)).Id!;
        public void Dispose() => Store.Dispose();
    }

    // A transactional in-memory store keeps the production service and evaluator in the test.
    private sealed class Store : ISystemSettingRepository, IUnitOfWork, IDisposable
    {
        private readonly SemaphoreSlim _transaction = new(1);
        public SystemSetting? Current { get; private set; }
        public bool FailWrites { get; set; }
        public bool FailAudit { get; set; }
        public bool AuditSawUpdatedIdentity { get; private set; }
        public List<OutboxMessage> Events { get; } = [];
        public Task<IReadOnlyList<OutboxMessage>> RecordAudit(IReadOnlyCollection<OutboxMessage> messages)
        {
            AuditSawUpdatedIdentity = Current?.Value.Contains("Must Roll Back", StringComparison.Ordinal) == true;
            Events.AddRange(messages);
            if (FailAudit) throw new IOException("Audit persistence unavailable.");
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(messages.ToArray());
        }
        private static SystemSetting? Copy(SystemSetting? source) => source is null ? null : new()
        {
            Id = source.Id, SettingKey = source.SettingKey, Value = source.Value,
            ValueType = source.ValueType, CreatedAt = source.CreatedAt, CreatedBy = source.CreatedBy,
            UpdatedAt = source.UpdatedAt, UpdatedBy = source.UpdatedBy
        };
        public Task<SystemSetting?> GetByKey(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current?.SettingKey == key ? Copy(Current) : null);
        public Task<string?> UpsertInCurrentTransactionAsync(SystemSetting setting, CancellationToken cancellationToken = default)
        {
            string? previous = Current?.Value;
            Current = Copy(setting);
            if (FailWrites) throw new IOException("Persistence unavailable.");
            return Task.FromResult(previous);
        }
        public Task<string?> UpsertAsync(SystemSetting setting, CancellationToken cancellationToken = default) =>
            UpsertInCurrentTransactionAsync(setting, cancellationToken);
        public Task<string?> UpsertLockAsync(SystemSetting setting, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<List<SystemSetting>> GetAllSettings(string? category = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current is null ? new List<SystemSetting>() : [Copy(Current)!]);
        public Task<bool> IsLocked(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public async Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            await _transaction.WaitAsync(ct);
            SystemSetting? before = Copy(Current);
            int eventCount = Events.Count;
            try { return await operation(ct); }
            catch { Current = before; Events.RemoveRange(eventCount, Events.Count - eventCount); throw; }
            finally { _transaction.Release(); }
        }
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            ExecuteSerializableAsync(operation, ct);
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            ExecuteSerializableAsync(operation, ct);
        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
            _ = await ExecuteSerializableAsync(async token => { await operation(token); return true; }, ct);
        public void Dispose() => _transaction.Dispose();
    }
}
