namespace SpaceTravel.Api.Code.Dispatch;

/// <summary>
/// Serialises everything that reads-then-writes the fleet, so two simultaneous
/// calls can never both claim the last seat on a shuttle (BRD edge case #4).
/// </summary>
/// <remarks>
/// This is an in-process lock: correct for a single-process monolith, and the
/// honest limit on horizontal scale-out. It exists as an interface precisely so
/// it can be swapped for a distributed lock without touching Domain or Features.
/// SQLite is a single-writer store anyway, so nothing is lost today.
/// </remarks>
public interface IDispatchGate
{
    Task<IDisposable> AcquireAsync(CancellationToken ct = default);
}

public sealed class DispatchGate : IDispatchGate, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        return new Release(_gate);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class Release : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _released;

        public Release(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _gate.Release();
        }
    }
}
