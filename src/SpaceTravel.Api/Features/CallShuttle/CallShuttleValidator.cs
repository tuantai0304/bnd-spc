using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SpaceTravel.Api.Data;

namespace SpaceTravel.Api.Features.CallShuttle;

/// <summary>
/// Structural validation only — is this a well-formed call? Whether a shuttle can
/// actually carry the party is a business decision and belongs to the Fleet, not here.
/// </summary>
public sealed class CallShuttleValidator : AbstractValidator<CallShuttleRequest>
{
    public CallShuttleValidator(SpaceTravelDbContext db)
    {
        RuleFor(r => r.OriginPlanetId)
            .MustAsync(async (id, ct) => await db.Planets.AnyAsync(p => p.Id == id, ct))
            .WithMessage("Origin must be one of the known planets.");

        RuleFor(r => r.DestinationPlanetId)
            .MustAsync(async (id, ct) => await db.Planets.AnyAsync(p => p.Id == id, ct))
            .WithMessage("Destination must be one of the known planets.");

        // BRD business rule 4 / assumption #11.
        RuleFor(r => r.DestinationPlanetId)
            .NotEqual(r => r.OriginPlanetId)
            .WithMessage("Destination must differ from the planet you are calling from.");

        RuleFor(r => r.LifeForms)
            .NotNull()
            .Must(lifeForms => lifeForms is { Count: > 0 })
            .WithMessage("A call must include at least one life form.");

        // BRD edge case #5.
        RuleForEach(r => r.LifeForms)
            .ChildRules(lifeForm =>
            {
                lifeForm.RuleFor(lf => lf.Species)
                    .NotEmpty()
                    .MaximumLength(100)
                    .WithMessage("Every life form needs a species.");

                lifeForm.RuleFor(lf => lf.WeightKg)
                    .GreaterThan(0)
                    .WithMessage("Every life form must weigh more than zero kilograms.");
            })
            .When(r => r.LifeForms is { Count: > 0 });
    }
}
