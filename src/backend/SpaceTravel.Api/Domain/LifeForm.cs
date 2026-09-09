namespace SpaceTravel.Api.Domain;

/// <summary>
/// One traveller. The brief is explicit that this is "not just for the human
/// race", so species is free text — a label for display, with no effect on the
/// capacity maths (BRD assumption #4).
/// </summary>
public sealed class LifeForm
{
    public const decimal MaxPlausibleWeightKg = 1_000_000m;

    public string Species { get; private set; }
    public decimal WeightKg { get; private set; }

    private LifeForm(string species, decimal weightKg)
    {
        Species = species;
        WeightKg = weightKg;
    }

    /// <summary>EF materialisation only.</summary>
    private LifeForm()
    {
        Species = string.Empty;
    }

    public static LifeForm Create(string species, decimal weightKg)
    {
        if (string.IsNullOrWhiteSpace(species))
        {
            throw new ArgumentException("A life form must have a species.", nameof(species));
        }

        if (weightKg <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weightKg), weightKg, "A life form must weigh more than zero.");
        }

        if (weightKg > MaxPlausibleWeightKg)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weightKg), weightKg, "Weight exceeds any plausible life form.");
        }

        return new LifeForm(species.Trim(), weightKg);
    }
}
