namespace SpaceTravel.Api.Domain;

/// <summary>
/// A destination and its space dock (1:1 per the brief, so the dock is not a
/// separate entity — it is the planet's role as an origin).
/// </summary>
public sealed class Planet
{
    public int Id { get; private set; }
    public string Name { get; private set; }

    /// <summary>1 = closest to the academy. The only distance data the brief gives.</summary>
    public int DistanceRank { get; private set; }

    private Planet(int id, string name, int distanceRank)
    {
        Id = id;
        Name = name;
        DistanceRank = distanceRank;
    }

    /// <summary>EF materialisation only.</summary>
    private Planet()
    {
        Name = string.Empty;
    }

    /// <param name="id">
    /// Leave 0 in production so the database assigns it. Tests pass an explicit
    /// id to build a fleet without touching a database.
    /// </param>
    public static Planet Create(string name, int distanceRank, int id = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A planet must have a name.", nameof(name));
        }

        if (distanceRank <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distanceRank), distanceRank, "Distance rank starts at 1.");
        }

        return new Planet(id, name.Trim(), distanceRank);
    }

    /// <summary>
    /// Ordinal distance between two planets, used as the fuel/duration proxy
    /// (BRD assumption #7 — no numeric distances exist).
    /// </summary>
    public int DistanceTo(Planet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Math.Abs(DistanceRank - other.DistanceRank);
    }
}
