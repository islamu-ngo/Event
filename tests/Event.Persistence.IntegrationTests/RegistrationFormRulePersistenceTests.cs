// ABOUTME: Verifies typed registration-form rule mapping and PostgreSQL enforcement.
// ABOUTME: Covers AST round-trip, tenant filters, composite ownership, ordinal uniqueness, and cleanup.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationFormRulePersistenceTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task EfModelMapsTypedRuleWithTenantSafeVersionOwnership()
    {
        await using ExploreDbContext context = new(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql("Host=localhost;Database=task73_model;Username=unused;Password=unused")
                .UseSnakeCaseNamingConvention().Options);
        IEntityType entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RegistrationFormRule))!;
        IProperty condition = entity.FindProperty(nameof(RegistrationFormRule.Condition))!;
        IForeignKey versionForeignKey = entity.GetForeignKeys().Single(candidate =>
            candidate.PrincipalEntityType.ClrType == typeof(RegistrationFormVersion));

        await Assert.That(condition.FindAnnotation(RelationalAnnotationNames.ColumnType)).IsNull();
        await Assert.That(condition.GetValueConverter()).IsNotNull();
        await Assert.That(condition.GetValueComparer()).IsNotNull();
        await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(versionForeignKey.Properties.Select(property => property.Name)).IsEquivalentTo(
            ["TenantId", "EventId", "RegistrationFormId", "RegistrationFormVersionId"]);
        await Assert.That(versionForeignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Restrict);
        await Assert.That(entity.GetIndexes().Any(index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                ["TenantId", "EventId", "RegistrationFormVersionId", "Ordinal"]))).IsTrue();
        await Assert.That(entity.GetCheckConstraints().Select(constraint => constraint.Name)).Contains(
            "ck_registration_form_rules_ordinal_positive");
        await Assert.That(entity.GetCheckConstraints().Select(constraint => constraint.Name)).Contains(
            "ck_registration_form_rules_effect");
    }

    [Test]
    public async Task ConditionConverterRoundTripsNestedTypedAst()
    {
        await using ExploreDbContext context = new(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql("Host=localhost;Database=task73_converter;Username=unused;Password=unused")
                .UseSnakeCaseNamingConvention().Options);
        IProperty property = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(RegistrationFormRule))!
            .FindProperty(nameof(RegistrationFormRule.Condition))!;
        FormFieldReference field = new("platform.registration", "age");
        FormCondition source = new FormCondition.AllCondition([
            new FormCondition.ExistsCondition(field),
            new FormCondition.NotCondition(new FormCondition.CompareCondition(
                field, FormComparisonKind.LessThan, FormScalarValue.From(18m)))
        ]);

        string json = (string)property.GetValueConverter()!.ConvertToProvider(source)!;
        FormCondition roundTrip = (FormCondition)property.GetValueConverter()!.ConvertFromProvider(json)!;
        string reorderedJson = json.Replace("{\"operator\":\"all\",", "{");
        reorderedJson = $"{reorderedJson[..^1]},\"operator\":\"all\"}}";
        FormCondition reorderedRoundTrip = (FormCondition)property.GetValueConverter()!
            .ConvertFromProvider(reorderedJson)!;
        IReadOnlyDictionary<FormFieldReference, FormAnswerValue> answers =
            new Dictionary<FormFieldReference, FormAnswerValue> { [field] = FormAnswerValue.From(21m) };

        await Assert.That(json).Contains("\"operator\":\"all\"");
        await Assert.That(json).Contains("\"operator\":\"compare\"");
        await Assert.That(FormConditionEvaluator.Evaluate(roundTrip, answers)).IsTrue();
        await Assert.That(FormConditionEvaluator.Evaluate(reorderedRoundTrip, answers)).IsTrue();
    }

    internal static RegistrationForm CreateGraph(Guid tenantId, Guid eventId, string key)
    {
        RegistrationForm form = RegistrationForm.Create(
            tenantId, eventId, "platform.registration", key, "Attendee", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", Now);
        version.AddSection(section);
        RegistrationFormField earlier = Field(section, 1, "country");
        RegistrationFormField target = Field(section, 2, "age");
        version.AddField(section, earlier);
        version.AddField(section, target);
        version.AddRule(RegistrationFormRule.Create(Guid.CreateVersion7(), version, 1,
            new FormFieldReference(target.Namespace, target.Key), RegistrationFormRuleEffect.Require,
            new FormCondition.EqualsCondition(
                new FormFieldReference(earlier.Namespace, earlier.Key), FormScalarValue.From("BE")), Now));
        form.AddVersion(version);
        return form;
    }

    private static RegistrationFormField Field(RegistrationFormSection section, int ordinal, string key) =>
        RegistrationFormField.Create(Guid.CreateVersion7(), section, ordinal, "platform.registration", key, key,
            RegistrationFieldTypeEnum.ShortText, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, Now);
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationFormRulePostgreSqlTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Category("Runtime")]
    public async Task PostgreSqlAppliesMigrationRoundTripsRulesAndEnforcesTenantAndOrdinalBoundaries()
    {
        await fixture.ResetAsync();
        try
        {
            await using ExploreDbContext context = fixture.CreateDbContext();
            FormScope tenantA = await SeedEvent(context, "task73-a");
            FormScope tenantB = await SeedEvent(context, "task73-b");
            RegistrationForm form = RegistrationFormRulePersistenceTests.CreateGraph(
                tenantA.TenantId, tenantA.EventId, "task73");
            context.RegistrationForms.Add(form);
            await context.SaveChangesAsync();

            await using (ExploreDbContext filtered = fixture.CreateTenantFilteredDbContext(
                new TestTenantContext(tenantA.TenantId)))
            {
                RegistrationFormRule persisted = await filtered.RegistrationFormRules.AsNoTracking().SingleAsync();
                await Assert.That(persisted.Condition).IsTypeOf<FormCondition.EqualsCondition>();
                await Assert.That(persisted.Target).IsEqualTo(new FormFieldReference("platform.registration", "age"));
            }

            await using (ExploreDbContext filtered = fixture.CreateTenantFilteredDbContext(
                new TestTenantContext(tenantB.TenantId)))
            {
                await Assert.That(await filtered.RegistrationFormRules.CountAsync()).IsEqualTo(0);
            }

            RegistrationFormVersion version = form.Versions.Single();
            RegistrationFormRule source = version.Rules.Single();
            await using (ExploreDbContext duplicate = fixture.CreateDbContext())
            {
                duplicate.RegistrationFormRules.Add(RegistrationFormRule.Create(
                    Guid.CreateVersion7(), version, source.Ordinal, source.Target, source.Effect,
                    source.Condition, DateTime.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(async () => await duplicate.SaveChangesAsync());
            }

            await using (ExploreDbContext crossTenant = fixture.CreateDbContext())
            {
                RegistrationFormRule invalid = RegistrationFormRule.Create(
                    Guid.CreateVersion7(), version, 2, source.Target, source.Effect, source.Condition, DateTime.UtcNow);
                invalid.TenantId = tenantB.TenantId;
                crossTenant.RegistrationFormRules.Add(invalid);
                await Assert.ThrowsAsync<DbUpdateException>(async () => await crossTenant.SaveChangesAsync());
            }
        }
        finally
        {
            await fixture.ResetAsync();
        }
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
}
