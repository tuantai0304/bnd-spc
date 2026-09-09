using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Data.Configurations;

public sealed class PlanetConfiguration : IEntityTypeConfiguration<Planet>
{
    public void Configure(EntityTypeBuilder<Planet> builder)
    {
        builder.ToTable("Planets");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.DistanceRank)
            .IsRequired();

        // Two planets cannot share a name, nor a position in the distance order.
        builder.HasIndex(p => p.Name).IsUnique();
        builder.HasIndex(p => p.DistanceRank).IsUnique();
    }
}
