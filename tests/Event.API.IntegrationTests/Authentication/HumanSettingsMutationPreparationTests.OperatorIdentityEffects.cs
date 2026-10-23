using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Explore.Persistence.Services;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(false, "value")]
    [Arguments(false, "lock")]
    [Arguments(false, "actor")]
    [Arguments(false, "failure")]
    [Arguments(true, "unchanged")]
    [Arguments(true, "audit")]
    [Arguments(true, "audit-status")]
    [Arguments(true, "failure")]
    public async Task OperatorIdentityExactEffectAndAuditSettleAtomically(bool import, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        var previous = CompleteOperatorIdentity() with { IsOfficialInstance = !import };
        string before = JsonSerializer.Serialize(previous, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.SystemSettings.Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                Value = before,
                ValueType = SettingValueType.Json,
                IsLocked = true,
                Category = "OperatorIdentity",
                Description = "Original metadata",
                DisplayOrder = 17,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                CreatedBy = fixture.Binding.LocalSubjectId
            });
            await seed.SaveChangesAsync();
        }
        var notifications = new OperatorNotifications(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            RegisterOperatorIdentityCustody(services);
            services.AddScoped<OperatorIdentitySettingsWriter>();
            services.AddScoped<IOperatorIdentitySettingsWriter, OperatorWriterProbe>();
            services.AddSingleton<INotificationHandler<SettingChangedNotification>>(notifications);
        }));
        await using var request = await OperatorIdentityRequestAsync(host.Services, await fixture.SignInAsync(), null);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var owner = services.GetRequiredService<ExploreDbContext>();
        var probe = (OperatorWriterProbe)services.GetRequiredService<IOperatorIdentitySettingsWriter>();
        probe.AfterWrite = async (mutation, token) =>
        {
            // The real writer has staged both row and audit and registered its terminal checks.
            await Assert.That(await owner.SystemSettings.Where(row =>
                row.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity)
                .Select(row => row.Value).SingleAsync(token)).IsEqualTo(mutation.After.Value);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => probe.Inner.ExecuteAsync(mutation, token));
            if (change == "value")
                await owner.SystemSettings.Where(row => row.Id == mutation.After.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value,
                        mutation.After.Value.Replace("Replacement", "Substitution", StringComparison.Ordinal)), token);
            if (change == "lock")
                await owner.SystemSettings.Where(row => row.Id == mutation.After.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, false), token);
            if (change == "actor")
                await owner.SystemSettings.Where(row => row.Id == mutation.After.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedBy, Guid.CreateVersion7()), token);
            if (change == "audit")
                await owner.OutboxMessages.Where(row => row.Id == mutation.AuditId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Payload, "{}"), token);
            if (change == "audit-status")
                await owner.OutboxMessages.Where(row => row.Id == mutation.AuditId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, OutboxMessageStatus.Completed), token);
            if (change == "failure")
                throw new IOException("Reject the original owner after its provisional operator write.");
        };
        var candidate = previous with { LegalName = "Replacement Operator", OperatorId = Guid.CreateVersion7() };
        var manifest = OperatorIdentityManifestJson.Create(JsonSerializer.SerializeToElement(candidate,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var importCommand = new ImportInstanceOperatorIdentityCommand(manifest,
            OperatorIdentityManifestJson.RevisionHash(previous.Revision));
        var saveCommand = new SaveInstanceOperatorIdentityCommand
        {
            Request = OperatorDraft() with { ExpectedRevision = previous.Revision, LegalName = "Replacement Operator" }
        };
        async Task Execute()
        {
            if (import)
            {
                var response = await ActivatorUtilities.CreateInstance<ImportInstanceOperatorIdentityCommandHandler>(services)
                    .ExecuteAsync(importCommand, CancellationToken.None);
                await Assert.That(response.IsSuccess).IsTrue();
                await Assert.That(response.Id!.PaidCommerce.IsReady).IsTrue();
                await Assert.That(response.Id.OperatorId).IsEqualTo(previous.OperatorId!.Value);
            }
            else
            {
                var response = await ActivatorUtilities.CreateInstance<SaveInstanceOperatorIdentityCommandHandler>(services)
                    .ExecuteAsync(saveCommand, CancellationToken.None);
                await Assert.That(response.IsSuccess).IsTrue();
                await Assert.That(response.Id!.OperatorId).IsEqualTo(previous.OperatorId);
            }
        }
        if (change == "unchanged") await Execute();
        else if (change == "failure") await Assert.ThrowsAsync<IOException>(Execute);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(Execute);
        await Assert.That(probe.Captured).IsNotNull();
        await Assert.That(ReferenceEquals(probe.Captured!.OriginalCommand, import ? importCommand : saveCommand)).IsTrue();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            probe.Inner.ExecuteAsync(probe.Captured, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        var row = await observer.SystemSettings.AsNoTracking().SingleAsync(item =>
            item.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        await Assert.That(row.Value).IsEqualTo(change == "unchanged" ? probe.Captured.After.Value : before);
        await Assert.That(row.IsLocked).IsTrue();
        await Assert.That(row.Description).IsEqualTo("Original metadata");
        var persisted = JsonSerializer.Deserialize<InstanceOperatorIdentitySettings>(row.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await Assert.That(persisted.OperatorId).IsEqualTo(previous.OperatorId);
        await Assert.That(persisted.IsOfficialInstance).IsEqualTo(previous.IsOfficialInstance);
        var audits = await observer.OutboxMessages.AsNoTracking()
            .Where(item => item.Id == probe.Captured.AuditId).ToListAsync();
        await Assert.That(audits.Count).IsEqualTo(import && change == "unchanged" ? 1 : 0);
        if (audits.Count == 1)
        {
            var audit = JsonSerializer.Deserialize<OperatorIdentityImportAudit>(audits.Single().Payload!)!;
            await Assert.That(audit.ActorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
            await Assert.That(audit.ContentDigest).IsEqualTo(manifest.ContentDigest);
            await Assert.That(audit.ExpectedRevisionHash).IsEqualTo(importCommand.ExpectedRevisionHash);
            await Assert.That(audits.Single().AggregateId).IsEqualTo(previous.OperatorId!.Value);
        }
        await Assert.That(notifications.Published.Count).IsEqualTo(change == "unchanged" ? 1 : 0);
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task OperatorIdentitySourceReplacementBeforeBeginPreservesTheWinningDocument()
    {
        var beforeOwner = new ConsumerBeforeOwnerTransaction();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: beforeOwner);
        await SeedPlatformAdministratorAsync(fixture);
        var original = CompleteOperatorIdentity();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string winner = JsonSerializer.Serialize(original with { PublicName = "Independent winner" }, options);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.SystemSettings.Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                Value = JsonSerializer.Serialize(original, options),
                ValueType = SettingValueType.Json
            });
            await seed.SaveChangesAsync();
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterOperatorIdentityCustody));
        await using var request = await OperatorIdentityRequestAsync(host.Services, await fixture.SignInAsync(), null);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        beforeOwner.Owner = services.GetRequiredService<ExploreDbContext>();
        beforeOwner.BeforeStart = async token =>
        {
            await using var independent = fixture.Native.CreateDatabase();
            await independent.SystemSettings.Where(row => row.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, winner), token);
        };
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            ActivatorUtilities.CreateInstance<SaveInstanceOperatorIdentityCommandHandler>(services)
                .ExecuteAsync(new() { Request = OperatorDraft() with { ExpectedRevision = original.Revision } }, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(winner);
        await Assert.That(beforeOwner.Observed).IsTrue();
    }

    private static InstanceOperatorIdentitySettings CompleteOperatorIdentity() => new()
    {
        OperatorId = Guid.CreateVersion7(),
        Revision = Guid.CreateVersion7(),
        PublicName = "Original Operator",
        LegalName = "Original Operator",
        OperatorKindCode = "individual",
        JurisdictionCountryCode = "BE",
        PublicContactEmail = "operator@example.org",
        WebsiteUrl = "https://example.org",
        LegalNoticeUrl = "https://example.org/legal",
        TermsUrl = "https://example.org/terms",
        PrivacyUrl = "https://example.org/privacy",
        OfficialOrigin = "https://example.org"
    };

    private sealed class OperatorWriterProbe(OperatorIdentitySettingsWriter inner) : IOperatorIdentitySettingsWriter
    {
        internal OperatorIdentitySettingsWriter Inner => inner;
        internal OperatorIdentityMutation? Captured { get; private set; }
        internal Func<OperatorIdentityMutation, CancellationToken, Task>? AfterWrite { get; set; }
        public DateTime NormalizeEffectTimestamp(DateTime timestamp) => inner.NormalizeEffectTimestamp(timestamp);
        public Task<string?> NormalizeAuditPayloadAsync(OperatorIdentityImportAudit? audit, CancellationToken token) =>
            inner.NormalizeAuditPayloadAsync(audit, token);
        public Task<OperatorIdentitySettingSnapshot?> ObserveAsync(IOperatorIdentityMutationSource source,
            IUnitOfWork owner, CancellationToken token) => inner.ObserveAsync(source, owner, token);
        public async Task ExecuteAsync(OperatorIdentityMutation mutation, CancellationToken token)
        {
            Captured = mutation;
            await inner.ExecuteAsync(mutation, token);
            if (AfterWrite is { } observe) await observe(mutation, token);
        }
    }

    private sealed class OperatorNotifications(LocalIdentityLifecycleHttpFixture fixture)
        : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];
        public async Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (notification.Key != InstanceOperatorIdentitySettingKeys.OperatorIdentity) return;
            await using var observer = fixture.Native.CreateDatabase();
            var stored = await observer.SystemSettings.AsNoTracking().SingleAsync(row =>
                row.SettingKey == notification.Key, cancellationToken);
            await Assert.That(stored.Value).IsEqualTo(notification.NewValue);
            await Assert.That(stored.UpdatedBy ?? stored.CreatedBy).IsEqualTo(notification.ActorUserId);
            Published.Add(notification);
        }
    }
}
