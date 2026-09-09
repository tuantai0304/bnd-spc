using SpaceTravel.Api.Domain;

namespace SpaceTravel.Tests.Domain;

/// <summary>
/// The brief's "20 life forms OR maximum load of 4000kg". "Whichever is reached
/// first" means a load only fits if it clears BOTH ceilings.
/// </summary>
public class CapacityTests
{
    private static readonly Capacity Standard = new(maxLifeForms: 20, maxWeightKg: 4000m);

    [Theory]
    [InlineData(1, 80, true)]
    [InlineData(20, 4000, true)]      // exactly on both limits still fits
    [InlineData(21, 100, false)]      // one seat over
    [InlineData(2, 4001, false)]      // one kilo over
    [InlineData(21, 4001, false)]     // over on both
    public void Accommodates_requires_both_ceilings(int count, decimal weight, bool expected)
        => Assert.Equal(expected, Standard.Accommodates(count, weight));

    /// <summary>BRD edge case #3, stated exactly as written.</summary>
    [Fact]
    public void Seats_free_does_not_mean_weight_free()
    {
        // 4 life forms totalling 3900kg: 16 seats spare but only 100kg spare.
        const int aboard = 4;
        const decimal weightAboard = 3900m;

        Assert.Equal(16, Standard.RemainingLifeForms(aboard));
        Assert.Equal(100m, Standard.RemainingWeightKg(weightAboard));

        // A 150kg fifth passenger fits the seat count and busts the weight cap.
        Assert.False(Standard.Accommodates(aboard + 1, weightAboard + 150m));

        // A 90kg one fits both.
        Assert.True(Standard.Accommodates(aboard + 1, weightAboard + 90m));
    }

    [Theory]
    [InlineData(0, 4000)]
    [InlineData(20, 0)]
    [InlineData(-1, 4000)]
    public void A_shuttle_cannot_be_built_with_a_useless_capacity(int maxLifeForms, decimal maxWeightKg)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Capacity(maxLifeForms, maxWeightKg));
}
