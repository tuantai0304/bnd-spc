using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Data.Configurations;

public sealed class TravelRequestConfiguration : IEntityTypeConfiguration<TravelRequest>
{
    public void Configure(EntityTypeBuilder<TravelRequest> builder)
    {
        builder.ToTable("TravelRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RequestedAtUtc).IsRequired();
        builder.Property(r => r.CompletedAtUtc);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne<Planet>()
            .WithMany()
            .HasForeignKey(r => r.OriginPlanetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Planet>()
            .WithMany()
            .HasForeignKey(r => r.DestinationPlanetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Which shuttle took this call. Set on assignment and never cleared, so a
        // completed trip keeps saying who flew it.
        builder.HasOne<Shuttle>()
            .WithMany()
            .HasForeignKey(r => r.AssignedShuttleId)
            .OnDelete(DeleteBehavior.Restrict);

        // The party. Owned, so life forms have no identity of their own and are
        // loaded, saved and deleted with the request that carries them.
        builder.OwnsMany(r => r.LifeForms, lifeForm =>
        {
            lifeForm.ToTable("LifeForms");
            lifeForm.WithOwner().HasForeignKey("TravelRequestId");
            lifeForm.Property<int>("Id");
            lifeForm.HasKey("Id");

            lifeForm.Property(lf => lf.Species)
                .IsRequired()
                .HasMaxLength(100);

            // Stored as REAL — see the note in ShuttleConfiguration.
            lifeForm.Property(lf => lf.WeightKg)
                .HasConversion<double>()
                .IsRequired();
        });

        builder.Metadata
            .FindNavigation(nameof(TravelRequest.LifeForms))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // The tick reloads every non-terminal request on every pass.
        builder.HasIndex(r => r.Status);
    }
}
