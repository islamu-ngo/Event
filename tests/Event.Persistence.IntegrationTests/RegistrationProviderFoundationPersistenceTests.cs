// ABOUTME: Verifies Phase 9 provider-neutral EF metadata, lookup seeding, filters, and credential references.
// ABOUTME: Includes the manual-QA persistence driver for qualified SecretBinding credentials and connections.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationProviderFoundationPersistenceTests
{
    private static readonly DateTime Now = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task EfModelMapsProviderFoundationWithoutCredentialValueColumns()
    {
        await using ExploreDbContext context = CreateModelContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        Type[] lookups = [typeof(RegistrationProviderKind), typeof(RegistrationProviderDeploymentKind), typeof(RegistrationProviderSchemaAuthority), typeof(RegistrationProviderPresentationMode), typeof(RegistrationProviderCollectionMode), typeof(RegistrationProviderCompletionMode), typeof(RegistrationProviderTrustLevel), typeof(RegistrationProviderDriftClass), typeof(RegistrationProviderBindingState)];
        foreach (Type lookupType in lookups)
        {
            IEntityType lookup = model.FindEntityType(lookupType)!;
            await Assert.That(lookup.FindPrimaryKey()!.Properties.Single().ClrType).IsEqualTo(typeof(int));
            await Assert.That(lookup.GetSeedData()).IsEmpty();
        }

        IEntityType connection = model.FindEntityType(typeof(RegistrationProviderConnection))!;
        await Assert.That(connection.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(connection.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(connection.GetProperties().Select(property => property.Name)).DoesNotContain("ApiToken");
        await Assert.That(connection.GetProperties().Select(property => property.Name)).DoesNotContain("WebhookSecret");
        await Assert.That(connection.FindProperty(nameof(RegistrationProviderConnection.ApiTokenSecretBindingId))).IsNotNull();
        await Assert.That(connection.FindProperty(nameof(RegistrationProviderConnection.WebhookSecretBindingId))).IsNotNull();

        IEntityType secret = model.FindEntityType(typeof(SecretBinding))!;
        await Assert.That(secret.FindProperty(nameof(SecretBinding.Qualifier))!.GetMaxLength()).IsEqualTo(128);
        await Assert.That(secret.GetIndexes().Any(index => index.IsUnique && HasProperties(index, nameof(SecretBinding.SettingKey), nameof(SecretBinding.ScopeId), nameof(SecretBinding.Qualifier)))).IsTrue();
    }

    [Test]
    public async Task RuntimeSeederRepairsRegistrationProviderLookups()
    {
        await using ExploreDbContext context = CreateInMemoryContext($"provider-seed-{Guid.NewGuid():N}");
        await LookupTableSeeder.SeedRegistrationProviderLookupsAsync(context, default);
        context.RegistrationProviderKinds.Remove(await context.RegistrationProviderKinds.SingleAsync(row => row.Id == (int)RegistrationProviderKindEnum.ExternalForm));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedRegistrationProviderLookupsAsync(context, default);
        await LookupTableSeeder.SeedRegistrationProviderLookupsAsync(context, default);

        await Assert.That(await context.RegistrationProviderKinds.CountAsync()).IsEqualTo(3);
        await Assert.That(await context.RegistrationProviderCollectionModes.CountAsync()).IsEqualTo(4);
        await Assert.That((await context.RegistrationProviderCollectionModes.SingleAsync(
            row => row.Id == (int)RegistrationProviderCollectionModeEnum.MirrorOnly)).MasterCode).IsEqualTo("MIRROR_ONLY");
        await Assert.That(await context.RegistrationProviderDriftClasses.CountAsync()).IsEqualTo(8);
        await Assert.That(await context.RegistrationProviderBindingStates.CountAsync()).IsEqualTo(4);
    }

    [Test]
    public async Task NamedFiltersHideDeletedAndCrossTenantProviderRows()
    {
        string databaseName = $"provider-filters-{Guid.NewGuid():N}";
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        await using (ExploreDbContext seed = CreateInMemoryContext(databaseName))
        {
            seed.RegistrationProviderConnections.AddRange(Connection(tenantA, "a", false), Connection(tenantA, "deleted", true), Connection(tenantB, "b", false));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext tenant = CreateInMemoryContext(databaseName);
        tenant.TenantContext = new TestTenantContext(tenantA);
        await Assert.That(await tenant.RegistrationProviderConnections.CountAsync()).IsEqualTo(1);
        await Assert.That(await tenant.RegistrationProviderConnections.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task ManualQa_DistinctQualifiedProviderCredentialsPersistWithoutConnectionSecretValues()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext($"provider-manual-qa-{Guid.NewGuid():N}");
        SecretBinding first = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken, SecretScope.Tenant, tenantId, "TOKEN_A", qualifier: "connection-a");
        SecretBinding second = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken, SecretScope.Tenant, tenantId, "TOKEN_B", qualifier: "connection-b");
        context.SecretBindings.AddRange(first, second);
        await context.SaveChangesAsync();
        context.RegistrationProviderConnections.AddRange(
            Connection(tenantId, "Connection A", false, first.Id),
            Connection(tenantId, "Connection B", false, second.Id));
        await context.SaveChangesAsync();
        context.TenantContext = new TestTenantContext(tenantId);

        await Assert.That(await context.SecretBindings.CountAsync(binding => binding.SettingKey == SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken)).IsEqualTo(2);
        await Assert.That(await context.RegistrationProviderConnections.CountAsync()).IsEqualTo(2);
        await Assert.That(context.Model.FindEntityType(typeof(RegistrationProviderConnection))!.GetProperties().Any(property => property.Name.Contains("Token", StringComparison.Ordinal) && property.ClrType == typeof(string))).IsFalse();
    }

    [Test]
    public async Task SecretPurposeMismatchFailsBeforePersistence()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext($"provider-secret-purpose-{Guid.NewGuid():N}");
        SecretBinding smtpBinding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Smtp.Password, SecretScope.Tenant, tenantId, "SMTP_PASSWORD", qualifier: "connection-a");
        context.SecretBindings.Add(smtpBinding);
        await context.SaveChangesAsync();
        context.RegistrationProviderConnections.Add(RegistrationProviderConnection.Create(
            tenantId, "Connection A", RegistrationProviderKindEnum.ExternalForm,
            RegistrationProviderDeploymentKindEnum.HostedSaas, "FORMBRICKS", "HOSTED_SAAS", "v1", "formbricks-policy-v1",
            "official-api-v1-2026-08", "https:/" + "/app.formbricks.com/api/v1/management", "https:/" + "/app.formbricks.com",
            "workspace", smtpBinding.Id, null, Now));

        await Assert.That(() => context.SaveChangesAsync()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BindingWebhookSecretReferenceRequiresTenantWebhookKeyAndBindingQualifier()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext($"provider-binding-secret-{Guid.NewGuid():N}");
        RegistrationProviderBinding binding = Binding(tenantId, Guid.CreateVersion7());
        SecretBinding wrongQualifier = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret,
            SecretScope.Tenant,
            tenantId,
            "WEBHOOK_SECRET",
            qualifier: "other-binding");
        context.SecretBindings.Add(wrongQualifier);
        await context.SaveChangesAsync();
        binding.SetDraftProvisionedSubscription("webhook-1", wrongQualifier.Id);
        context.RegistrationProviderBindings.Add(binding);

        await Assert.That(() => context.SaveChangesAsync()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ChildQueriesHideRowsWhenParentBindingOrConnectionIsSoftDeleted()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext($"provider-child-filters-{Guid.NewGuid():N}");
        RegistrationProviderConnection connection = Connection(tenantId, "a", false);
        RegistrationProviderBinding binding = Binding(tenantId, connection.Id);
        RegistrationProviderFieldMapping field = RegistrationProviderFieldMapping.Create(binding, "attendee.email", "email", true);
        binding.AddFieldMapping(field);
        binding.AddOptionMapping(RegistrationProviderOptionMapping.Create(binding, field, "yes", "1"));
        binding.AddCapability(RegistrationProviderCapability.Create(binding, "unknown", "hosted", "v1", "policy", "evidence", "callback"));
        RegistrationProviderSchemaRevision revision = RegistrationProviderSchemaRevision.Create(
            tenantId,
            connection.Id,
            RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered,
            Hash(),
            "survey-1",
            "revision-1",
            "{\"schema\":\"test\",\"fields\":[]}",
            new string('0', 64),
            RegistrationProviderDriftClassEnum.NoDrift,
            Now);
        context.AddRange(connection, binding, revision);
        await context.SaveChangesAsync();
        context.TenantContext = new TestTenantContext(tenantId);
        await Assert.That(await context.RegistrationProviderFieldMappings.CountAsync()).IsEqualTo(1);

        binding.IsDeleted = true;
        connection.IsDeleted = true;
        await context.SaveChangesAsync();

        await Assert.That(await context.RegistrationProviderCapabilities.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.RegistrationProviderFieldMappings.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.RegistrationProviderOptionMappings.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.RegistrationProviderSchemaRevisions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task CrossTenantBindingConnectionCompositeForeignKeyFails()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"provider-cross-tenant-{Guid.CreateVersion7():N}.db");
        try
        {
            await using ExploreDbContext context = CreateSqliteContext(databasePath);
            await context.Database.EnsureCreatedAsync();
            await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
            Guid tenantA = Guid.CreateVersion7();
            Guid tenantB = Guid.CreateVersion7();
            context.Tenants.AddRange(Tenant(tenantA, "a"), Tenant(tenantB, "b"));
            RegistrationProviderConnection connection = Connection(tenantA, "a", false);
            context.RegistrationProviderConnections.Add(connection);
            await context.SaveChangesAsync();

            context.RegistrationProviderBindings.Add(Binding(tenantB, connection.Id));

            await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task AttemptProviderRevisionForeignKeyTargetsPublishedBindingRevision()
    {
        await using ExploreDbContext context = CreateModelContext();
        IForeignKey? foreignKey = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RegistrationAttempt))!
            .GetForeignKeys().SingleOrDefault(candidate => candidate.PrincipalEntityType.ClrType == typeof(RegistrationProviderBinding) &&
                candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(RegistrationAttempt.TenantId), nameof(RegistrationAttempt.RegistrationProviderBindingId), "ProviderMappingRevisionHashKey"]));

        await Assert.That(foreignKey).IsNotNull();
        await Assert.That(foreignKey!.PrincipalKey.Properties.Select(property => property.Name)).IsEquivalentTo([
            nameof(RegistrationProviderBinding.TenantId), nameof(RegistrationProviderBinding.Id), nameof(RegistrationProviderBinding.PublishedMappingRevisionHashKey)]);
    }

    [Test]
    public async Task ProviderManagementQueue_IsolatesRetainedEffectsByBindingAndEvent()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventA = Guid.CreateVersion7();
        Guid eventB = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext($"provider-management-queue-{Guid.NewGuid():N}");
        RegistrationProviderConnection connection = Connection(tenantId, "a", false);
        RegistrationForm formA = RegistrationForm.Create(tenantId, eventA, "registration", "a", "A", Now);
        RegistrationForm formB = RegistrationForm.Create(tenantId, eventB, "registration", "b", "B", Now);
        RegistrationProviderBinding bindingA = Binding(tenantId, connection.Id, formA.Id);
        RegistrationProviderBinding bindingB = Binding(tenantId, connection.Id, formB.Id);
        context.AddRange(connection, formA, formB, bindingA, bindingB);
        AddRetainedEffect(context, tenantId, bindingA.Id, "a", Now.AddMinutes(-30));
        Complete(AddRetainedEffect(context, tenantId, bindingA.Id, "a-callback", Now.AddMinutes(-30)), Now.AddMinutes(-29));
        AddRetainedEffect(context, tenantId, bindingB.Id, "b", Now.AddMinutes(-5));
        await context.SaveChangesAsync();
        context.TenantContext = new TestTenantContext(tenantId);
        RegistrationProviderRepository repository = new(context);

        IReadOnlyList<RegistrationProviderParkedItem> rows = await repository.GetParkedItemsForEventAsync(tenantId, eventA, 10, CancellationToken.None);

        await Assert.That(rows).HasSingleItem();
        await Assert.That(rows[0].Effect!.BindingId).IsEqualTo(bindingA.Id);
        await Assert.That(rows[0].Effect!.EventId).IsEqualTo(eventA);
        await Assert.That(await repository.GetLastCallbackAtAsync(tenantId, bindingA.Id, CancellationToken.None)).IsEqualTo(Now.AddMinutes(-30));
        await Assert.That(await repository.GetOldestPendingItemAtAsync(tenantId, bindingA.Id, CancellationToken.None)).IsEqualTo(Now.AddMinutes(-30));
    }

    [Test]
    public async Task LastCallback_ExcludesManualImportsAndRequiresCompletedCallbackEffect()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext($"provider-callback-gate-{Guid.NewGuid():N}");
        IncomingWebhookEffectOutbox callback = AddRetainedEffect(context, tenantId, bindingId, "callback", Now);
        IncomingWebhookEffectOutbox manual = AddRetainedEffect(
            context, tenantId, bindingId, "manual", Now.AddMinutes(1), "registration.provider_manual_import");
        Complete(callback, Now.AddMinutes(2));
        Complete(manual, Now.AddMinutes(3));
        AddRetainedEffect(context, tenantId, bindingId, "pending", Now.AddMinutes(4));
        await context.SaveChangesAsync();
        var repository = new RegistrationProviderRepository(context);

        DateTime? result = await repository.GetLastCallbackAtAsync(tenantId, bindingId, CancellationToken.None);

        await Assert.That(result).IsEqualTo(Now);
    }

    [Test]
    public async Task ProviderManagementQueue_ExcludesResolvedEffectsAndSubmissions()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext($"provider-management-resolved-{Guid.NewGuid():N}");
        RegistrationProviderConnection connection = Connection(tenantId, "a", false);
        RegistrationForm form = RegistrationForm.Create(tenantId, eventId, "registration", "a", "A", Now);
        RegistrationProviderBinding binding = Binding(tenantId, connection.Id, form.Id);
        context.AddRange(connection, form, binding);
        IncomingWebhookEffectOutbox effect = AddRetainedEffect(context, tenantId, binding.Id, "resolved", Now.AddMinutes(-30));
        RegistrationSubmission submission = ProviderSubmission(tenantId, eventId, binding.Id);
        context.AddRange(
            submission,
            RegistrationSubmissionIssue.Create(submission, "BLOCKING_DRIFT", Now));
        await context.SaveChangesAsync();
        context.TenantContext = new TestTenantContext(tenantId);
        RegistrationProviderRepository repository = new(context);

        await Assert.That((await repository.GetParkedItemsForEventAsync(tenantId, eventId, 10, CancellationToken.None)).Count).IsEqualTo(2);

        effect.AcknowledgeResolution("organizer_accepted", Now.AddMinutes(1));
        await repository.AddSubmissionIssueAsync(RegistrationSubmissionIssue.Create(submission, "RESOLVED_ACCEPTED", Now.AddMinutes(1)), CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        IReadOnlyList<RegistrationProviderParkedItem> rows = await repository.GetParkedItemsForEventAsync(tenantId, eventId, 10, CancellationToken.None);

        await Assert.That(rows).IsEmpty();
        await Assert.That(await repository.CountParkedItemsAsync(tenantId, binding.Id, CancellationToken.None)).IsEqualTo(0);
        await Assert.That(await repository.GetOldestPendingItemAtAsync(tenantId, binding.Id, CancellationToken.None)).IsNull();
        await Assert.That(await context.RegistrationSubmissionIssues.AnyAsync(issue => issue.RegistrationSubmissionId == submission.Id && issue.Code == "BLOCKING_DRIFT")).IsTrue();
    }

    [Test]
    public async Task ApprovedOrigins_ReaddingSameOriginRevivesSoftDeletedRow()
    {
        Guid tenantId = Guid.CreateVersion7();
        RegistrationProviderConnection connection = Connection(tenantId, "origins", false);

        connection.ReplaceApprovedOrigins(["https://forms.example.org"], Now);
        connection.ReplaceApprovedOrigins([], Now.AddMinutes(1));
        connection.ReplaceApprovedOrigins(["https://forms.example.org"], Now.AddMinutes(2));

        await Assert.That(connection.ApprovedOrigins).HasSingleItem();
        await Assert.That(connection.ApprovedOrigins.Single().IsDeleted).IsFalse();
        await Assert.That(connection.IsOriginApproved(new Uri("https://forms.example.org/launch"))).IsTrue();
    }

    [Test]
    public async Task ApprovedOrigins_ReaddingPersistedSoftDeletedOriginRevivesExistingRow()
    {
        string databaseName = $"provider-origin-revive-{Guid.NewGuid():N}";
        Guid tenantId = Guid.CreateVersion7();
        Guid connectionId;

        await using (ExploreDbContext seed = CreateInMemoryContext(databaseName))
        {
            RegistrationProviderConnection connection = Connection(tenantId, "origins", false);
            connectionId = connection.Id;
            connection.ReplaceApprovedOrigins(["https://forms.example.org"], Now);
            seed.RegistrationProviderConnections.Add(connection);
            await seed.SaveChangesAsync();
        }

        await using (ExploreDbContext removeContext = CreateInMemoryContext(databaseName))
        {
            removeContext.TenantContext = new TestTenantContext(tenantId);
            RegistrationProviderRepository repository = new(removeContext);
            RegistrationProviderConnection connection = (await repository.GetConnectionAsync(tenantId, connectionId, CancellationToken.None))!;
            connection.ReplaceApprovedOrigins([], Now.AddMinutes(1));
            await repository.SaveChangesAsync(CancellationToken.None);
        }

        await using (ExploreDbContext readdContext = CreateInMemoryContext(databaseName))
        {
            readdContext.TenantContext = new TestTenantContext(tenantId);
            RegistrationProviderRepository repository = new(readdContext);
            RegistrationProviderConnection connection = (await repository.GetConnectionAsync(tenantId, connectionId, CancellationToken.None))!;
            await Assert.That(connection.ApprovedOrigins).HasSingleItem();
            await Assert.That(connection.ApprovedOrigins.Single().IsDeleted).IsTrue();

            connection.ReplaceApprovedOrigins(["https://forms.example.org"], Now.AddMinutes(2));
            await repository.SaveChangesAsync(CancellationToken.None);
        }

        await using ExploreDbContext verifyContext = CreateInMemoryContext(databaseName);
        verifyContext.TenantContext = new TestTenantContext(tenantId);
        RegistrationProviderRepository verifyRepository = new(verifyContext);
        RegistrationProviderConnection reloaded = (await verifyRepository.GetConnectionAsync(tenantId, connectionId, CancellationToken.None))!;

        await Assert.That(reloaded.ApprovedOrigins).HasSingleItem();
        await Assert.That(reloaded.ApprovedOrigins.Single().IsDeleted).IsFalse();
        await Assert.That(reloaded.IsOriginApproved(new Uri("https://forms.example.org/launch"))).IsTrue();
    }

    [Test]
    public async Task SubscriptionStateModelUsesTenantBindingCompositeKeyAndNamedFilters()
    {
        await using ExploreDbContext context = CreateModelContext();
        IEntityType state = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RegistrationProviderSubscriptionState))!;

        await Assert.That(state.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(state.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(state.FindProperty(nameof(RegistrationProviderSubscriptionState.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
        await Assert.That(state.GetIndexes().Any(index => index.IsUnique && HasProperties(index,
            nameof(RegistrationProviderSubscriptionState.TenantId),
            nameof(RegistrationProviderSubscriptionState.RegistrationProviderBindingId),
            nameof(RegistrationProviderSubscriptionState.ProviderEventType)))).IsTrue();
        IForeignKey bindingKey = state.GetForeignKeys().Single(key => key.PrincipalEntityType.ClrType == typeof(RegistrationProviderBinding));
        await Assert.That(bindingKey.Properties.Select(property => property.Name)).IsEquivalentTo([
            nameof(RegistrationProviderSubscriptionState.TenantId),
            nameof(RegistrationProviderSubscriptionState.RegistrationProviderBindingId)]);
    }

    [Test]
    public async Task SubscriptionStateRepositoryClaimsDueRenewalAndRejectsStaleConcurrency()
    {
        string databaseName = $"provider-subscription-state-{Guid.NewGuid():N}";
        Guid tenantId = Guid.CreateVersion7();
        Guid bindingId;
        Guid stateId;
        await using (ExploreDbContext seed = CreateInMemoryContext(databaseName))
        {
            RegistrationProviderConnection connection = Connection(tenantId, "subscription", false);
            RegistrationProviderBinding binding = Binding(tenantId, connection.Id);
            RegistrationProviderSubscriptionState state = RegistrationProviderSubscriptionState.Create(
                tenantId, binding.Id, "google.forms.responses", "watch-1", Now.AddHours(1), "sync-1", Now);
            bindingId = binding.Id;
            stateId = state.Id;
            seed.AddRange(connection, binding, state);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext firstContext = CreateInMemoryContext(databaseName);
        await using ExploreDbContext secondContext = CreateInMemoryContext(databaseName);
        RegistrationProviderSubscriptionStateRepository repository = new(firstContext);
        IReadOnlyList<RegistrationProviderSubscriptionState> claims = await repository.ClaimDueRenewalsAsync(
            1, Now.AddHours(2), Now.AddMinutes(1), TimeSpan.FromMinutes(5), CancellationToken.None);
        RegistrationProviderSubscriptionState stale = (await secondContext.RegistrationProviderSubscriptionStates
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubscriptionStateWorkerCrossTenantQueue)
            .SingleAsync(value => value.Id == stateId))!;
        stale.Claim(Guid.CreateVersion7(), Now.AddMinutes(10), Now.AddMinutes(6));

        await Assert.That(claims).HasSingleItem();
        await Assert.That(claims[0].TenantId).IsEqualTo(tenantId);
        await Assert.That(claims[0].RegistrationProviderBindingId).IsEqualTo(bindingId);
        claims[0].SettleCheckpoint(claims[0].LeaseToken!.Value, claims[0].ProcessingGeneration, "sync-2", Now.AddMinutes(2));
        await repository.SaveChangesAsync(CancellationToken.None);

        await Assert.That(() => secondContext.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
    }

    [Test]
    public async Task SubscriptionStateSweepSettleDoesNotImmediatelyReclaimUntilNewNotification()
    {
        string databaseName = $"provider-subscription-sweep-{Guid.NewGuid():N}";
        Guid tenantId = Guid.CreateVersion7();
        Guid stateId;
        await using (ExploreDbContext seed = CreateInMemoryContext(databaseName))
        {
            RegistrationProviderConnection connection = Connection(tenantId, "sweep", false);
            RegistrationProviderBinding binding = Binding(tenantId, connection.Id);
            RegistrationProviderSubscriptionState seededState = RegistrationProviderSubscriptionState.Create(
                tenantId, binding.Id, "google.forms.responses", "watch-1", Now.AddDays(1), "sync-1", Now);
            seededState.ReceiveNotification(Now.AddMinutes(1));
            stateId = seededState.Id;
            seed.AddRange(connection, binding, seededState);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateInMemoryContext(databaseName);
        RegistrationProviderSubscriptionStateRepository repository = new(context);
        IReadOnlyList<RegistrationProviderSubscriptionState> first = await repository.ClaimDueSweepsAsync(
            1, Now.AddMinutes(2), TimeSpan.FromMinutes(5), CancellationToken.None);
        first[0].SettleCheckpoint(first[0].LeaseToken!.Value, first[0].ProcessingGeneration, "sync-2", Now.AddMinutes(3));
        await repository.SaveChangesAsync(CancellationToken.None);

        IReadOnlyList<RegistrationProviderSubscriptionState> second = await repository.ClaimDueSweepsAsync(
            1, Now.AddMinutes(4), TimeSpan.FromMinutes(5), CancellationToken.None);

        await Assert.That(second).IsEmpty();

        RegistrationProviderSubscriptionState reloadedState = await context.RegistrationProviderSubscriptionStates
            .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubscriptionStateWorkerCrossTenantQueue)
            .SingleAsync(value => value.Id == stateId);
        reloadedState.ReceiveNotification(Now.AddMinutes(5));
        await context.SaveChangesAsync();

        await Assert.That(await repository.ClaimDueSweepsAsync(1, Now.AddMinutes(6), TimeSpan.FromMinutes(5), CancellationToken.None)).HasSingleItem();
    }

    [Test]
    public async Task SubscriptionStatePeriodicSweepClaimsWhenNoPendingNotificationButScheduleIsStale()
    {
        string databaseName = $"provider-subscription-periodic-sweep-{Guid.NewGuid():N}";
        Guid tenantId = Guid.CreateVersion7();
        await using (ExploreDbContext seed = CreateInMemoryContext(databaseName))
        {
            RegistrationProviderConnection connection = Connection(tenantId, "periodic-sweep", false);
            RegistrationProviderBinding binding = Binding(tenantId, connection.Id);
            RegistrationProviderSubscriptionState state = RegistrationProviderSubscriptionState.Create(
                tenantId, binding.Id, "google.forms.responses", "watch-1", Now.AddDays(1), "sync-1", Now);
            state.Claim(Guid.CreateVersion7(), Now.AddMinutes(2), Now);
            state.SettleCheckpoint(state.LeaseToken!.Value, state.ProcessingGeneration, "sync-2", Now.AddHours(6), Now.AddMinutes(1));
            seed.AddRange(connection, binding, state);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateInMemoryContext(databaseName);
        RegistrationProviderSubscriptionStateRepository repository = new(context);

        await Assert.That(await repository.ClaimDueSweepsAsync(1, Now.AddHours(5), TimeSpan.FromMinutes(5), CancellationToken.None)).IsEmpty();
        await Assert.That(await repository.ClaimDueSweepsAsync(1, Now.AddHours(6), TimeSpan.FromMinutes(5), CancellationToken.None)).HasSingleItem();
    }

    [Test]
    public async Task SubscriptionStateFailureBackoffAndDuplicateClaimLoserReturnEmpty()
    {
        string databaseName = $"provider-subscription-claim-race-{Guid.NewGuid():N}";
        Guid tenantId = Guid.CreateVersion7();
        await using (ExploreDbContext seed = CreateInMemoryContext(databaseName))
        {
            RegistrationProviderConnection connection = Connection(tenantId, "race", false);
            RegistrationProviderBinding binding = Binding(tenantId, connection.Id);
            RegistrationProviderSubscriptionState state = RegistrationProviderSubscriptionState.Create(
                tenantId, binding.Id, "google.forms.responses", "watch-1", Now.AddDays(1), "sync-1", Now);
            state.ReceiveNotification(Now.AddMinutes(1));
            seed.AddRange(connection, binding, state);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext firstContext = CreateInMemoryContext(databaseName);
        await using ExploreDbContext secondContext = CreateInMemoryContext(databaseName);
        RegistrationProviderSubscriptionStateRepository firstRepository = new(firstContext);
        RegistrationProviderSubscriptionStateRepository secondRepository = new(secondContext);
        IReadOnlyList<RegistrationProviderSubscriptionState> first = await firstRepository.ClaimDueSweepsAsync(
            1, Now.AddMinutes(2), TimeSpan.FromMinutes(5), CancellationToken.None);
        IReadOnlyList<RegistrationProviderSubscriptionState> duplicate = await secondRepository.ClaimDueSweepsAsync(
            1, Now.AddMinutes(2), TimeSpan.FromMinutes(5), CancellationToken.None);

        await Assert.That(first).HasSingleItem();
        await Assert.That(duplicate).IsEmpty();

        first[0].Fail(RegistrationProviderSubscriptionOperation.Sweep, first[0].LeaseToken!.Value, first[0].ProcessingGeneration, "provider_timeout", Now.AddMinutes(10), Now.AddMinutes(3));
        await firstRepository.SaveChangesAsync(CancellationToken.None);

        await Assert.That(await firstRepository.ClaimDueSweepsAsync(1, Now.AddMinutes(4), TimeSpan.FromMinutes(5), CancellationToken.None)).IsEmpty();
        await Assert.That(await firstRepository.ClaimDueSweepsAsync(1, Now.AddMinutes(11), TimeSpan.FromMinutes(5), CancellationToken.None)).HasSingleItem();
    }

    [Test]
    public async Task SubscriptionStateSweepBackoffDoesNotSuppressUrgentRenewal()
    {
        string databaseName = $"provider-subscription-sweep-renewal-{Guid.NewGuid():N}";
        Guid tenantId = Guid.CreateVersion7();
        await using (ExploreDbContext seed = CreateInMemoryContext(databaseName))
        {
            RegistrationProviderConnection connection = Connection(tenantId, "sweep-renewal", false);
            RegistrationProviderBinding binding = Binding(tenantId, connection.Id);
            RegistrationProviderSubscriptionState state = RegistrationProviderSubscriptionState.Create(
                tenantId, binding.Id, "google.forms.responses", "watch-1", Now.AddMinutes(30), "sync-1", Now);
            state.ReceiveNotification(Now.AddMinutes(1));
            seed.AddRange(connection, binding, state);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateInMemoryContext(databaseName);
        RegistrationProviderSubscriptionStateRepository repository = new(context);
        IReadOnlyList<RegistrationProviderSubscriptionState> sweep = await repository.ClaimDueSweepsAsync(
            1, Now.AddMinutes(2), TimeSpan.FromMinutes(5), CancellationToken.None);
        sweep[0].Fail(RegistrationProviderSubscriptionOperation.Sweep, sweep[0].LeaseToken!.Value, sweep[0].ProcessingGeneration, "sweep_timeout", Now.AddMinutes(20), Now.AddMinutes(3));
        await repository.SaveChangesAsync(CancellationToken.None);

        await Assert.That(await repository.ClaimDueRenewalsAsync(1, Now.AddHours(1), Now.AddMinutes(4), TimeSpan.FromMinutes(5), CancellationToken.None)).HasSingleItem();
        await Assert.That(await repository.ClaimDueSweepsAsync(1, Now.AddMinutes(4), TimeSpan.FromMinutes(5), CancellationToken.None)).IsEmpty();
    }

    [Test]
    public async Task SubscriptionStateRenewalBackoffDoesNotSuppressResponseRecovery()
    {
        string databaseName = $"provider-subscription-renewal-sweep-{Guid.NewGuid():N}";
        Guid tenantId = Guid.CreateVersion7();
        await using (ExploreDbContext seed = CreateInMemoryContext(databaseName))
        {
            RegistrationProviderConnection connection = Connection(tenantId, "renewal-sweep", false);
            RegistrationProviderBinding binding = Binding(tenantId, connection.Id);
            RegistrationProviderSubscriptionState state = RegistrationProviderSubscriptionState.Create(
                tenantId, binding.Id, "google.forms.responses", "watch-1", Now.AddMinutes(30), "sync-1", Now);
            state.ReceiveNotification(Now.AddMinutes(1));
            seed.AddRange(connection, binding, state);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateInMemoryContext(databaseName);
        RegistrationProviderSubscriptionStateRepository repository = new(context);
        IReadOnlyList<RegistrationProviderSubscriptionState> renewal = await repository.ClaimDueRenewalsAsync(
            1, Now.AddHours(1), Now.AddMinutes(2), TimeSpan.FromMinutes(5), CancellationToken.None);
        renewal[0].Fail(RegistrationProviderSubscriptionOperation.Renewal, renewal[0].LeaseToken!.Value, renewal[0].ProcessingGeneration, "renewal_timeout", Now.AddMinutes(20), Now.AddMinutes(3));
        await repository.SaveChangesAsync(CancellationToken.None);

        await Assert.That(await repository.ClaimDueRenewalsAsync(1, Now.AddHours(1), Now.AddMinutes(4), TimeSpan.FromMinutes(5), CancellationToken.None)).IsEmpty();
        await Assert.That(await repository.ClaimDueSweepsAsync(1, Now.AddMinutes(4), TimeSpan.FromMinutes(5), CancellationToken.None)).HasSingleItem();
    }

    private static RegistrationProviderConnection Connection(Guid tenantId, string name, bool deleted, Guid? apiTokenSecretBindingId = null)
    {
        RegistrationProviderConnection connection = RegistrationProviderConnection.Create(tenantId, name, RegistrationProviderKindEnum.ExternalForm,
            RegistrationProviderDeploymentKindEnum.HostedSaas, "FORMBRICKS", name, "v1", "formbricks-policy-v1",
            "official-api-v1-2026-08", "https:/" + "/app.formbricks.com/api/v1/management", "https:/" + "/app.formbricks.com",
            "workspace-" + name, apiTokenSecretBindingId, null, Now);
        connection.IsDeleted = deleted;
        return connection;
    }

    private static RegistrationProviderBinding Binding(Guid tenantId, Guid connectionId, Guid? formId = null) => RegistrationProviderBinding.Create(
        tenantId, connectionId, formId ?? Guid.CreateVersion7(), Guid.CreateVersion7(), RegistrationProviderPresentationModeEnum.Redirect,
        RegistrationProviderCollectionModeEnum.ProviderHosted, RegistrationProviderCompletionModeEnum.Callback,
        RegistrationProviderTrustLevelEnum.SelectedFields, null, Now);

    private static IncomingWebhookEffectOutbox AddRetainedEffect(
        ExploreDbContext context,
        Guid tenantId,
        Guid bindingId,
        string suffix,
        DateTime createdAt,
        string eventType = "registration.provider_submission")
    {
        byte[] payload = [1];
        string hash = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        string providerDecisionId = $"{bindingId:N}:{suffix}";
        IncomingWebhookMessage message = IncomingWebhookMessage.CreateVerified(
            tenantId,
            "registration-provider",
            providerDecisionId,
            providerDecisionId,
            eventType,
            payload,
            hash,
            "application/json",
            "utf-8",
            "{}",
            createdAt,
            createdAt,
            createdAt.AddDays(1),
            "test",
            createdAt.AddDays(1),
            createdAt.AddDays(1),
            createdAt.AddDays(1),
            createdAt.AddDays(1));
        IncomingWebhookEffectOutbox effect = IncomingWebhookEffectOutbox.CreatePending(
            tenantId,
            message.Id,
            "registration-provider",
            providerDecisionId,
            "registration.provider_submission",
            hash,
            createdAt);
        context.AddRange(message, effect);
        return effect;
    }

    private static void Complete(IncomingWebhookEffectOutbox effect, DateTime completedAt)
    {
        Guid leaseToken = Guid.CreateVersion7();
        effect.Claim("test", leaseToken, completedAt.AddMinutes(1), completedAt.AddMinutes(-1));
        effect.Complete(leaseToken, effect.ProcessingFence, effect.ProcessingGeneration, completedAt);
    }

    private static RegistrationSubmission ProviderSubmission(Guid tenantId, Guid eventId, Guid bindingId)
    {
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            Guid.CreateVersion7(), tenantId, eventId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])),
            bindingId, RegistrationEvidenceHash.Create(Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray())), Now, Now.AddHours(1));
        return RegistrationSubmission.CreateProviderEvidenceOnly(
            attempt,
            RegistrationEvidenceHash.Create(Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray())),
            Now.AddMinutes(1),
            null,
            "provider-submission-1",
            "revision-1",
            null,
            null);
    }

    private static Tenant Tenant(Guid id, string slug) => new()
    {
        Id = id,
        FullName = slug,
        Slug = $"provider-{slug}-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
    };

    private static RegistrationEvidenceHash Hash() => RegistrationEvidenceHash.Create(Convert.ToBase64String(new byte[32]));

    private static bool HasProperties(IReadOnlyIndex index, params string[] propertyNames) =>
        index.Properties.Select(property => property.Name).SequenceEqual(propertyNames);

    private static ExploreDbContext CreateModelContext() => new(TestDbContextOptions.Create<ExploreDbContext>()
        .UseNpgsql("Host=localhost;Database=provider_model;Username=unused;Password=unused")
        .UseSnakeCaseNamingConvention().Options);

    private readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot _databaseRoot = new();

    private ExploreDbContext CreateInMemoryContext(string databaseName) => new(TestDbContextOptions.Create<ExploreDbContext>()
        .UseTestInMemoryDatabase(databaseName, _databaseRoot).Options);

    private static ExploreDbContext CreateSqliteContext(string databasePath) => new(TestDbContextOptions.Create<ExploreDbContext>()
        .UseSqlite($"Data Source={databasePath}")
        .UseSnakeCaseNamingConvention().Options);

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
        public string? TenantSlug => null;
        public bool IsResolved => true;
    }
}
