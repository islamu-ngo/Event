// ABOUTME: Verifies the immutable registration-form EF model, lookup seeding, and named isolation filters.
// ABOUTME: Covers composite graph boundaries, portable metadata, provider-neutral identity, and language persistence.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationFormPersistenceTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task EfModelMapsTenantSafeImmutableFormGraph()
    {
        await using ExploreDbContext context = CreateModelContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;
        Type[] graphTypes =
        [
            typeof(RegistrationForm), typeof(RegistrationFormVersion), typeof(RegistrationFormSection),
            typeof(RegistrationFormField), typeof(RegistrationFormFieldOption)
        ];

        foreach (Type graphType in graphTypes)
        {
            IEntityType entity = model.FindEntityType(graphType)!;
            await Assert.That(entity.FindProperty("Id")!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
            await Assert.That(entity.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken).IsTrue();
            await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
            await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
            await Assert.That(entity.GetProperties().Any(property =>
                property.Name.Contains("ProviderQuestion", StringComparison.OrdinalIgnoreCase))).IsFalse();
        }

        IEntityType version = model.FindEntityType(typeof(RegistrationFormVersion))!;
        IEntityType section = model.FindEntityType(typeof(RegistrationFormSection))!;
        IEntityType field = model.FindEntityType(typeof(RegistrationFormField))!;
        IEntityType option = model.FindEntityType(typeof(RegistrationFormFieldOption))!;
        await Assert.That(version.FindProperty(nameof(RegistrationFormVersion.LanguageTag))!.GetMaxLength()).IsEqualTo(35);
        await Assert.That(version.FindProperty(nameof(RegistrationFormVersion.SchemaHash))!.GetMaxLength()).IsEqualTo(64);
        await Assert.That(field.FindProperty(nameof(RegistrationFormField.ConsentPurposeCode))!.GetMaxLength()).IsEqualTo(100);
        await Assert.That(field.FindProperty(nameof(RegistrationFormField.ConsentText))!.GetMaxLength()).IsEqualTo(4000);
        await Assert.That(field.FindProperty(nameof(RegistrationFormField.ConsentTextVersion))!.GetMaxLength()).IsEqualTo(100);
        await Assert.That(field.FindProperty(nameof(RegistrationFormField.ConsentPurposeCode))!.IsNullable).IsTrue();
        await Assert.That(field.FindProperty(nameof(RegistrationFormField.ConsentText))!.IsNullable).IsTrue();
        await Assert.That(field.FindProperty(nameof(RegistrationFormField.ConsentTextVersion))!.IsNullable).IsTrue();
        await Assert.That(field.GetCheckConstraints().Any(constraint =>
            constraint.Name == "ck_registration_form_fields_consent_metadata")).IsTrue();
        foreach (string artifact in new[]
                 {
                     nameof(RegistrationFormVersion.DataSchemaArtifact), nameof(RegistrationFormVersion.UiSchemaArtifact),
                     nameof(RegistrationFormVersion.LogicSchemaArtifact), nameof(RegistrationFormVersion.MappingArtifact)
                 })
        {
            await Assert.That(version.FindProperty(artifact)!.GetColumnType()).IsEqualTo("text");
        }
        await AssertCompositeForeignKey(version, typeof(RegistrationForm), "TenantId", "EventId", "RegistrationFormId");
        await AssertCompositeForeignKey(section, typeof(RegistrationFormVersion),
            "TenantId", "EventId", "RegistrationFormId", "RegistrationFormVersionId");
        await AssertCompositeForeignKey(field, typeof(RegistrationFormSection),
            "TenantId", "EventId", "RegistrationFormId", "RegistrationFormVersionId", "RegistrationFormSectionId");
        await AssertCompositeForeignKey(option, typeof(RegistrationFormField),
            "TenantId", "EventId", "RegistrationFormId", "RegistrationFormVersionId", "RegistrationFormSectionId", "RegistrationFormFieldId");
    }

    [Test]
    public async Task SeederRepairsFormLookupRowsIdempotently()
    {
        await using ExploreDbContext context = CreateInMemoryContext($"task72-seed-{Guid.NewGuid():N}");
        await LookupTableSeeder.SeedRegistrationFormLookupsAsync(context, default);
        context.RegistrationFormStatuses.Remove(await context.RegistrationFormStatuses.SingleAsync(row => row.Id == 2));
        context.RegistrationFieldTypes.Remove(await context.RegistrationFieldTypes.SingleAsync(row => row.Id == 9));
        context.RegistrationOrganizerVisibilities.Remove(await context.RegistrationOrganizerVisibilities.SingleAsync(row => row.Id == 2));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedRegistrationFormLookupsAsync(context, default);
        await LookupTableSeeder.SeedRegistrationFormLookupsAsync(context, default);

        await Assert.That(await context.RegistrationFormStatuses.CountAsync()).IsEqualTo(3);
        await Assert.That(await context.RegistrationFieldTypes.CountAsync()).IsEqualTo(19);
        await Assert.That(await context.RegistrationOrganizerVisibilities.CountAsync()).IsEqualTo(2);
        await Assert.That((await context.RegistrationFieldTypes.SingleAsync(row => row.Id == 19)).MasterCode)
            .IsEqualTo("OPAQUE_EXTERNAL");
    }

    [Test]
    public async Task NamedFiltersHideDeletedAndCrossTenantRowsAcrossEveryGraphLevel()
    {
        string database = $"task72-filter-{Guid.NewGuid():N}";
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        await using (ExploreDbContext seed = CreateInMemoryContext(database))
        {
            RegistrationForm tenantAForm = Graph(tenantA, false);
            RegistrationForm deletedTenantAForm = Graph(tenantA, true);
            RegistrationForm tenantBForm = Graph(tenantB, false);
            seed.AddRange(
                tenantAForm,
                deletedTenantAForm,
                tenantBForm,
                RegistrationFormTemplate.Create(null, "Platform", "Platform", "Registration", null, tenantAForm.Versions.Single(), Now),
                RegistrationFormTemplate.Create(tenantA, "Tenant A", "Tenant", "Registration", null, tenantAForm.Versions.Single(), Now),
                RegistrationFormTemplate.Create(tenantB, "Tenant B", "Tenant", "Registration", null, tenantBForm.Versions.Single(), Now),
                DeletedTemplate(tenantA, deletedTenantAForm.Versions.Single()));
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateInMemoryContext(database);
        context.TenantContext = new TestTenantContext(tenantA);
        await Assert.That(await context.RegistrationForms.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.RegistrationFormVersions.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.RegistrationFormSections.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.RegistrationFormFields.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.RegistrationFormFieldOptions.CountAsync()).IsEqualTo(1);
        await Assert.That((await new RegistrationFormTemplateRepository(context).ListAsync(CancellationToken.None)).Count).IsEqualTo(2);
        await Assert.That(await context.RegistrationForms.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync()).IsEqualTo(2);
    }

    private static RegistrationFormTemplate DeletedTemplate(Guid tenantId, RegistrationFormVersion source)
    {
        RegistrationFormTemplate template = RegistrationFormTemplate.Create(
            tenantId, "Deleted", "Deleted", "Registration", null, source, Now);
        template.IsDeleted = true;
        return template;
    }

    private static RegistrationForm Graph(Guid tenantId, bool deleted)
    {
        RegistrationForm form = RegistrationForm.Create(tenantId, Guid.CreateVersion7(), "platform.registration",
            Guid.NewGuid().ToString("N"), "Form", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", Now);
        RegistrationFormField field = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1,
            "platform.registration", "email", "Email", RegistrationFieldTypeEnum.Email, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, Now);
        version.AddSection(section);
        version.AddField(section, field);
        version.AddOption(field, RegistrationFormFieldOption.Create(Guid.CreateVersion7(), field, 1, "primary", "Primary", Now));
        new FormSchemaArtifactPublicationService(new FormSchemaArtifactGenerator()).Publish(version, Now.AddMinutes(1));
        form.AddVersion(version);
        form.IsDeleted = deleted;
        version.IsDeleted = deleted;
        section.IsDeleted = deleted;
        field.IsDeleted = deleted;
        field.Options.Single().IsDeleted = deleted;
        return form;
    }

    private static ExploreDbContext CreateModelContext() => new(
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=task72_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention().Options);

    private readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot _databaseRoot = new();

    private ExploreDbContext CreateInMemoryContext(string database) => new(
        TestDbContextOptions.Create<ExploreDbContext>().UseTestInMemoryDatabase(database, _databaseRoot).Options);

    private static async Task AssertCompositeForeignKey(IEntityType entity, Type principal, params string[] properties)
    {
        IForeignKey? foreignKey = entity.GetForeignKeys().SingleOrDefault(candidate =>
            candidate.PrincipalEntityType.ClrType == principal &&
            candidate.Properties.Select(property => property.Name).SequenceEqual(properties));
        await Assert.That(foreignKey).IsNotNull();
        await Assert.That(foreignKey!.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationFormPostgreSqlPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Category("Runtime")]
    public async Task GetFormsAsync_IsOneTenantAndEventBoundedNoTrackingQuery()
    {
        await fixture.ResetAsync();
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            FormScope tenantA = await SeedEvent(seed, "workflow-graph-a");
            FormScope tenantB = await SeedEvent(seed, "workflow-graph-b");
            seed.RegistrationForms.AddRange(CreateGraph(tenantA), CreateGraph(tenantB));
            await seed.SaveChangesAsync();

            var counter = new CommandCountingInterceptor();
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                .AddInterceptors(counter)
                .Options;
            await using var context = new ExploreDbContext(options)
            {
                TenantContext = new TestTenantContext(tenantA.TenantId)
            };

            IReadOnlyList<RegistrationForm> forms = await new RegistrationFormAuthoringRepository(context)
                .GetFormsAsync(tenantA.EventId, CancellationToken.None);

            await Assert.That(counter.ReaderCommandCount).IsEqualTo(1);
            await Assert.That(forms).HasSingleItem();
            await Assert.That(forms.Single().EventId).IsEqualTo(tenantA.EventId);
            await Assert.That(forms.Single().Versions).HasSingleItem();
            await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
        }
    }

    [Test]
    [Category("Runtime")]
    public async Task PostgreSqlAppliesGraphConstraintsFiltersAndPublishedConcurrency()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        FormScope tenantA = await SeedEvent(context, "task72-a");
        FormScope tenantB = await SeedEvent(context, "task72-b");
        RegistrationForm form = CreateGraph(tenantA);
        context.RegistrationForms.Add(form);
        await context.SaveChangesAsync();

        await using (ExploreDbContext filtered = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId)))
        {
            RegistrationFormField persistedField = await filtered.RegistrationFormFields.AsNoTracking().SingleAsync();
            await Assert.That(persistedField.ConsentPurposeCode).IsEqualTo("REGISTRATION.CONTACT");
            await Assert.That(persistedField.ConsentText).IsEqualTo("I agree to share my registration contact details.");
            await Assert.That(persistedField.ConsentTextVersion).IsEqualTo("v1");
        }

        await using (ExploreDbContext filtered = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.TenantId)))
        {
            await Assert.That(await filtered.RegistrationFormFields.CountAsync()).IsEqualTo(0);
        }

        RegistrationFormVersion version = form.Versions.Single();
        RegistrationFormSection section = version.Sections.Single();
        await using (ExploreDbContext duplicateOrdinal = fixture.CreateDbContext())
        {
            duplicateOrdinal.RegistrationFormSections.Add(
                RegistrationFormSection.Create(Guid.CreateVersion7(), version, section.Ordinal, "Duplicate", DateTime.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(async () => await duplicateOrdinal.SaveChangesAsync());
        }

        RegistrationFormSection secondSection = RegistrationFormSection.Create(
            Guid.CreateVersion7(), version, 2, "Second", DateTime.UtcNow);
        await using (ExploreDbContext secondSectionContext = fixture.CreateDbContext())
        {
            secondSectionContext.RegistrationFormSections.Add(secondSection);
            await secondSectionContext.SaveChangesAsync();
        }

        await using (ExploreDbContext duplicateKey = fixture.CreateDbContext())
        {
            duplicateKey.RegistrationFormFields.Add(RegistrationFormField.Create(
                Guid.CreateVersion7(), secondSection, 1, "platform.registration", "email", "Duplicate email",
                RegistrationFieldTypeEnum.Email, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
                false, true, DateTime.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(async () => await duplicateKey.SaveChangesAsync());
        }

        new FormSchemaArtifactPublicationService(new FormSchemaArtifactGenerator()).Publish(version, DateTime.UtcNow);
        await context.SaveChangesAsync();
        await using (ExploreDbContext pinnedArtifacts = fixture.CreateDbContext())
        {
            RegistrationFormVersion pinned = await pinnedArtifacts.RegistrationFormVersions
                .AsNoTracking().SingleAsync(row => row.Id == version.Id);
            await Assert.That(pinned.SchemaHash).Matches("^[0-9a-f]{64}$");
            FormSchemaArtifactBundle expected = new FormSchemaArtifactGenerator().Generate(version);
            await Assert.That(pinned.DataSchemaArtifact).IsEqualTo(expected.DataSchemaJson);
            await Assert.That(pinned.UiSchemaArtifact).IsEqualTo(expected.UiSchemaJson);
            await Assert.That(pinned.LogicSchemaArtifact).IsEqualTo(expected.LogicSchemaJson);
            await Assert.That(pinned.MappingArtifact).IsEqualTo(expected.MappingArtifactJson);
        }
        Guid staleStamp = version.ConcurrencyStamp;

        await using ExploreDbContext competing = fixture.CreateDbContext();
        RegistrationFormVersion competingVersion = await competing.RegistrationFormVersions.SingleAsync(row => row.Id == version.Id);
        competingVersion.ConcurrencyStamp = Guid.CreateVersion7();
        await competing.SaveChangesAsync();
        version.ConcurrencyStamp = staleStamp;
        version.Retire(DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        RegistrationForm crossTenant = RegistrationForm.Create(tenantA.TenantId, tenantB.EventId,
            "platform.registration", "cross", "Cross", DateTime.UtcNow);
        context.RegistrationForms.Add(crossTenant);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    private static RegistrationForm CreateGraph(FormScope scope)
    {
        DateTime now = DateTime.UtcNow;
        RegistrationForm form = RegistrationForm.Create(scope.TenantId, scope.EventId,
            "platform.registration", "attendee", "Attendee", now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", now);
        RegistrationFormField field = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1,
            "platform.registration", "email", "Email", RegistrationFieldTypeEnum.Email, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, true, true, now,
            "registration.contact", "v1", "I agree to share my registration contact details.");
        version.AddSection(section);
        version.AddField(section, field);
        version.AddOption(field, RegistrationFormFieldOption.Create(Guid.CreateVersion7(), field, 1, "primary", "Primary", now));
        form.AddVersion(version);
        return form;
    }

    private static async Task<FormScope> SeedEvent(ExploreDbContext context, string slug)
    {
        Tenant tenant = new() { FullName = slug, Slug = $"{slug}-{Guid.NewGuid():N}", TenantStatusId = 2, TenantStatus = null! };
        User user = new() { Pii = new UserPii { Email = $"{Guid.NewGuid():N}@example.com", FirstName = "Form", LastName = "Owner" } };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        Actor actor = new() { Pii = new ActorPii { DisplayName = slug }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        Explore.Domain.Event @event = new(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = slug,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = 1,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();
        return new FormScope(tenant.Id, @event.Id);
    }

    private sealed record FormScope(Guid TenantId, Guid EventId);
    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}
