using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Settings.Documents.Payloads;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Api.IntegrationTests.Authentication;

public sealed class InstanceOperatorIdentityPortabilityContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ExportImportPreservesTargetAuthorityRotatesRevisionAndAuditsOnlyDigests()
    {
        await using var target = await Scenario.CreateAsync();
        await target.SeedAsync();
        var before = await target.ExportAsync();
        var stored = await target.DocumentAsync();
        var source = Manifest(stored! with { OperatorId = Guid.CreateVersion7(), LegalName = "Replacement Association" });
        using var response = await target.ImportAsync(source, before.RevisionHash);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await ReadResultAsync(response);
        await Assert.That(result.IsSuccess).IsTrue();
        var after = await target.ExportAsync();
        await Assert.That(after.Document.GetProperty("legalName").GetString()).IsEqualTo("Replacement Association");
        await Assert.That(result.Id!.OperatorId).IsEqualTo(stored.OperatorId!.Value);
        await Assert.That(result.Id.Revision).IsNotEqualTo(stored.Revision!.Value);
        await Assert.That(result.Id.PaidCommerce.IsReady).IsTrue();
        var events = await target.EventsAsync();
        await Assert.That(events.Count).IsEqualTo(1);
        var message = events.Single();
        var audit = JsonSerializer.Deserialize<OperatorIdentityImportAudit>(message.Payload!)!;
        await Assert.That(audit.ActorUserId).IsEqualTo(target.Fixture.Actor);
        await Assert.That(message.EventType).IsEqualTo(OperatorIdentityImportAudit.EventType);
        await Assert.That(audit.ExpectedRevisionHash).IsEqualTo(before.RevisionHash);
        await Assert.That(audit.ContentDigest).IsEqualTo(source.ContentDigest);
        await Assert.That(audit.CommittedRevision).IsEqualTo(result.Id.Revision);
        await Assert.That(JsonSerializer.Serialize(audit).Contains("Replacement", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task InvalidLegalIdentityCannotReplaceCurrentDocumentOrEmitSuccessAudit()
    {
        await using var target = await Scenario.CreateAsync();
        await target.SeedAsync();
        var before = await target.ExportAsync();
        var candidate = (await target.DocumentAsync())!;
        InstanceOperatorIdentitySettings[] invalid =
        [
            candidate with { LegalName = null }, candidate with { PublicContactEmail = "invalid-address" },
            candidate with { RegistrationIdentifier = "" }, candidate with { JurisdictionCountryCode = "ZZ" },
            candidate with { TermsUrl = null }
        ];
        foreach (var value in invalid)
        {
            using var response = await target.ImportAsync(Manifest(value), before.RevisionHash);
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
            await Assert.That((await target.ExportAsync()).ContentDigest).IsEqualTo(before.ContentDigest);
        }
        await Assert.That(await target.EventsAsync()).IsEmpty();
    }

    [Test]
    public async Task StaleRevisionAndCompetingImportsCannotOverwriteWinningDocument()
    {
        await using var target = await Scenario.CreateAsync();
        await target.SeedAsync();
        var before = await target.ExportAsync();
        var manifest = Manifest((await target.DocumentAsync())! with { LegalName = "Winning Association" });
        using var first = target.Fixture.CreateClient();
        using var second = target.Fixture.CreateClient();
        await target.Fixture.AuthorizeAsync(first);
        await target.Fixture.AuthorizeAsync(second);
        var request = new ImportInstanceOperatorIdentityCommand(manifest, before.RevisionHash);
        var attempts = new[]
        {
            first.PostAsJsonAsync("/api/instance-operator-identity/manifest/import", request),
            second.PostAsJsonAsync("/api/instance-operator-identity/manifest/import", request)
        };
        var responses = await Task.WhenAll(attempts);
        try
        {
            await Assert.That(responses.Count(response => response.StatusCode == HttpStatusCode.OK)).IsEqualTo(1);
            var conflict = responses.Single(response => response.StatusCode != HttpStatusCode.OK);
            await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That(conflict.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
            await Assert.That(await conflict.Content.ReadAsStringAsync()).Contains("concurrent_update");
            await Assert.That((await target.EventsAsync()).Count).IsEqualTo(1);
            await Assert.That((await target.ExportAsync()).Document.GetProperty("legalName").GetString())
                .IsEqualTo("Winning Association");
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Test]
    public async Task TamperingOfficialClaimsAndNonAdminCallersFailClosed()
    {
        await using var target = await Scenario.CreateAsync();
        await target.SeedAsync();
        var before = await target.ExportAsync();
        OperatorIdentityManifest[] invalid =
        [
            before with { ContentDigest = new string('0', 64) }, before with { RevisionHash = new string('0', 64) },
            Manifest((await target.DocumentAsync())! with { IsOfficialInstance = true })
        ];
        foreach (var value in invalid)
        {
            using var response = await target.ImportAsync(value, before.RevisionHash);
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
        }
        await using (var context = target.Fixture.Local.Native.CreateDatabase())
        {
            await context.PlatformUserRoles.Where(role => role.UserId == target.Fixture.Actor).ExecuteDeleteAsync();
        }
        using var deniedImport = await target.ImportAsync(before, before.RevisionHash);
        using var deniedExport = await target.Client.GetAsync("/api/instance-operator-identity/manifest");
        await Assert.That(deniedImport.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(deniedExport.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await target.EventsAsync()).IsEmpty();
        await Assert.That((await target.DocumentAsync())!.IsOfficialInstance).IsFalse();
    }

    [Test]
    public async Task PersistenceFailureRollsBackWithoutAuditAndMissingTargetUsesExplicitRevision()
    {
        var fault = new ImportCommitFailure();
        await using var target = await Scenario.CreateAsync(fault);
        var source = Manifest(Complete());
        fault.Enabled = true;
        using var failed = await target.ImportAsync(source, OperatorIdentityManifestJson.RevisionHash(null));
        await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(fault.SawIdentityAndAudit).IsTrue();
        await Assert.That(await target.DocumentAsync()).IsNull();
        await Assert.That(await target.EventsAsync()).IsEmpty();
        fault.Enabled = false;
        using var succeeded = await target.ImportAsync(source, OperatorIdentityManifestJson.RevisionHash(null));
        var result = await ReadResultAsync(succeeded);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.OperatorId).IsNotEqualTo(source.Document.GetProperty("operatorId").GetGuid());
    }

    [Test]
    public async Task AuditPersistenceFailureAfterIdentityWriteRollsBackBothChanges()
    {
        var fault = new ImportCommitFailure();
        await using var target = await Scenario.CreateAsync(fault);
        await target.SeedAsync();
        var before = await target.ExportAsync();
        fault.Enabled = true;
        using var failed = await target.ImportAsync(
            Manifest((await target.DocumentAsync())! with { LegalName = "Must Roll Back" }), before.RevisionHash);
        await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(fault.SawIdentityAndAudit).IsTrue();
        fault.Enabled = false;
        await Assert.That((await target.ExportAsync()).ContentDigest).IsEqualTo(before.ContentDigest);
        await Assert.That(await target.EventsAsync()).IsEmpty();
    }

    private static async Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>> ReadResultAsync(
        HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>>())!;

    private static OperatorIdentityManifest Manifest(InstanceOperatorIdentitySettings settings) =>
        OperatorIdentityManifestJson.Create(JsonSerializer.SerializeToElement(settings, JsonOptions));

    private static InstanceOperatorIdentitySettings Complete() => new()
    {
        OperatorId = Guid.CreateVersion7(),
        Revision = Guid.CreateVersion7(),
        PublicName = "Independent Operator",
        LegalName = "Independent ASBL",
        OperatorKindCode = "registered_organization",
        JurisdictionCountryCode = "BE",
        RegistrationIdentifier = "BE 0123.456.789",
        PublicContactEmail = "contact@example.test",
        WebsiteUrl = "https://example.test",
        LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms",
        PrivacyUrl = "https://example.test/privacy",
        OfficialOrigin = "https://example.test",
        IsOfficialInstance = false
    };

    private sealed class Scenario(OperatorIdentityHttpMigrationFixture fixture, HttpClient client) : IAsyncDisposable
    {
        internal OperatorIdentityHttpMigrationFixture Fixture => fixture;
        internal HttpClient Client => client;
        internal static async Task<Scenario> CreateAsync(ImportCommitFailure? fault = null)
        {
            var fixture = await OperatorIdentityHttpMigrationFixture.CreateAsync(interceptor: fault);
            var client = fixture.CreateClient();
            try { await fixture.AuthorizeAsync(client); return new(fixture, client); }
            catch { client.Dispose(); await fixture.DisposeAsync(); throw; }
        }
        internal async Task SeedAsync()
        {
            var request = JsonSerializer.Deserialize<SaveInstanceOperatorIdentityRequestDto>(
                JsonSerializer.Serialize(Complete(), JsonOptions), JsonOptions)!;
            var result = await fixture.SaveAsync(request);
            await Assert.That(result.IsSuccess).IsTrue();
        }
        internal Task<HttpResponseMessage> ImportAsync(OperatorIdentityManifest manifest, string expectedRevision) =>
            client.PostAsJsonAsync("/api/instance-operator-identity/manifest/import",
                new ImportInstanceOperatorIdentityCommand(manifest, expectedRevision));
        internal async Task<OperatorIdentityManifest> ExportAsync()
        {
            using var response = await client.GetAsync("/api/instance-operator-identity/manifest");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<OperatorIdentityManifest>())!;
        }
        internal async Task<InstanceOperatorIdentitySettings?> DocumentAsync()
        {
            await using var context = fixture.Local.Native.CreateDatabase();
            string? value = await context.SystemSettings.Where(row =>
                row.SettingKey == Explore.Application.Settings.InstanceOperatorIdentitySettingKeys.OperatorIdentity)
                .Select(row => row.Value).SingleOrDefaultAsync();
            return value is null ? null : JsonSerializer.Deserialize<InstanceOperatorIdentitySettings>(value, JsonOptions);
        }
        internal async Task<List<OutboxMessage>> EventsAsync()
        {
            await using var context = fixture.Local.Native.CreateDatabase();
            return await context.OutboxMessages.AsNoTracking()
                .Where(row => row.EventType == OperatorIdentityImportAudit.EventType).ToListAsync();
        }
        public async ValueTask DisposeAsync() { client.Dispose(); await fixture.DisposeAsync(); }
    }

    internal sealed class ImportCommitFailure : DbTransactionInterceptor
    {
        internal bool Enabled { get; set; }
        internal bool SawIdentityAndAudit { get; private set; }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled) return result;
            var context = eventData.Context!;
            await using var command = transaction.Connection!.CreateCommand();
            command.Transaction = transaction;
            var setting = context.Model.FindEntityType(typeof(SystemSetting));
            var outbox = context.Model.FindEntityType(typeof(OutboxMessage));
            if (setting is null || outbox is null) return result;
            command.CommandText = $"SELECT (SELECT COUNT(*) FROM \"{setting.GetTableName()}\" WHERE setting_key = @key)"
                + $" + (SELECT COUNT(*) FROM \"{outbox.GetTableName()}\" WHERE event_type = @event)";
            var key = command.CreateParameter();
            key.ParameterName = "@key";
            key.Value = Explore.Application.Settings.InstanceOperatorIdentitySettingKeys.OperatorIdentity;
            command.Parameters.Add(key);
            var type = command.CreateParameter();
            type.ParameterName = "@event";
            type.Value = OperatorIdentityImportAudit.EventType;
            command.Parameters.Add(type);
            SawIdentityAndAudit = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 2;
            if (!SawIdentityAndAudit) return result;
            throw new IOException("Reject durability after actual identity and import audit writes.");
        }
    }
}
