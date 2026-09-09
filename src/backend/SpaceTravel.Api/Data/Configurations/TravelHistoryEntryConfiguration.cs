using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Data.Configurations;

public sealed class TravelHistoryEntryConfiguration : IEntityTypeConfiguration<TravelHistoryEntry>
{
    public void Configure(EntityTypeBuilder<TravelHistoryEntry> builder)
    {
        builder.ToTable("TravelHistory");
        builder.HasKey(h => h.Id);

        // Stored as REAL so the stats slice can SUM it — see ShuttleConfiguration.
        builder.Property(h => h.TotalWeightKg).HasConversion<double>();
        builder.Property(h => h.RejectionReason).HasMaxLength(500);

        builder.Property(h => h.Outcome)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne<Planet>()
            .WithMany()
            .HasForeignKey(h => h.OriginPlanetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Planet>()
            .WithMany()
            .HasForeignKey(h => h.DestinationPlanetId)
            .OnDelete(DeleteBehavior.Restrict);

        // "Most traveled planets" groups by destination; the stats slice lives on this.
        builder.HasIndex(h => h.DestinationPlanetId);
        builder.HasIndex(h => h.RecordedAtUtc);

        // One terminal outcome per call — history cannot double-count a trip.
        builder.HasIndex(h => h.TravelRequestId).IsUnique();
    }
}
