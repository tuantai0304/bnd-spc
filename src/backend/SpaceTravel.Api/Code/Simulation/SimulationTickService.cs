using Microsoft.Extensions.Options;
using SpaceTravel.Api.Code.Dispatch;
using SpaceTravel.Api.Code.Options;
using SpaceTravel.Api.Code.Time;

namespace SpaceTravel.Api.Code.Simulation;

/// <summary>
/// Moves the world forward. Without this, En Route would never become Arrived and
/// queued calls would sit forever — a shuttle's flight time is what makes the
/// dispatcher's choice matter at all.
/// </summary>
public sealed class SimulationTickService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDispatchGate _gate;
    private readonly IClock _clock;
    private readonly SimulationOptions _options;
    private readonly ILogger<SimulationTickService> _logger;

    public SimulationTickService(
        IServiceScopeFactory scopeFactory,
        IDispatchGate gate,
        IClock clock,
        IOptions<SimulationOptions> options,
        ILogger<SimulationTickService> logger)
    {
        _scopeFactory = scopeFactory;
        _gate = gate;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Simulation tick is disabled; shuttles will not move on their own.");
            return;
        }

        _logger.LogInformation(
            "Simulation tick started at {Interval} intervals, {BaseLeg} per distance step.",
            _options.TickInterval, _options.BaseLegDuration);

        using var timer = new PeriodicTimer(_options.TickInterval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad tick must not take the fleet down — the brief asks for
                // resilience, so we log and try again on the next interval.
                _logger.LogError(ex, "Simulation tick failed; retrying next interval.");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // The gate is held for the whole load-decide-save, so a tick and an
        // incoming call can never interleave on the same shuttle.
        using var _ = await _gate.AcquireAsync(ct);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var advancer = scope.ServiceProvider.GetRequiredService<FleetAdvancer>();

        await advancer.AdvanceAsync(_clock.UtcNow, ct);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
