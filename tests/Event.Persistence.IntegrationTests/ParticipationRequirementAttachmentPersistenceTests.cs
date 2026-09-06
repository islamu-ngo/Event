// ABOUTME: Verifies the EF boundary for participation requirement attachments.
// ABOUTME: Pins tenant/event lineage, active uniqueness, standalone uniqueness, and concurrency metadata.

using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests;

public sealed class ParticipationRequirementAttachmentPersistenceTests
{
    [Test]
    public async Task EfModelEnforcesAttachmentLineageAndActiveUniqueness()
    {
        await using var context = new ExploreDbContext(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql("Host=localhost;Database=task77_model;Username=unused;Password=unused")
                .UseSnakeCaseNamingConvention().Options);
        IEntityType entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(ParticipationRequirementAttachment))!;

        await Assert.That(entity).IsNotNull();
        await Assert.That(entity.GetTableName()).IsEqualTo("participation_requirement_attachments");
        await Assert.That(entity.FindProperty(nameof(ParticipationRequirementAttachment.ConcurrencyStamp))!
            .IsConcurrencyToken).IsTrue();
        await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
        await Assert.That(entity.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(EventParticipationConfiguration) &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(
                ["TenantId", "ParticipationConfigurationId"]))).IsTrue();
        await Assert.That(entity.GetCheckConstraints().Any(constraint =>
            constraint.Name == "ck_participation_requirement_attachments_configuration_event")).IsTrue();
        await Assert.That(entity.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(RegistrationRequirement) &&
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(
                ["TenantId", "EventId", "RegistrationWorkflowId", "RegistrationRequirementId"]))).IsTrue();
        await Assert.That(entity.GetIndexes().Any(index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                ["ParticipationConfigurationId", "RegistrationRequirementId"]) &&
            index.GetFilter() == "is_deleted = false")).IsTrue();
        await Assert.That(entity.GetIndexes().Any(index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
                ["ParticipationConfigurationId", "IsStandaloneQuestionnaire"]) &&
            index.GetFilter() == "is_deleted = false AND is_standalone_questionnaire = true")).IsTrue();
    }
}
