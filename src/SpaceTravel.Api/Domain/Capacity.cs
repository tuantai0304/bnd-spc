namespace SpaceTravel.Api.Domain;

/// <summary>
/// The shuttle's dual ceiling: "20 life forms OR maximum load of 4000kg".
/// The brief's "OR" means whichever is reached first, so a shuttle is full when
/// EITHER cap is hit — which makes fitting a party a check against BOTH.
/// </summary>
public sealed record Capacity
{
    public int MaxLifeForms { get; }
    public decimal MaxWeightKg { get; }

    public Capacity(int maxLifeForms, decimal maxWeightKg)
    {
        if (maxLifeForms <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLifeForms), maxLifeForms, "Capacity must allow at least one life form.");
        }

        if (maxWeightKg <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxWeightKg), maxWeightKg, "Capacity must allow a positive weight.");
        }

        MaxLifeForms = maxLifeForms;
        MaxWeightKg = maxWeightKg;
    }

    /// <summary>
    /// True only when the load fits within both ceilings. This is the joint check
    /// that BRD edge case #3 calls for: 4 life forms at 3900kg leaves 16 free seats
    /// but only 100kg, so a 150kg fifth passenger does not fit.
    /// </summary>
    public bool Accommodates(int lifeFormCount, decimal totalWeightKg)
        => lifeFormCount <= MaxLifeForms && totalWeightKg <= MaxWeightKg;

    public int RemainingLifeForms(int usedLifeForms)
        => Math.Max(0, MaxLifeForms - usedLifeForms);

    public decimal RemainingWeightKg(decimal usedWeightKg)
        => Math.Max(0m, MaxWeightKg - usedWeightKg);
}
