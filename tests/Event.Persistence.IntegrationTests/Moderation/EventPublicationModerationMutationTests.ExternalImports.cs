using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventPublicationModerationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task External_import_qualified_absence_or_unrestricted_adoption_publishes_the_frozen_native_bundle(bool adopted)
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateExternalImportFixtureAsync(privacy, clock, reader);
        var command = await SeedExternalImportAsync(fixture, clock, reader);
        if (adopted) await AdoptAsync(fixture, clock);
        else await QualifyRegistrationPublicationAsync(fixture);
        await RemoveRegistrationPublisherAdminAsync(fixture);

        var response = await ImportExternalAsync(fixture, command);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        var form = await ReadExternalImportAsync(fixture, command);
        var version = form.Versions.Single();
        var revision = await fixture.Context.RegistrationProviderSchemaRevisions.AsNoTracking()
            .SingleAsync(row => row.RegistrationProviderConnectionId == command.ConnectionId);
        await Assert.That(response.Id).IsEqualTo(version.Id);
        await AssertExternalImportPublishedAsync(form, version, revision, command, fixture.UserId, clock.Now.UtcDateTime);
        await Assert.That(reader.LastRequest!.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(reader.LastRequest.Connection.Id).IsEqualTo(command.ConnectionId);
        await Assert.That(reader.LastRequest.Binding.ProviderSurveyId).IsEqualTo("publication-survey");
        await Assert.That(await fixture.Context.RegistrationProviderBindings.AnyAsync()).IsFalse();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task External_import_current_human_sanction_or_protection_denies_before_reading_the_provider(
        bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateExternalImportFixtureAsync(privacy, clock, reader);
        var command = await SeedExternalImportAsync(fixture, clock, reader);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);
        await RemoveRegistrationPublisherAdminAsync(fixture);

        var response = await ImportExternalAsync(fixture, command);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(command.EventId);
        await Assert.That(response.FailureCode).IsEqualTo("registration_provider_external_import_publish_restricted");
        await Assert.That(reader.LastRequest).IsNull();
        await AssertNoExternalImportAsync(fixture, command);
    }

    [Test]
    public async Task External_import_adopted_unknown_authority_denies_before_reading_the_provider()
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateExternalImportFixtureAsync(privacy, clock, reader);
        var command = await SeedExternalImportAsync(fixture, clock, reader);
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();
        await RemoveRegistrationPublisherAdminAsync(fixture);

        var response = await ImportExternalAsync(fixture, command);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(command.EventId);
        await Assert.That(response.FailureCode).IsEqualTo("registration_provider_external_import_publish_authority_unavailable");
        await Assert.That(reader.LastRequest).IsNull();
        await AssertNoExternalImportAsync(fixture, command);
    }

    [Test]
    public async Task External_import_still_requires_native_channel_management_permission_and_preserves_native_failure_identities()
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateExternalImportFixtureAsync(privacy, clock, reader);
        var command = await SeedExternalImportAsync(fixture, clock, reader, grantOwner: false);
        await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);

        await Assert.ThrowsAsync<AuthorizationException>(async () => _ = await ImportExternalAsync(fixture, command));
        await Assert.That(reader.LastRequest).IsNull();
        await AssertNoExternalImportAsync(fixture, command);

        await GrantRegistrationPublisherAsync(fixture, command.EventId);
        var invalid = await ImportExternalAsync(fixture, command with
        {
            Request = command.Request with { ProviderSurveyId = string.Empty }
        });
        await Assert.That(invalid.IsSuccess).IsFalse();
        await Assert.That(invalid.Id).IsEqualTo(Guid.Empty);
        await Assert.That(invalid.FailureCode).IsEqualTo("registration_provider_external_import_validation_failed");
        var missingConnection = Guid.CreateVersion7();
        var disconnected = await ImportExternalAsync(fixture, command with { ConnectionId = missingConnection });
        await Assert.That(disconnected.IsSuccess).IsFalse();
        await Assert.That(disconnected.Id).IsEqualTo(missingConnection);
        await Assert.That(disconnected.FailureCode).IsEqualTo("registration_provider_connection_not_found");
        await Assert.That(reader.LastRequest).IsNull();
        var missingForm = Guid.CreateVersion7();
        var absentForm = await ImportExternalAsync(fixture, command with
        {
            Request = command.Request with { FormId = missingForm }
        });
        await Assert.That(absentForm.IsSuccess).IsFalse();
        await Assert.That(absentForm.Id).IsEqualTo(missingForm);
        await Assert.That(absentForm.FailureCode).IsEqualTo("registration_form_not_found");
        await AssertNoExternalImportAsync(fixture, command);
    }

    [Test]
    public async Task External_import_cannot_rebind_a_foreign_form_or_use_another_tenant_to_resolve_the_original_connection()
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateExternalImportFixtureAsync(privacy, clock, reader);
        var command = await SeedExternalImportAsync(fixture, clock, reader);
        var otherEvent = await ReadyDraftAsync(fixture);
        var otherForm = RegistrationForm.Create(fixture.TenantId, otherEvent.Id, "external", "other-form",
            "Another event's form", clock.Now.UtcDateTime);
        var otherTenantId = Guid.CreateVersion7();
        fixture.Context.Tenants.Add(new Tenant
        {
            Id = otherTenantId, FullName = "Other import authority", Slug = $"other-import-{otherTenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        });
        fixture.Context.RegistrationForms.Add(otherForm);
        await fixture.Context.SaveChangesAsync();
        var originalStamp = otherForm.ConcurrencyStamp;
        fixture.Context.ChangeTracker.Clear();
        await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);

        var wrongForm = await ImportExternalAsync(fixture, command with
        {
            Request = command.Request with { FormId = otherForm.Id }
        });

        await Assert.That(wrongForm.IsSuccess).IsFalse();
        await Assert.That(wrongForm.Id).IsEqualTo(otherForm.Id);
        await Assert.That(wrongForm.FailureCode).IsEqualTo("registration_form_not_found");
        reader.LastRequest = null;
        var wrongTenant = await ImportExternalAsync(fixture, command with { TenantId = otherTenantId });
        await Assert.That(wrongTenant.IsSuccess).IsFalse();
        await Assert.That(wrongTenant.Id).IsEqualTo(command.ConnectionId);
        await Assert.That(wrongTenant.FailureCode).IsEqualTo("registration_provider_connection_not_found");
        await Assert.That(reader.LastRequest).IsNull();
        await AssertNoExternalImportAsync(fixture, command);
        var preserved = await fixture.Services.GetRequiredService<IRegistrationProviderRepository>()
            .GetFormForExternalImportAsync(fixture.TenantId, otherEvent.Id, otherForm.Id, CancellationToken.None);
        await Assert.That(preserved!.EventId).IsEqualTo(otherEvent.Id);
        await Assert.That(preserved.ConcurrencyStamp).IsEqualTo(originalStamp);
        await Assert.That(preserved.Versions).IsEmpty();
    }

    [Test]
    public async Task External_import_connection_author_sanction_does_not_replace_the_unrestricted_current_human()
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateExternalImportFixtureAsync(privacy, clock, reader);
        var command = await SeedExternalImportAsync(fixture, clock, reader);
        var author = NativeUser(Guid.CreateVersion7());
        fixture.Context.Users.Add(author);
        var connection = await fixture.Context.RegistrationProviderConnections.SingleAsync(row => row.Id == command.ConnectionId);
        connection.CreatedBy = author.Id;
        var parent = await fixture.Context.Events.SingleAsync(row => row.Id == command.EventId);
        parent.SubmittedByUserId = author.Id;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, author.Id, clock.Now);
        await RemoveRegistrationPublisherAdminAsync(fixture);

        var response = await ImportExternalAsync(fixture, command);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        var form = await ReadExternalImportAsync(fixture, command);
        var revision = await fixture.Context.RegistrationProviderSchemaRevisions.AsNoTracking()
            .SingleAsync(row => row.RegistrationProviderConnectionId == command.ConnectionId);
        await AssertExternalImportPublishedAsync(form, form.Versions.Single(), revision, command,
            fixture.UserId, clock.Now.UtcDateTime);
        await Assert.That((await fixture.Context.RegistrationProviderConnections.AsNoTracking()
            .SingleAsync(row => row.Id == command.ConnectionId)).CreatedBy).IsEqualTo((Guid?)author.Id);
    }

    [Test]
    public async Task External_import_idempotent_success_is_gated_again_before_the_duplicate_provider_read()
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateExternalImportFixtureAsync(privacy, clock, reader);
        var command = await SeedExternalImportAsync(fixture, clock, reader);
        var adoption = await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var imported = await ImportExternalAsync(fixture, command);
        await Assert.That(imported.IsSuccess).IsTrue().Because(imported.Message);
        var originalForm = await ReadExternalImportAsync(fixture, command);
        var original = RegistrationPublicationSnapshot.From(originalForm.Versions.Single());

        var duplicate = await ImportExternalAsync(fixture, command);

        await Assert.That(duplicate.IsSuccess).IsTrue().Because(duplicate.Message);
        await Assert.That(duplicate.Id).IsEqualTo(imported.Id);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);
        reader.LastRequest = null;
        var blocked = await ImportExternalAsync(fixture, command);
        await Assert.That(blocked.IsSuccess).IsFalse();
        await Assert.That(blocked.Id).IsEqualTo(command.EventId);
        await Assert.That(blocked.FailureCode).IsEqualTo("registration_provider_external_import_publish_restricted");
        await Assert.That(reader.LastRequest).IsNull();
        var preserved = await ReadExternalImportAsync(fixture, command);
        await Assert.That(preserved.Versions).HasSingleItem();
        await Assert.That(RegistrationPublicationSnapshot.From(preserved.Versions.Single())).IsEqualTo(original);
        await Assert.That(await fixture.Context.RegistrationProviderSchemaRevisions
            .CountAsync(row => row.RegistrationProviderConnectionId == command.ConnectionId)).IsEqualTo(1);
    }

    [Test]
    public async Task External_import_blocked_drift_retains_native_evidence_without_publishing_another_version()
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateExternalImportFixtureAsync(privacy, clock, reader);
        var command = await SeedExternalImportAsync(fixture, clock, reader);
        await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var imported = await ImportExternalAsync(fixture, command);
        await Assert.That(imported.IsSuccess).IsTrue().Because(imported.Message);
        var original = RegistrationPublicationSnapshot.From((await ReadExternalImportAsync(fixture, command)).Versions.Single());
        reader.Snapshot = new([.. reader.Snapshot.Fields.Select(field => field.Key == "email" ? field with { Type = "Integer" } : field)]);

        var blocked = await ImportExternalAsync(fixture, command);

        await Assert.That(blocked.IsSuccess).IsFalse();
        await Assert.That(blocked.FailureCode).IsEqualTo("registration_provider_schema_drift_blocked");
        fixture.Context.ChangeTracker.Clear();
        var evidence = await fixture.Context.RegistrationProviderSchemaRevisions.AsNoTracking()
            .SingleAsync(row => row.RegistrationProviderConnectionId == command.ConnectionId
                && row.DriftClassId == (int)RegistrationProviderDriftClassEnum.TypeChanged);
        await Assert.That(blocked.Id).IsEqualTo(evidence.Id);
        using var snapshot = JsonDocument.Parse(evidence.ProviderSnapshotJson);
        await Assert.That(snapshot.RootElement.GetProperty("fields")[0].GetProperty("type").GetString()).IsEqualTo("Integer");
        var retry = await ImportExternalAsync(fixture, command);
        await Assert.That(retry.IsSuccess).IsFalse();
        await Assert.That(retry.Id).IsEqualTo(evidence.Id);
        await Assert.That(retry.FailureCode).IsEqualTo("registration_provider_schema_drift_blocked");
        var preserved = await ReadExternalImportAsync(fixture, command);
        await Assert.That(preserved.Versions).HasSingleItem();
        await Assert.That(RegistrationPublicationSnapshot.From(preserved.Versions.Single())).IsEqualTo(original);
        await Assert.That(await fixture.Context.RegistrationProviderSchemaRevisions
            .CountAsync(row => row.RegistrationProviderConnectionId == command.ConnectionId)).IsEqualTo(2);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task External_import_owning_commit_rolls_back_flushed_form_revision_and_artifacts_when_publication_authority_changes(
        bool colocated, bool unavailable)
    {
        var clock = new PublicationClock();
        var reader = new ExternalImportSchemaReader();
        var checkpoint = new AfterExternalImportFlush(clock, unavailable);
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            ConfigureServices(services, () => privacy!, clock, [checkpoint]);
            services.AddSingleton<IRegistrationProviderDescriptor>(reader);
        });
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var command = await SeedExternalImportAsync(fixture, clock, reader);
        var adoption = await AdoptAsync(fixture, clock);
        var crossing = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, crossing);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        checkpoint.EventId = command.EventId;
        checkpoint.ConnectionId = command.ConnectionId;
        checkpoint.Crossing = crossing;
        checkpoint.Armed = true;
        var publishedAt = clock.Now.UtcDateTime;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => _ = await ImportExternalAsync(fixture, command));

        await Assert.That(checkpoint.Form).IsNotNull();
        await Assert.That(checkpoint.Revision).IsNotNull();
        await AssertExternalImportPublishedAsync(checkpoint.Form!, checkpoint.Form!.Versions.Single(), checkpoint.Revision!,
            command, fixture.UserId, publishedAt);
        await Assert.That(checkpoint.ObservedPrivacyOwner).IsEqualTo((bool?)colocated);
        await AssertNoExternalImportAsync(fixture, command);
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    private static Task<EventVisitorCapabilitySqliteFixture> CreateExternalImportFixtureAsync(
        ModerationNoticePrivacyFixture privacy, PublicationClock clock, ExternalImportSchemaReader reader) =>
        EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            ConfigureServices(services, () => privacy, clock, []);
            services.AddSingleton<IRegistrationProviderDescriptor>(reader);
        });

    private static async Task<ImportExternalRegistrationProviderFormVersionCommand> SeedExternalImportAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock, ExternalImportSchemaReader reader, bool grantOwner = true)
    {
        var parent = await ReadyDraftAsync(fixture);
        if (!grantOwner)
        {
            // The missing event role must not be replaced by the caller's organizer authority.
            var organizer = NativeUser(Guid.CreateVersion7());
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                UserId = organizer.Id, Pii = new ActorPii { DisplayName = "Independent import organizer" },
                CreatedAt = clock.Now.UtcDateTime
            };
            fixture.Context.Users.Add(organizer);
            fixture.Context.Actors.Add(actor);
            parent.ActorId = actor.Id;
            parent.Actor = actor;
            parent.OrganizerActorId = actor.Id;
            parent.OrganizerActor = actor;
        }
        var tuple = reader.Tuple;
        var connection = RegistrationProviderConnection.Create(fixture.TenantId, "Publication schema reader",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            tuple.ProviderCode, tuple.ProviderDeploymentCode, tuple.ApiVersion, tuple.AdapterPolicyVersion,
            tuple.ConformanceEvidenceRevision, "https://provider.example.test/management", "https://provider.example.test",
            "publication-workspace", null, null, clock.Now.UtcDateTime);
        fixture.Context.RegistrationProviderConnections.Add(connection);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        if (grantOwner) await GrantRegistrationPublisherAsync(fixture, parent.Id);
        return new(fixture.TenantId, parent.Id, connection.Id, new ImportExternalRegistrationProviderFormVersionRequestDto
        {
            Namespace = "external", Key = "publication-import", Name = "Imported publication questions",
            LanguageTag = "en", ProviderSurveyId = "publication-survey", ProviderSurveyRevisionId = "survey-revision-1"
        });
    }

    private static Task<BaseCommandResponse<Guid>> ImportExternalAsync(EventVisitorCapabilitySqliteFixture fixture,
        ImportExternalRegistrationProviderFormVersionCommand command) =>
        fixture.ExecuteCommandAsync<ImportExternalRegistrationProviderFormVersionCommand, BaseCommandResponse<Guid>>(command);

    private static async Task<RegistrationForm> ReadExternalImportAsync(EventVisitorCapabilitySqliteFixture fixture,
        ImportExternalRegistrationProviderFormVersionCommand command)
    {
        fixture.Context.ChangeTracker.Clear();
        return await fixture.Services.GetRequiredService<IRegistrationProviderRepository>()
            .GetExternalImportFormAsync(command.TenantId, command.EventId, command.ConnectionId,
                command.Request.ProviderSurveyId, CancellationToken.None)
            ?? throw new InvalidOperationException("The imported form was not persisted.");
    }

    private static async Task AssertNoExternalImportAsync(EventVisitorCapabilitySqliteFixture fixture,
        ImportExternalRegistrationProviderFormVersionCommand command)
    {
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await fixture.Context.RegistrationForms.AnyAsync(row => row.EventId == command.EventId)).IsFalse();
        await Assert.That(await fixture.Context.RegistrationFormVersions.AnyAsync(row => row.EventId == command.EventId)).IsFalse();
        await Assert.That(await fixture.Context.Set<RegistrationFormSection>().AnyAsync(row => row.EventId == command.EventId)).IsFalse();
        await Assert.That(await fixture.Context.Set<RegistrationFormField>().AnyAsync(row => row.EventId == command.EventId)).IsFalse();
        await Assert.That(await fixture.Context.Set<RegistrationFormFieldOption>().AnyAsync(row => row.EventId == command.EventId)).IsFalse();
        await Assert.That(await fixture.Context.RegistrationProviderSchemaRevisions
            .AnyAsync(row => row.RegistrationProviderConnectionId == command.ConnectionId)).IsFalse();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    private static async Task AssertExternalImportPublishedAsync(RegistrationForm form, RegistrationFormVersion version,
        RegistrationProviderSchemaRevision revision, ImportExternalRegistrationProviderFormVersionCommand command,
        Guid humanId, DateTime publishedAt)
    {
        await Assert.That(form.TenantId).IsEqualTo(command.TenantId);
        await Assert.That(form.EventId).IsEqualTo(command.EventId);
        await Assert.That(form.Key).IsEqualTo("publication-import");
        await Assert.That(version.RegistrationFormId).IsEqualTo(form.Id);
        await Assert.That(version.TenantId).IsEqualTo(command.TenantId);
        await Assert.That(version.EventId).IsEqualTo(command.EventId);
        await Assert.That(version.Version).IsEqualTo(1);
        await Assert.That(version.SourceKindId).IsEqualTo((int)RegistrationFormVersionSourceKindEnum.ExternalImported);
        await Assert.That(version.StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Published);
        await Assert.That(version.LanguageTag).IsEqualTo("en");
        await Assert.That(version.PublishedAt).IsEqualTo((DateTime?)publishedAt);
        await Assert.That(version.CreatedBy).IsEqualTo((Guid?)humanId);
        await Assert.That(version.ExternalRegistrationProviderConnectionId).IsEqualTo((Guid?)command.ConnectionId);
        await Assert.That(version.ExternalRegistrationProviderSchemaRevisionId).IsEqualTo((Guid?)revision.Id);
        await Assert.That(version.ExternalProviderSurveyId).IsEqualTo("publication-survey");
        await Assert.That(version.ExternalProviderSurveyRevisionId).IsEqualTo("survey-revision-1");
        await Assert.That(version.SourceTemplateFormId).IsNull();
        await Assert.That(version.SourceTemplateVersionId).IsNull();
        await Assert.That(revision.TenantId).IsEqualTo(command.TenantId);
        await Assert.That(revision.RegistrationProviderConnectionId).IsEqualTo(command.ConnectionId);
        await Assert.That(revision.SchemaAuthorityId).IsEqualTo((int)RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered);
        await Assert.That(revision.DriftClassId).IsEqualTo((int)RegistrationProviderDriftClassEnum.NoDrift);
        await Assert.That(revision.ObservedAt).IsEqualTo(publishedAt);
        await Assert.That(revision.ProviderSurveyId).IsEqualTo("publication-survey");
        await Assert.That(revision.ProviderSurveyRevisionId).IsEqualTo("survey-revision-1");
        await Assert.That(revision.ProviderSnapshotJson).IsEqualTo(ExternalImportSnapshotJson);
        var snapshotHash = SHA256.HashData(Encoding.UTF8.GetBytes(ExternalImportSnapshotJson));
        await Assert.That(revision.ProviderSnapshotSha256Hash).IsEqualTo(Convert.ToHexStringLower(snapshotHash));
        await Assert.That(revision.RevisionHash.Value).IsEqualTo(Convert.ToBase64String(snapshotHash));
        await Assert.That(version.ExternalImportMappingRevisionHash).IsEqualTo(Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(ExternalImportMappingJson))));
        var section = version.Sections.Single();
        await Assert.That(section.Title).IsEqualTo("Imported questions");
        await Assert.That(section.Fields.Count).IsEqualTo(2);
        var email = section.Fields.Single(field => field.Key == "email");
        await Assert.That(email.Namespace).IsEqualTo("external");
        await Assert.That(email.FieldTypeId).IsEqualTo((int)RegistrationFieldTypeEnum.Email);
        await Assert.That(email.IsRequired).IsTrue();
        var meal = section.Fields.Single(field => field.Key == "meal");
        await Assert.That(meal.FieldTypeId).IsEqualTo((int)RegistrationFieldTypeEnum.SingleChoice);
        await Assert.That(meal.IsRequired).IsFalse();
        await Assert.That(meal.Options.Count).IsEqualTo(2);
        await Assert.That(meal.Options.Single(option => option.Ordinal == 1).Key).IsEqualTo("halal");
        await Assert.That(meal.Options.Single(option => option.Ordinal == 2).Key).IsEqualTo("vegetarian");
        using var data = JsonDocument.Parse(version.DataSchemaArtifact!);
        var properties = data.RootElement.GetProperty("properties");
        await Assert.That(properties.EnumerateObject().Count()).IsEqualTo(2);
        await Assert.That(properties.GetProperty("external/email").GetProperty("format").GetString()).IsEqualTo("email");
        await Assert.That(data.RootElement.GetProperty("required").GetArrayLength()).IsEqualTo(1);
        await Assert.That(data.RootElement.GetProperty("required")[0].GetString()).IsEqualTo("external/email");
        using var ui = JsonDocument.Parse(version.UiSchemaArtifact!);
        var fields = ui.RootElement.GetProperty("sections")[0].GetProperty("fields");
        await Assert.That(fields.GetArrayLength()).IsEqualTo(2);
        await Assert.That(fields[0].GetProperty("label").GetString()).IsEqualTo("Email");
        await Assert.That(fields[1].GetProperty("renderer").GetString()).IsEqualTo("SingleChoice");
        await Assert.That(version.LogicSchemaArtifact).IsEqualTo("{\"rules\":[]}");
        await Assert.That(version.MappingArtifact).IsEqualTo("{\"fields\":[],\"options\":[]}");
        var bundle = "{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"versionId\":\"" + version.Id.ToString("D")
            + "\",\"version\":1,\"languageTag\":\"en\",\"data\":" + version.DataSchemaArtifact
            + ",\"ui\":" + version.UiSchemaArtifact + ",\"logic\":" + version.LogicSchemaArtifact
            + ",\"mapping\":" + version.MappingArtifact + "}";
        await Assert.That(version.SchemaHash).IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bundle))));
    }

    private const string ExternalImportSnapshotJson = """{"schema":"islamu.registration-provider-schema-snapshot.v1","fields":[{"key":"email","label":"Email","type":"Email","isRequired":true,"options":[]},{"key":"meal","label":"Meal","type":"SingleChoice","isRequired":false,"options":[{"key":"halal","label":"Halal"},{"key":"vegetarian","label":"Vegetarian"}]}]}""";

    private const string ExternalImportMappingJson = """{"schema":"islamu.external-import-mapping-revision.v1","importerPolicyVersion":"publication-import-v1","providerCode":"PUBLICATION_SCHEMA","providerDeploymentCode":"HOSTED_SAAS","apiVersion":"v1","conformanceEvidenceRevision":"publication-evidence-v1","providerSurveyId":"publication-survey","providerSurveyRevisionId":"survey-revision-1","fields":[{"providerKey":"email","providerType":"Email","isRequired":true,"mappingKey":"external/email","options":[]},{"providerKey":"meal","providerType":"SingleChoice","isRequired":false,"mappingKey":"external/meal","options":[{"providerKey":"halal"},{"providerKey":"vegetarian"}]}]}""";

    private sealed class ExternalImportSchemaReader : IRegistrationProviderDescriptor, IRegistrationProviderSchemaReader
    {
        public RegistrationProviderTuple Tuple { get; } = new("PUBLICATION_SCHEMA", "HOSTED_SAAS", "v1",
            "publication-import-v1", "publication-evidence-v1");
        public RegistrationProviderCapabilitySet ProvenCapabilities => RegistrationProviderCapabilitySet.None with { SchemaRead = true };
        internal RegistrationProviderSchemaReadRequest? LastRequest { get; set; }
        internal RegistrationProviderSchemaSnapshot Snapshot { get; set; } = new([
            new("meal", "Meal", "SingleChoice", false, [new("vegetarian", "Vegetarian"), new("halal", "Halal")]),
            new("email", "Email", "Email", true, [])
        ]);

        public Task<RegistrationProviderSchemaReadResult> ReadSchemaAsync(RegistrationProviderSchemaReadRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new RegistrationProviderSchemaReadResult(Snapshot, Fingerprint: "survey-revision-1"));
        }
    }

    private sealed class AfterExternalImportFlush(PublicationClock clock, bool unavailable) : SaveChangesInterceptor
    {
        internal Guid EventId { get; set; }
        internal Guid ConnectionId { get; set; }
        internal DateTimeOffset Crossing { get; set; }
        internal bool Armed { get; set; }
        internal RegistrationForm? Form { get; private set; }
        internal RegistrationProviderSchemaRevision? Revision { get; private set; }
        internal bool? ObservedPrivacyOwner { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            var form = await context.RegistrationForms.AsNoTracking()
                .Include(row => row.Versions).ThenInclude(version => version.Sections)
                .ThenInclude(section => section.Fields).ThenInclude(field => field.Options)
                .SingleOrDefaultAsync(row => row.EventId == EventId && row.Versions.Any(version =>
                    version.ExternalRegistrationProviderConnectionId == ConnectionId
                    && version.StatusId == (int)RegistrationFormStatusEnum.Published), cancellationToken);
            if (form is null) return result;
            Armed = false;
            Form = form;
            Revision = await context.RegistrationProviderSchemaRevisions.AsNoTracking()
                .SingleAsync(row => row.RegistrationProviderConnectionId == ConnectionId, cancellationToken);
            ObservedPrivacyOwner = context.IdentityFenceOwnsTransaction;
            if (unavailable)
                await context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
            else
                clock.Now = Crossing;
            return result;
        }
    }
}
