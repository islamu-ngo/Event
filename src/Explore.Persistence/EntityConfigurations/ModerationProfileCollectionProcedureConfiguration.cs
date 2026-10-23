using System.Text.Json;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationProfileCollectionProcedureSourceConfiguration
    : IEntityTypeConfiguration<ModerationProfileCollectionProcedureSource>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ModerationProfileCollectionProcedureSource> builder)
    {
        builder.ToTable("moderation_profile_collection_procedure_sources", table =>
        {
            table.HasCheckConstraint("ck_profile_collection_procedure_generation",
                "revision = 1 AND policy_revision > 0 AND child_age_source_revision = 1 " +
                "AND child_age_source_writer_generation > 0 AND child_age_adoption_revision = 1 " +
                "AND child_age_adoption_writer_generation > child_age_source_writer_generation " +
                "AND writer_generation > child_age_adoption_writer_generation");
            table.HasCheckConstraint("ck_profile_collection_procedure_mode", "mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_profile_collection_procedure_validity",
                "valid_until > recorded_at AND recorded_at >= child_age_recorded_at " +
                "AND recorded_at >= child_age_adopted_at AND valid_until <= child_age_valid_until " +
                "AND valid_until <= child_age_adoption_valid_until");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        // Immutable, bounded policy snapshots contain no individual evidence or PII values.
        // These owner columns preserve the original proposal, not a live parent navigation.
        builder.Property(row => row.Procedure).HasConversion(
            value => JsonSerializer.Serialize(value, Json),
            value => JsonSerializer.Deserialize<ModerationProfileCollectionProcedure>(value, Json)!)
            .IsRequired();
        builder.Property(row => row.ChildAgeTerms).HasConversion(
            value => JsonSerializer.Serialize(value, Json),
            value => JsonSerializer.Deserialize<ModerationChildAgePolicyTerms>(value, Json)!)
            .IsRequired();
        builder.Property(row => row.ChildAgeOwnership).HasConversion(
            value => JsonSerializer.Serialize(value, Json),
            value => JsonSerializer.Deserialize<ModerationChildAgePolicyOwnership>(value, Json)!)
            .IsRequired();
        builder.Ignore(row => row.OwnerHumanId);
        builder.Ignore(row => row.OwnerControllerHumanId);
        builder.Ignore(row => row.ReviewerHumanId);
        builder.Ignore(row => row.ReviewerControllerHumanId);
        builder.HasIndex(row => new
            { row.HostBindingId, row.TenantId, row.PublicationId, row.ProfileId, row.ModeCode, row.WriterGeneration })
            .IsUnique();
        builder.HasOne<ModerationChildAgePolicySource>().WithMany()
            .HasForeignKey(row => row.ChildAgeSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationChildAgePolicyAdoption>().WithMany()
            .HasForeignKey(row => row.ChildAgeAdoptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId)
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationProfileCollectionProcedureAdoptionConfiguration
    : IEntityTypeConfiguration<ModerationProfileCollectionProcedureAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationProfileCollectionProcedureAdoption> builder)
    {
        builder.ToTable("moderation_profile_collection_procedure_adoptions", table =>
        {
            table.HasCheckConstraint("ck_profile_collection_adoption_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_profile_collection_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_profile_collection_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne(row => row.Source).WithMany().HasForeignKey(row => row.SourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(row => row.Source).AutoInclude();
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId)
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationProfileCollectionProcedureAdoption.Revision)
                or nameof(ModerationProfileCollectionProcedureAdoption.WithdrawnAt)
                or nameof(ModerationProfileCollectionProcedureAdoption.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
