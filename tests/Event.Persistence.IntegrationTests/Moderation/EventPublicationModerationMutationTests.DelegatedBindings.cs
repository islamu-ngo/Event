using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services.Registration.Providers.MicrosoftForms;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventPublicationModerationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Delegated_binding_qualified_absence_or_unrestricted_adoption_publishes_and_pins_native_mappings(bool adopted)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedDelegatedBindingAsync(fixture, clock);
        if (adopted) await AdoptAsync(fixture, clock);
        else await QualifyRegistrationPublicationAsync(fixture);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);

        var response = await PublishDelegatedBindingAsync(fixture, draft, clock);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await Assert.That(response.Id).IsEqualTo(draft.BindingId);
        var published = await DelegatedBindingSnapshotAsync(fixture, draft);
        await AssertDelegatedBindingPublishedAsync(published, original, clock.Now.UtcDateTime);
        await Assert.That(published.UpdatedBy).IsEqualTo((Guid?)fixture.UserId);
        var replacement = await fixture.ExecuteCommandAsync<ReplaceDraftRegistrationProviderMappingsCommand, BaseCommandResponse<Guid>>(
            new(fixture.TenantId, draft.BindingId, [new("platform.registration.email", "different-question", true)], []));
        await Assert.That(replacement.IsSuccess).IsFalse();
        await Assert.That(replacement.FailureCode).IsEqualTo("registration_provider_binding_not_draft");
        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, draft)).IsEqualTo(published);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Delegated_binding_current_human_tenant_or_instance_sanction_or_protection_precedes_native_readiness(
        bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedDelegatedBindingAsync(fixture, clock, callback: false);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);

        var response = await PublishDelegatedBindingAsync(fixture, draft, clock);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(draft.BindingId);
        await Assert.That(response.FailureCode).IsEqualTo("registration_provider_binding_publish_restricted");
        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, draft)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Delegated_binding_unknown_authority_denies_both_native_entrypoints_without_publishing()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedDelegatedBindingAsync(fixture, clock, callback: false);
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);

        foreach (var direct in new[] { false, true })
        {
            var response = await PublishDelegatedBindingAsync(fixture, draft, clock, direct);
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Id).IsEqualTo(draft.BindingId);
            await Assert.That(response.FailureCode).IsEqualTo("registration_provider_binding_publish_authority_unavailable");
            await Assert.That(await DelegatedBindingSnapshotAsync(fixture, draft)).IsEqualTo(original);
            await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        }
    }

    [Test]
    public async Task Delegated_binding_native_low_writer_is_not_an_alias_around_current_human_restrictions()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedDelegatedBindingAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);

        var response = await PublishDelegatedBindingAsync(fixture, draft, clock, direct: true);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(draft.BindingId);
        await Assert.That(response.FailureCode).IsEqualTo("registration_provider_binding_publish_restricted");
        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, draft)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Delegated_binding_author_and_connection_owner_restrictions_do_not_replace_the_current_human_publisher()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedDelegatedBindingAsync(fixture, clock);
        var author = NativeUser(Guid.CreateVersion7());
        fixture.Context.Users.Add(author);
        var binding = await fixture.Context.RegistrationProviderBindings.SingleAsync(row => row.Id == draft.BindingId);
        binding.CreatedBy = author.Id;
        var connection = await fixture.Context.RegistrationProviderConnections.SingleAsync(row => row.Id == draft.ConnectionId);
        connection.CreatedBy = author.Id;
        var version = await fixture.Context.RegistrationFormVersions.SingleAsync(row => row.Id == draft.VersionId);
        version.CreatedBy = author.Id;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, author.Id, clock.Now);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);

        var response = await PublishDelegatedBindingAsync(fixture, draft, clock);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        var published = await DelegatedBindingSnapshotAsync(fixture, draft);
        await AssertDelegatedBindingPublishedAsync(published, original, clock.Now.UtcDateTime);
        await Assert.That(published.UpdatedBy).IsEqualTo((Guid?)fixture.UserId);
        await Assert.That((await fixture.Context.RegistrationProviderBindings.AsNoTracking()
            .SingleAsync(row => row.Id == draft.BindingId)).CreatedBy).IsEqualTo((Guid?)author.Id);
    }

    [Test]
    public async Task Delegated_binding_unrestricted_moderation_does_not_grant_native_channel_management_permission()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedDelegatedBindingAsync(fixture, clock, grantOwner: false);
        await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);

        await Assert.ThrowsAsync<AuthorizationException>(async () =>
            _ = await PublishDelegatedBindingAsync(fixture, draft, clock));

        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, draft)).IsEqualTo(original);
        await GrantRegistrationPublisherAsync(fixture, draft.EventId);
        var permitted = await PublishDelegatedBindingAsync(fixture, draft, clock);
        await Assert.That(permitted.IsSuccess).IsTrue().Because(permitted.Message);
        await AssertDelegatedBindingPublishedAsync(await DelegatedBindingSnapshotAsync(fixture, draft), original, clock.Now.UtcDateTime);
    }

    [Test]
    public async Task Delegated_binding_high_level_readiness_and_low_level_drift_and_publication_time_keep_their_distinct_contracts()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var draft = await SeedDelegatedBindingAsync(fixture, clock, callback: false);
        await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);

        var high = await PublishDelegatedBindingAsync(fixture, draft, clock);

        await Assert.That(high.IsSuccess).IsFalse();
        await Assert.That(high.Id).IsEqualTo(draft.BindingId);
        await Assert.That(high.FailureCode).IsEqualTo("registration_provider_test_callback_required");
        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, draft)).IsEqualTo(original);
        var suppliedPublicationTime = clock.Now.AddMinutes(-2).UtcDateTime;
        var drift = await fixture.ExecuteCommandAsync<PublishRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>(
            new(fixture.TenantId, draft.EventId, draft.BindingId, RegistrationProviderSchemaDriftClass.TypeChanged, suppliedPublicationTime));
        await Assert.That(drift.IsSuccess).IsFalse();
        await Assert.That(drift.Id).IsEqualTo(draft.BindingId);
        await Assert.That(drift.FailureCode).IsEqualTo("registration_provider_drift_blocks_publication");
        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, draft)).IsEqualTo(original);
        var low = await fixture.ExecuteCommandAsync<PublishRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>(
            new(fixture.TenantId, draft.EventId, draft.BindingId, RegistrationProviderSchemaDriftClass.NoDrift, suppliedPublicationTime));
        await Assert.That(low.IsSuccess).IsTrue().Because(low.Message);
        await AssertDelegatedBindingPublishedAsync(await DelegatedBindingSnapshotAsync(fixture, draft), original, suppliedPublicationTime);
        await Assert.That(await fixture.Services.GetRequiredService<IRegistrationProviderRepository>()
            .GetLastCallbackAtAsync(fixture.TenantId, draft.BindingId, CancellationToken.None)).IsNull();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Delegated_binding_low_writer_hash_is_independent_of_persisted_field_and_option_insertion_order()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var first = await SeedDelegatedBindingAsync(fixture, clock);
        var tuple = fixture.Services.GetServices<IRegistrationProviderDescriptor>()
            .OfType<MicrosoftFormsRegistrationProviderDescriptor>().Single().Tuple;
        var secondConnection = RegistrationProviderConnection.Create(fixture.TenantId, "Reversed mapping publication forms",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            tuple.ProviderCode, tuple.ProviderDeploymentCode, tuple.ApiVersion, tuple.AdapterPolicyVersion,
            tuple.ConformanceEvidenceRevision, "https://forms.office.com", "https://forms.office.com/Pages/ResponsePage.aspx",
            "reversed-publication-workspace", null, null, clock.Now.UtcDateTime);
        fixture.Context.RegistrationProviderConnections.Add(secondConnection);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var second = await AddDelegatedBindingAsync(fixture, clock, first.EventId, first.FormId,
            first.VersionId, secondConnection.Id, callback: true, reverseMappings: true);
        await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var firstOriginal = await DelegatedBindingSnapshotAsync(fixture, first);
        var secondOriginal = await DelegatedBindingSnapshotAsync(fixture, second);

        var firstResponse = await PublishDelegatedBindingAsync(fixture, first, clock, direct: true);
        var secondResponse = await PublishDelegatedBindingAsync(fixture, second, clock, direct: true);

        await Assert.That(firstResponse.IsSuccess).IsTrue().Because(firstResponse.Message);
        await Assert.That(secondResponse.IsSuccess).IsTrue().Because(secondResponse.Message);
        var firstPublished = await DelegatedBindingSnapshotAsync(fixture, first);
        var secondPublished = await DelegatedBindingSnapshotAsync(fixture, second);
        await AssertDelegatedBindingPublishedAsync(firstPublished, firstOriginal, clock.Now.UtcDateTime);
        await AssertDelegatedBindingPublishedAsync(secondPublished, secondOriginal, clock.Now.UtcDateTime);
        await Assert.That(firstPublished.Hash).IsEqualTo(secondPublished.Hash);
        foreach (var draft in new[] { first, second })
        {
            var rejected = await fixture.ExecuteCommandAsync<ReplaceDraftRegistrationProviderMappingsCommand, BaseCommandResponse<Guid>>(
                new(fixture.TenantId, draft.BindingId, [new("platform.registration.email", "replacement", true)], []));
            await Assert.That(rejected.IsSuccess).IsFalse();
            await Assert.That(rejected.FailureCode).IsEqualTo("registration_provider_binding_not_draft");
        }
        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, first)).IsEqualTo(firstPublished);
        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, second)).IsEqualTo(secondPublished);
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    public async Task Delegated_binding_one_native_transaction_rolls_back_flushed_hash_state_and_mappings_if_commit_authority_changes(
        bool direct, bool colocated, bool unavailable)
    {
        var clock = new PublicationClock();
        var transactions = new DelegatedBindingTransactions();
        var callbackReads = new DelegatedBindingCallbackReads();
        var checkpoint = new AfterDelegatedBindingFlush(clock, unavailable);
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            ConfigureServices(services, () => privacy!, clock, [transactions, callbackReads, checkpoint]));
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var draft = await SeedDelegatedBindingAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        var crossing = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, crossing);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);
        checkpoint.Draft = draft;
        checkpoint.Crossing = crossing;
        checkpoint.Armed = true;
        transactions.Armed = true;
        callbackReads.Armed = true;
        var publishedAt = clock.Now.UtcDateTime;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await PublishDelegatedBindingAsync(fixture, draft, clock, direct));

        transactions.Armed = false;
        callbackReads.Armed = false;
        await Assert.That(checkpoint.Observed).IsNotNull();
        await AssertDelegatedBindingPublishedAsync(checkpoint.Observed!, original, publishedAt);
        await Assert.That(checkpoint.ObservedPrivacyOwner).IsEqualTo((bool?)colocated);
        await Assert.That(transactions.Started.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(transactions.Started.Single(), checkpoint.Transaction)).IsTrue();
        if (direct)
            await Assert.That(callbackReads.Transactions.Count).IsEqualTo(0);
        else
        {
            await Assert.That(callbackReads.Transactions.Count).IsEqualTo(1);
            await Assert.That(ReferenceEquals(callbackReads.Transactions.Single(), checkpoint.Transaction)).IsTrue();
        }
        await Assert.That(await DelegatedBindingSnapshotAsync(fixture, draft)).IsEqualTo(original);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Delegated_binding_original_connection_tuple_retarget_at_native_fence_is_rejected_and_rolled_back(bool direct)
    {
        var clock = new PublicationClock();
        var retarget = new RetargetDelegatedBindingConnection();
        var callbackReads = new DelegatedBindingCallbackReads();
        var flush = new AfterDelegatedBindingFlush(clock, unavailable: false);
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock, retarget, callbackReads, flush);
        var draft = await SeedDelegatedBindingAsync(fixture, clock);
        await AdoptAsync(fixture, clock);
        await RemoveRegistrationPublisherAdminAsync(fixture);
        var original = await DelegatedBindingSnapshotAsync(fixture, draft);
        var connection = await fixture.Services.GetRequiredService<IRegistrationProviderRepository>()
            .GetConnectionAsync(fixture.TenantId, draft.ConnectionId, CancellationToken.None)
            ?? throw new InvalidOperationException("The original connection was not persisted.");
        var originalTuple = new RegistrationProviderTuple(connection.ProviderCode, connection.ProviderDeploymentCode,
            connection.ApiVersion, connection.AdapterPolicyVersion, connection.ConformanceEvidenceRevision);
        fixture.Context.ChangeTracker.Clear();
        retarget.TenantId = fixture.TenantId;
        retarget.ConnectionId = draft.ConnectionId;
        retarget.Armed = true;
        callbackReads.Armed = true;
        flush.Draft = draft;
        flush.Crossing = clock.Now;
        flush.Armed = true;
        ConcurrencyConflictException? rejected = null;

        try
        {
            _ = await PublishDelegatedBindingAsync(fixture, draft, clock, direct);
        }
        catch (ConcurrencyConflictException exception)
        {
            rejected = exception;
        }

        retarget.Armed = false;
        callbackReads.Armed = false;
        await Assert.That(retarget.ObservedTuple).IsNotNull()
            .Because("Publication must execute the original connection's native row fence before reloading its selected tuple.");
        await Assert.That(retarget.ChangedRows).IsEqualTo(1);
        await Assert.That(retarget.Transaction).IsNotNull();
        await Assert.That(retarget.ObservedTuple).IsEqualTo(originalTuple with
        {
            ConformanceEvidenceRevision = RetargetDelegatedBindingConnection.UnregisteredRevision
        });
        await Assert.That(fixture.Services.GetRequiredService<IRegistrationProviderRegistry>()
            .TryResolve(retarget.ObservedTuple!)).IsNull();
        await Assert.That(rejected).IsNotNull();
        await Assert.That(callbackReads.Transactions.Count).IsEqualTo(0);
        await Assert.That(flush.Observed).IsNull();
        var preserved = await DelegatedBindingSnapshotAsync(fixture, draft);
        await Assert.That(preserved).IsEqualTo(original);
        await Assert.That(preserved.State).IsEqualTo((int)RegistrationProviderBindingStateEnum.Draft);
        await Assert.That(preserved.Hash).IsNull();
        await Assert.That(preserved.HashKey).IsEqualTo(string.Empty);
        await Assert.That(preserved.PublishedAt).IsNull();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    private static async Task<DelegatedBindingDraft> SeedDelegatedBindingAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock, bool callback = true, bool grantOwner = true)
    {
        var draft = await SeedRegistrationPublicationAsync(fixture, clock, grantOwner: grantOwner);
        var version = await fixture.Services.GetRequiredService<IRegistrationFormAuthoringRepository>()
            .GetVersionForUpdateAsync(draft.EventId, draft.RegistrationFormId, draft.Id, CancellationToken.None)
            ?? throw new InvalidOperationException("The native registration form version was not persisted.");
        var section = version.Sections.Single();
        version.UpdateFieldValidation(section.Fields.Single(), true, false, null, null, null, null, null, null, null, null);
        var meal = RegistrationFormField.Create(Guid.CreateVersion7(), section, 2, "platform.registration", "meal",
            "Meal", RegistrationFieldTypeEnum.SingleChoice, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, clock.Now.UtcDateTime);
        version.AddField(section, meal);
        version.AddOption(meal, RegistrationFormFieldOption.Create(Guid.CreateVersion7(), meal, 1, "halal", "Halal", clock.Now.UtcDateTime));
        version.AddOption(meal, RegistrationFormFieldOption.Create(Guid.CreateVersion7(), meal, 2, "vegetarian", "Vegetarian", clock.Now.UtcDateTime));
        fixture.Services.GetRequiredService<FormSchemaArtifactPublicationService>().Publish(version, clock.Now.UtcDateTime);
        var descriptor = fixture.Services.GetServices<IRegistrationProviderDescriptor>()
            .OfType<MicrosoftFormsRegistrationProviderDescriptor>().Single();
        await Assert.That(descriptor.ProvenCapabilities.FormProvision).IsFalse();
        await Assert.That(descriptor.ProvenCapabilities.SubscriptionManagement).IsFalse();
        await Assert.That(ReferenceEquals(fixture.Services.GetRequiredService<IRegistrationProviderRegistry>()
            .TryResolve(descriptor.Tuple), descriptor)).IsTrue();
        await Assert.That(fixture.Services.GetRequiredService<IRegistrationProviderManagedPublishPreflight>().GetType())
            .IsEqualTo(typeof(RegistrationProviderManagedPublishPreflightService));
        var tuple = descriptor.Tuple;
        var connection = RegistrationProviderConnection.Create(fixture.TenantId, "Delegated publication forms",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            tuple.ProviderCode, tuple.ProviderDeploymentCode, tuple.ApiVersion, tuple.AdapterPolicyVersion,
            tuple.ConformanceEvidenceRevision, "https://forms.office.com", "https://forms.office.com/Pages/ResponsePage.aspx",
            "publication-workspace", null, null, clock.Now.UtcDateTime);
        fixture.Context.RegistrationProviderConnections.Add(connection);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return await AddDelegatedBindingAsync(fixture, clock, version.EventId, version.RegistrationFormId,
            version.Id, connection.Id, callback, reverseMappings: false);
    }

    private static async Task<DelegatedBindingDraft> AddDelegatedBindingAsync(EventVisitorCapabilitySqliteFixture fixture,
        PublicationClock clock, Guid eventId, Guid formId, Guid versionId, Guid connectionId, bool callback, bool reverseMappings)
    {
        var binding = RegistrationProviderBinding.Create(fixture.TenantId, connectionId, formId, versionId,
            RegistrationProviderPresentationModeEnum.Redirect, RegistrationProviderCollectionModeEnum.ProviderHosted,
            RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.CompletionOnly,
            null, clock.Now.UtcDateTime);
        binding.CreatedBy = fixture.UserId;
        binding.SetDraftProvisionedSurvey("delegated-publication-survey", "revision-1");
        // Metadata only: reuse the existing webhook fixture reference; this local preflight never fetches a secret value.
        var secret = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret,
            SecretScope.Tenant, fixture.TenantId, "WEBHOOK_SECRET", qualifier: binding.Id.ToString("N"));
        fixture.Context.SecretBindings.Add(secret);
        await fixture.Context.SaveChangesAsync();
        binding.SetDraftProvisionedSubscription(MicrosoftFormsRegistrationProviderDescriptor.ContractVersion, secret.Id);
        RegistrationProviderFieldMappingInput[] fieldInputs =
        [
            new("platform.registration.email", "question-email", true),
            new("platform.registration.meal", "question-meal", false),
            new(MicrosoftFormsRegistrationProviderDescriptor.CorrelationPlatformFieldKey, "question-attempt", true)
        ];
        foreach (var field in reverseMappings ? fieldInputs.Reverse() : fieldInputs)
            binding.AddFieldMapping(RegistrationProviderFieldMapping.Create(binding, field.PlatformFieldKey, field.ProviderFieldKey, field.IsRequired));
        var meal = binding.FieldMappings.Single(row => row.PlatformFieldKey == "platform.registration.meal");
        RegistrationProviderOptionMappingInput[] optionInputs =
        [
            new("platform.registration.meal", "halal", "Halal"),
            new("platform.registration.meal", "vegetarian", "Vegetarian")
        ];
        foreach (var option in reverseMappings ? optionInputs.Reverse() : optionInputs)
            binding.AddOptionMapping(RegistrationProviderOptionMapping.Create(binding, meal, option.PlatformOptionKey, option.ProviderOptionKey));
        fixture.Context.RegistrationProviderBindings.Add(binding);
        if (callback) SeedDelegatedTestCallback(fixture.Context, fixture.TenantId, binding.Id, clock.Now.UtcDateTime);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new(eventId, formId, versionId, connectionId, binding.Id);
    }

    private static void SeedDelegatedTestCallback(ExploreDbContext context, Guid tenantId, Guid bindingId, DateTime now)
    {
        // Seed retained completed-callback evidence, not an HTTP replay or a claim that the connector was contacted.
        byte[] payload = Encoding.UTF8.GetBytes("{\"testCallback\":true}");
        var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payload));
        var decisionId = $"{bindingId:N}:publication-test-callback";
        var message = IncomingWebhookMessage.CreateVerified(tenantId, "registration-provider", decisionId, decisionId,
            "registration.provider_submission", payload, hash, "application/json", "utf-8", "{}", now, now,
            now.AddDays(1), "test", now.AddDays(1), now.AddDays(1), now.AddDays(1), now.AddDays(1));
        var effect = IncomingWebhookEffectOutbox.CreatePending(tenantId, message.Id, "registration-provider", decisionId,
            "registration.provider_submission", hash, now);
        var lease = Guid.CreateVersion7();
        effect.Claim("publication-test-callback", lease, now.AddMinutes(1), now);
        effect.Complete(lease, effect.ProcessingFence, effect.ProcessingGeneration, now);
        context.AddRange(message, effect);
    }

    private static Task<BaseCommandResponse<Guid>> PublishDelegatedBindingAsync(EventVisitorCapabilitySqliteFixture fixture,
        DelegatedBindingDraft draft, PublicationClock clock, bool direct = false) => direct
        ? fixture.ExecuteCommandAsync<PublishRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>(
            new(fixture.TenantId, draft.EventId, draft.BindingId, RegistrationProviderSchemaDriftClass.NoDrift, clock.Now.UtcDateTime))
        : fixture.ExecuteCommandAsync<PublishEventRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>(
            new(fixture.TenantId, draft.EventId, draft.BindingId));

    private static async Task<DelegatedBindingPublicationSnapshot> DelegatedBindingSnapshotAsync(
        EventVisitorCapabilitySqliteFixture fixture, DelegatedBindingDraft draft)
    {
        fixture.Context.ChangeTracker.Clear();
        var binding = await fixture.Services.GetRequiredService<IRegistrationProviderRepository>()
            .GetBindingAsync(fixture.TenantId, draft.BindingId, CancellationToken.None)
            ?? throw new InvalidOperationException("The native provider binding was not persisted.");
        var form = await RegistrationSnapshotAsync(fixture, draft.VersionId);
        return DelegatedBindingPublicationSnapshot.From(binding, form);
    }

    private static async Task AssertDelegatedBindingPublishedAsync(DelegatedBindingPublicationSnapshot actual,
        DelegatedBindingPublicationSnapshot original, DateTime publishedAt)
    {
        await Assert.That(actual.Id).IsEqualTo(original.Id);
        await Assert.That(actual.State).IsEqualTo((int)RegistrationProviderBindingStateEnum.Published);
        await Assert.That(actual.Drift).IsEqualTo((int)RegistrationProviderDriftClassEnum.NoDrift);
        await Assert.That(actual.Hash).IsNotNull();
        await Assert.That(Convert.FromBase64String(actual.Hash!).Length).IsEqualTo(32);
        await Assert.That(actual.HashKey).IsEqualTo(actual.Hash);
        await Assert.That(actual.PublishedAt).IsEqualTo((DateTime?)publishedAt);
        await Assert.That(actual.Stamp).IsNotEqualTo(original.Stamp);
        await Assert.That(actual.Configuration).IsEqualTo(original.Configuration);
        await Assert.That(actual.Fields).IsEqualTo(original.Fields);
        await Assert.That(actual.Options).IsEqualTo(original.Options);
        await Assert.That(actual.Form).IsEqualTo(original.Form);
    }

    private sealed record DelegatedBindingDraft(Guid EventId, Guid FormId, Guid VersionId, Guid ConnectionId, Guid BindingId);

    private sealed record DelegatedBindingPublicationSnapshot(Guid Id, int State, int Drift, Guid Stamp, string? Hash,
        string HashKey, DateTime? PublishedAt, Guid? UpdatedBy, DateTime? UpdatedAt, string Configuration,
        string Fields, string Options, RegistrationPublicationSnapshot Form)
    {
        internal static DelegatedBindingPublicationSnapshot From(RegistrationProviderBinding binding, RegistrationPublicationSnapshot form) =>
            new(binding.Id, binding.StateId, binding.DriftClassId, binding.ConcurrencyStamp,
                binding.PublishedMappingRevisionHash?.Value, binding.PublishedMappingRevisionHashKey, binding.PublishedAt,
                binding.UpdatedBy, binding.UpdatedAt,
                JsonSerializer.Serialize(new
                {
                    binding.TenantId, binding.RegistrationProviderConnectionId, binding.RegistrationFormId,
                    binding.RegistrationFormVersionId, binding.ProviderSurveyId, binding.ProviderSurveyRevisionId,
                    binding.ProviderWebhookId, binding.WebhookSecretBindingId, binding.PresentationModeId,
                    binding.CollectionModeId, binding.CompletionModeId, binding.TrustLevelId, binding.CreatedBy,
                    binding.IsDeleted, Tuple = binding.Connection!.TupleKey
                }),
                JsonSerializer.Serialize(binding.FieldMappings.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, row.TenantId, row.RegistrationProviderBindingId, row.PlatformFieldKey,
                    row.ProviderFieldKey, row.IsRequired, row.IsDeleted
                })),
                JsonSerializer.Serialize(binding.OptionMappings.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, row.TenantId, row.RegistrationProviderBindingId, row.RegistrationProviderFieldMappingId,
                    row.PlatformOptionKey, row.ProviderOptionKey, row.IsDeleted
                })), form);
    }

    private sealed class RetargetDelegatedBindingConnection : DbCommandInterceptor
    {
        internal const string UnregisteredRevision = "publication-unregistered-evidence";
        internal Guid TenantId { get; set; }
        internal Guid ConnectionId { get; set; }
        internal bool Armed { get; set; }
        internal int ChangedRows { get; private set; }
        internal RegistrationProviderTuple? ObservedTuple { get; private set; }
        internal DbTransaction? Transaction { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            var entity = context.Model.FindEntityType(typeof(RegistrationProviderConnection))!;
            var sql = context.GetService<ISqlGenerationHelper>();
            var update = "UPDATE " + sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            if (!command.CommandText.TrimStart().StartsWith(update, StringComparison.Ordinal)) return result;
            Armed = false;
            Transaction = command.Transaction;
            // Same-transaction source-order fault: not a competing PostgreSQL connection or a lock-blocking proof.
            ChangedRows = await context.RegistrationProviderConnections
                .Where(row => row.TenantId == TenantId && row.Id == ConnectionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConformanceEvidenceRevision, UnregisteredRevision), cancellationToken);
            var changed = await context.RegistrationProviderConnections.AsNoTracking()
                .SingleAsync(row => row.TenantId == TenantId && row.Id == ConnectionId, cancellationToken);
            ObservedTuple = new(changed.ProviderCode, changed.ProviderDeploymentCode, changed.ApiVersion,
                changed.AdapterPolicyVersion, changed.ConformanceEvidenceRevision);
            return result;
        }
    }

    private sealed class DelegatedBindingTransactions : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal List<DbTransaction> Started { get; } = [];

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ExploreDbContext) Started.Add(result);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DelegatedBindingCallbackReads : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal List<DbTransaction?> Transactions { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ExploreDbContext context && command.CommandText.Contains(
                context.Model.FindEntityType(typeof(IncomingWebhookEffectOutbox))!.GetTableName()!, StringComparison.Ordinal))
                Transactions.Add(command.Transaction);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AfterDelegatedBindingFlush(PublicationClock clock, bool unavailable) : SaveChangesInterceptor
    {
        internal DelegatedBindingDraft? Draft { get; set; }
        internal DateTimeOffset Crossing { get; set; }
        internal bool Armed { get; set; }
        internal DelegatedBindingPublicationSnapshot? Observed { get; private set; }
        internal bool? ObservedPrivacyOwner { get; private set; }
        internal DbTransaction? Transaction { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || Draft is not { } draft || eventData.Context is not ExploreDbContext context) return result;
            var binding = await context.RegistrationProviderBindings.AsNoTracking().Include(row => row.Connection)
                .Include(row => row.FieldMappings).Include(row => row.OptionMappings)
                .SingleOrDefaultAsync(row => row.Id == draft.BindingId
                    && row.StateId == (int)RegistrationProviderBindingStateEnum.Published, cancellationToken);
            if (binding is null) return result;
            Armed = false;
            var form = await context.RegistrationFormVersions.AsNoTracking().SingleAsync(row => row.Id == draft.VersionId, cancellationToken);
            Observed = DelegatedBindingPublicationSnapshot.From(binding, RegistrationPublicationSnapshot.From(form));
            ObservedPrivacyOwner = context.IdentityFenceOwnsTransaction;
            Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            if (unavailable)
                await context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
            else
                clock.Now = Crossing;
            return result;
        }
    }
}
