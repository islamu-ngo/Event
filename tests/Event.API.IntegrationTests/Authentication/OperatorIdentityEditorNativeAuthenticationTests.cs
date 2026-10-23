using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class OperatorIdentityEditorNativeAuthenticationTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(true, "unchanged")]
    [Arguments(false, "new")]
    [Arguments(true, "new")]
    [Arguments(false, "binding-revoked")]
    [Arguments(true, "administrator-revoked")]
    [Arguments(false, "terminal-expiry")]
    [Arguments(true, "terminal-expiry")]
    [Arguments(false, "failure")]
    [Arguments(true, "failure")]
    public async Task OriginalSignedGlobalOwnerRetainsRepresentableEffectAndFullPrecisionDeadline(bool import, string change)
    {
        await using var fixture = await OperatorIdentityEditorNativeFixture.CreateAsync(postgres);
        var original = CompleteIdentity();
        bool existing = change != "new";
        bool succeeds = change is "unchanged" or "new";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string before = JsonSerializer.Serialize(original, options);
        DateTime expectedTime = fixture.Clock.GetUtcNow().UtcDateTime.AddTicks(-7);
        await Assert.That(fixture.Clock.GetUtcNow().Ticks % 10).IsEqualTo(7L);
        Guid id = Guid.CreateVersion7();
        await using (var seed = postgres.CreateDbContext())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity).ExecuteDeleteAsync();
            if (existing)
                seed.SystemSettings.Add(new()
                {
                    Id = id,
                    SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                    Value = before,
                    ValueType = SettingValueType.Json,
                    IsLocked = true,
                    Category = "OperatorIdentity",
                    Description = "Original metadata",
                    DisplayOrder = 17,
                    CreatedAt = expectedTime.AddMinutes(-1),
                    CreatedBy = fixture.Actor
                });
            await seed.SaveChangesAsync();
        }
        fixture.Writer.AfterObservation = async token =>
        {
            await using var independent = postgres.CreateDbContext();
            if (change == "binding-revoked")
                await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding).ExecuteDeleteAsync(token);
            if (change == "administrator-revoked")
                await independent.PlatformUserRoles.Where(row => row.Id == fixture.Grant).ExecuteDeleteAsync(token);
        };
        fixture.Writer.AfterWrite = async (mutation, token) =>
        {
            await Assert.That(mutation.Actor).IsEqualTo((Guid?)fixture.Actor);
            await Assert.That(existing ? mutation.After.UpdatedAt : mutation.After.CreatedAt).IsEqualTo((DateTime?)expectedTime);
            await Assert.That(mutation.OccurredAt).IsEqualTo(expectedTime);
            await Assert.That(mutation.Source.IsHuman).IsTrue();
            await Assert.That(mutation.Source.IsSetup).IsFalse();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Writer.Inner.ExecuteAsync(mutation, token));
            if (change == "terminal-expiry")
                fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    // One .NET tick after the genuine retained deadline, not a fabricated lifetime
                    // and not the normalized persisted-effect clock quantum.
                    fixture.Clock.Set(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1));
                    return Task.CompletedTask;
                }, _ => { });
            if (change == "failure") throw new IOException("Reject after the original native provisional effects.");
        };
        var changed = original with { LegalName = "Replacement operator" };
        var manifest = OperatorIdentityManifestJson.Create(JsonSerializer.SerializeToElement(changed, options));
        var importCommand = new ImportInstanceOperatorIdentityCommand(manifest,
            OperatorIdentityManifestJson.RevisionHash(existing ? original.Revision : null));
        var saveCommand = new SaveInstanceOperatorIdentityCommand
        {
            Request = new SaveInstanceOperatorIdentityRequestDto
            {
                ExpectedRevision = existing ? original.Revision : null,
                PublicName = changed.PublicName,
                LegalName = changed.LegalName,
                OperatorKindCode = changed.OperatorKindCode,
                JurisdictionCountryCode = changed.JurisdictionCountryCode,
                PublicContactEmail = changed.PublicContactEmail,
                WebsiteUrl = changed.WebsiteUrl,
                LegalNoticeUrl = changed.LegalNoticeUrl,
                TermsUrl = changed.TermsUrl,
                PrivacyUrl = changed.PrivacyUrl,
                OfficialOrigin = changed.OfficialOrigin
            }
        };
        Task Execute() => fixture.InRequestAsync(async () =>
        {
            if (import)
                await Assert.That((await ActivatorUtilities.CreateInstance<ImportInstanceOperatorIdentityCommandHandler>(fixture.Services)
                    .ExecuteAsync(importCommand, default)).IsSuccess).IsTrue();
            else
                await Assert.That((await ActivatorUtilities.CreateInstance<SaveInstanceOperatorIdentityCommandHandler>(fixture.Services)
                    .ExecuteAsync(saveCommand, default)).IsSuccess).IsTrue();
        });
        if (succeeds) await Execute();
        else if (change == "failure") await Assert.ThrowsAsync<IOException>(Execute);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(Execute);
        await using var observer = postgres.CreateDbContext();
        var row = await observer.SystemSettings.AsNoTracking().SingleAsync(setting =>
            setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        var saved = JsonSerializer.Deserialize<InstanceOperatorIdentitySettings>(row.Value, options)!;
        var audits = await observer.OutboxMessages.AsNoTracking()
            .Where(audit => audit.AggregateId == saved.OperatorId).ToListAsync();
        await Assert.That(row.Value).IsEqualTo(succeeds ? fixture.Writer.Mutation!.After.Value : before);
        await Assert.That(row.IsLocked).IsEqualTo(existing);
        await Assert.That(row.Description).IsEqualTo(existing ? "Original metadata" : null);
        await Assert.That(row.DisplayOrder).IsEqualTo(existing ? 17 : 0);
        await Assert.That(row.CreatedBy).IsEqualTo((Guid?)fixture.Actor);
        await Assert.That(row.CreatedAt).IsEqualTo(existing ? expectedTime.AddMinutes(-1) : expectedTime);
        await Assert.That(row.UpdatedAt).IsEqualTo(succeeds && existing ? (DateTime?)expectedTime : null);
        await Assert.That(audits.Count).IsEqualTo(import && succeeds ? 1 : 0);
        if (audits.Count == 1)
        {
            await Assert.That(audits[0].Payload).IsEqualTo(fixture.Writer.Mutation!.AuditPayload);
            await Assert.That(audits[0].CreatedAt).IsEqualTo(expectedTime);
            var audit = JsonSerializer.Deserialize<OperatorIdentityImportAudit>(audits[0].Payload!)!;
            await Assert.That(audit.ActorUserId).IsEqualTo(fixture.Actor);
            await Assert.That(audit.ContentDigest).IsEqualTo(manifest.ContentDigest);
            await Assert.That(audit.ExpectedRevisionHash).IsEqualTo(importCommand.ExpectedRevisionHash);
            await Assert.That(audit.CommittedRevision).IsEqualTo(fixture.Writer.Mutation.Audit!.CommittedRevision);
        }
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
        if (fixture.Writer.Mutation is { } settled)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Writer.Inner.ExecuteAsync(settled, default));
    }

    [Test]
    public async Task OriginalSignedRequestCannotReplaceItsPhysicalUnitOfWork()
    {
        await using var fixture = await OperatorIdentityEditorNativeFixture.CreateAsync(postgres);
        await fixture.InRequestAsync(async () =>
        {
            var command = new SaveInstanceOperatorIdentityCommand { Request = new() };
            var source = await fixture.Services.GetRequiredService<IOperatorIdentityMutationAuthority>().CaptureAsync(command, default);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Writer.ObserveAsync(
                source, new EfCoreUnitOfWork(fixture.Owner), default));
        });
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
    }

    private static InstanceOperatorIdentitySettings CompleteIdentity() => new()
    {
        OperatorId = Guid.CreateVersion7(),
        Revision = Guid.CreateVersion7(),
        PublicName = "Original operator",
        LegalName = "Original operator",
        OperatorKindCode = "individual",
        JurisdictionCountryCode = "BE",
        PublicContactEmail = "operator@example.org",
        WebsiteUrl = "https://example.org",
        LegalNoticeUrl = "https://example.org/legal",
        TermsUrl = "https://example.org/terms",
        PrivacyUrl = "https://example.org/privacy",
        OfficialOrigin = "https://example.org"
    };
}
