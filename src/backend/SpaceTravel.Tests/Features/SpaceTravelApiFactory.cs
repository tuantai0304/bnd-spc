using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpaceTravel.Api.Code.Simulation;
using SpaceTravel.Api.Domain;

namespace SpaceTravel.Tests.Features;

/// <summary>
/// Boots the real app over its own throwaway SQLite file, with the background
/// tick switched off so tests move the clock themselves and never race.
/// </summary>
public sealed class SpaceTravelApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"spacetravel-api-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SpaceTravel"] = $"Data Source={_dbPath}",
                ["Simulation:Enabled"] = "false"
            }));
    }

    /// <summary>
    /// Runs one simulation pass at the given moment. Passing a time far in the
    /// future lands every in-flight shuttle at once.
    /// </summary>
    public async Task<IReadOnlyList<TravelHistoryEntry>> AdvanceAsync(DateTime now)
    {
        await using var scope = Services.CreateAsyncScope();
        var advancer = scope.ServiceProvider.GetRequiredService<FleetAdvancer>();
        return await advancer.AdvanceAsync(now);
    }

    /// <summary>Loads, launches, and lands everything currently in play.</summary>
    public async Task<IReadOnlyList<TravelHistoryEntry>> RunToCompletionAsync()
    {
        // First pass launches; the second lands them an hour later.
        await AdvanceAsync(DateTime.UtcNow);
        return await AdvanceAsync(DateTime.UtcNow.AddHours(1));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
