using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Api.Data.Configurations;

public sealed class ShuttleConfiguration : IEntityTypeConfiguration<Shuttle>
{
    public void Configure(EntityTypeBuilder<Shuttle> builder)
    {
        builder.ToTable("Shuttles");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        // Stored as text so a migration or a human reading the file sees "EnRoute",
        // not "1" — and so adding a state later cannot silently renumber the others.
        builder.Property(s => s.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.DepartedAtUtc);
        builder.Property(s => s.ArrivesAtUtc);

        // The dual cap travels with the shuttle, so a future shuttle class can
        // carry different limits without a schema change.
        builder.OwnsOne(s => s.Capacity, capacity =>
        {
            capacity.Property(c => c.MaxLifeForms)
                .HasColumnName("MaxLifeForms")
                .IsRequired();

            capacity.Property(c => c.MaxWeightKg)
                .HasColumnName("MaxWeightKg")
                // SQLite has no decimal type and EF would fall back to TEXT, which
                // silently breaks SUM and ORDER BY. REAL keeps weights aggregable;
                // kilograms to 3dp are far inside double's exact range.
                .HasConversion<double>()
                .IsRequired();
        });
        builder.Navigation(s => s.Capacity).IsRequired();

        // Three independent references to Planet: where it is, where it is flying,
        // and the trip destination it is committed to.
        builder.HasOne<Planet>()
            .WithMany()
            .HasForeignKey(s => s.CurrentPlanetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Planet>()
            .WithMany()
            .HasForeignKey(s => s.FlyingToPlanetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Planet>()
            .WithMany()
            .HasForeignKey(s => s.PlannedDestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The manifest is deliberately NOT an EF relationship. If it were, unloading
        // a party would clear the collection and EF would null each request's
        // AssignedShuttleId — erasing which shuttle actually flew them. Instead it is
        // "the active requests pointing at this shuttle", rebuilt by FleetRepository,
        // so AssignedShuttleId survives completion as a permanent record.
        builder.Ignore(s => s.Manifest);
    }
}
