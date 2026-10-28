using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationOperationalBudgetConfiguration :
    IEntityTypeConfiguration<ModerationOperationalBudgetSource>, IEntityTypeConfiguration<ModerationOperationalBudgetAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationOperationalBudgetSource> builder)
    {
        builder.ToTable("moderation_operational_budget_sources");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Ignore(row => row.Terms);
        builder.Property(row => row.RecordedAt).HasConversion(
            instant => instant.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        builder.Property(row => row.ValidUntil).HasConversion(
            instant => instant.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        builder.HasAlternateKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId });
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.SubjectKind, row.Action, row.WriterGeneration }).IsUnique();
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.OperatorHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_operational_budget_source_generation",
                "revision = 1 AND writer_generation > 0 AND writer_generation < 9223372036854775807");
            table.HasCheckConstraint("ck_operational_budget_source_terms",
                "subject_kind = 1 AND action IN (1, 2, 3, 4, 5, 6) AND maximum_occurrences > 0 " +
                "AND window_ticks > 0 AND window_ticks < 9223372036854775807");
            table.HasCheckConstraint("ck_operational_budget_source_validity",
                "recorded_at > 0 AND valid_until > recorded_at AND valid_until < 3155378975999999999 " +
                "AND window_ticks < recorded_at AND window_ticks < 3155378975999999999 - recorded_at");
            table.HasCheckConstraint("ck_operational_budget_source_independence",
                "operator_human_id <> recorded_by_human_id");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationOperationalBudgetAdoption> builder)
    {
        builder.ToTable("moderation_operational_budget_adoptions");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.AdoptedAt).HasConversion(
            instant => instant.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        builder.Property(row => row.ValidUntil).HasConversion(
            instant => instant.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        builder.Property(row => row.WithdrawnAt).HasConversion(
            instant => instant.HasValue ? instant.Value.UtcTicks : (long?)null,
            ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasAlternateKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId, row.SourceId });
        builder.HasOne<ModerationOperationalBudgetSource>().WithMany()
            .HasForeignKey(row => new { row.SourceId, row.HostBindingId, row.TrustDomainId, row.TenantId })
            .HasPrincipalKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_operational_budget_adoption_generation",
                "id <> source_id AND source_revision = 1 AND source_writer_generation > 0 " +
                "AND source_writer_generation < 9223372036854775807 AND writer_generation > source_writer_generation " +
                "AND writer_generation < 9223372036854775807");
            table.HasCheckConstraint("ck_operational_budget_adoption_validity",
                "adopted_at > 0 AND valid_until > adopted_at AND valid_until < 3155378975999999999");
            table.HasCheckConstraint("ck_operational_budget_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= adopted_at " +
                "AND withdrawn_at < 3155378975999999999 AND withdrawn_by_human_id IS NOT NULL)");
        });
    }
}
