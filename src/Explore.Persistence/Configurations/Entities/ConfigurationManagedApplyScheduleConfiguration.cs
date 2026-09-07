namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ConfigurationManagedApplyScheduleConfiguration
    : IEntityTypeConfiguration<ConfigurationManagedApplySchedule>
{
    public void Configure(
        EntityTypeBuilder<ConfigurationManagedApplySchedule> builder)
    {
        builder.ToTable("configuration_managed_apply_schedules");
        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.TargetAuthorityKey)
            .HasMaxLength(200)
            .IsRequired();
        Digest(builder.Property(schedule => schedule.ArtifactDigest));
        Digest(builder.Property(schedule => schedule.TargetRevisionDigest));
        Digest(builder.Property(schedule => schedule.ManagedPlanDigest));
        builder.Property(schedule => schedule.UploadedBy).IsRequired();
        builder.Property(schedule => schedule.ReviewedBy);
        builder.Property(schedule => schedule.AppliedBy);
        Utc(builder.Property(schedule => schedule.ApplyNotBefore), required: true);
        Utc(builder.Property(schedule => schedule.ApplyBefore), required: true);
        builder.Property(schedule => schedule.Status).HasConversion<int>().IsRequired();
        Utc(builder.Property(schedule => schedule.CreatedAt), required: true);
        Utc(builder.Property(schedule => schedule.CompletedAt));
        builder.Property(schedule => schedule.Revision)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(schedule => new
        {
            schedule.TargetAuthorityKey,
            schedule.Status,
            schedule.ApplyNotBefore
        });
    }

    private static void Digest(PropertyBuilder<string> property) =>
        property.HasMaxLength(64).IsFixedLength().IsRequired();

    private static void Utc(
        PropertyBuilder<DateTime> property,
        bool required)
    {
        property.HasConversion(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        if (required)
            property.IsRequired();
    }

    private static void Utc(PropertyBuilder<DateTime?> property) =>
        property.HasConversion(
            value => value,
            value => value.HasValue
                ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                : null);
}
