// The aspect's Id is both its PK and the FK to Event.Id (1:1 relationship).

namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class EventTechAspectConfiguration : IEntityTypeConfiguration<EventTechAspect>
{
    public void Configure(EntityTypeBuilder<EventTechAspect> builder)
    {
        // Primary key - shared with Event (no UUID v7 generation here, uses Event.Id)
        builder.HasKey(e => e.Id);

        // 1:1 relationship with Event using shared primary key pattern
        builder.HasOne(e => e.Event)
            .WithOne(e => e.TechAspect)
            .HasForeignKey<EventTechAspect>(e => e.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // GitHub repo URL
        builder.Property(e => e.GithubRepoUrl)
            .HasMaxLength(2048);

        // Hackathon track
        builder.Property(e => e.HackathonTrack)
            .HasMaxLength(200);

        // Skill level
        builder.Property(e => e.SkillLevel)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(SkillLevel.AllLevels);

        // Tech stack tags (comma-separated)
        builder.Property(e => e.TechStackTags)
            .HasMaxLength(1000);

        // Boolean defaults
        builder.Property(e => e.RequiresLaptop)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.IsCodingCompetition)
            .IsRequired()
            .HasDefaultValue(false);

        // Prize configuration
        builder.Property(e => e.PrizePool)
            .HasPrecision(19, 4);

        builder.Property(e => e.PrizeCurrencyCode)
            .HasMaxLength(3);
    }
}
