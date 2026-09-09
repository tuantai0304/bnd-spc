interface CapacityBarProps {
  label: string;
  used: number;
  /** Derived as used + remaining, never hardcoded — the caps are config. */
  capacity: number;
  unit?: string;
}

const round = (n: number) => Math.round(n * 100) / 100;

/**
 * Capacity is dual: a shuttle is full when it hits *either* the life-form cap
 * or the weight cap, so both bars are always shown side by side.
 */
export function CapacityBar({ label, used, capacity, unit = '' }: CapacityBarProps) {
  const pct = capacity > 0 ? Math.min(100, (used / capacity) * 100) : 0;
  const full = capacity > 0 && used >= capacity;

  return (
    <div>
      <div className="flex items-baseline justify-between text-xs">
        <span className="text-muted">{label}</span>
        <span className="tnum text-ink">
          {round(used)} / {round(capacity)}
          {unit}
        </span>
      </div>
      <div
        className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-raised"
        role="progressbar"
        aria-label={label}
        aria-valuenow={round(used)}
        aria-valuemin={0}
        aria-valuemax={round(capacity)}
      >
        <div
          className={`h-full rounded-full transition-[width] duration-500 ${
            full ? 'bg-rejected' : 'bg-enroute'
          }`}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  );
}
