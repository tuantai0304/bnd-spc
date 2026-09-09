import { useEffect, useMemo, useState } from 'react';
import { Link, createFileRoute, useNavigate } from '@tanstack/react-router';
import { ApiError, type FieldErrors } from '../api/client';
import { callShuttle } from '../api/endpoints';
import { ErrorBanner } from '../components/ErrorBanner';
import { FieldError, fieldErrorProps } from '../components/FieldError';
import { usePlanetsStore } from '../stores/usePlanetsStore';
import { useFleetStore } from '../stores/useFleetStore';
import type { CallShuttleResult } from '../api/types';

export const Route = createFileRoute('/call')({ component: CallShuttle });

/** A row of the form. Weight is a string so the field can legitimately be blank. */
interface LifeFormRow {
  species: string;
  weightKg: string;
}

const emptyRow = (): LifeFormRow => ({ species: '', weightKg: '' });

/**
 * The server's rules, mirrored exactly — messages included, copied verbatim
 * from CallShuttleValidator so the client and server can never disagree about
 * what a rule says. Keys match what normalizeFieldKey produces from the
 * server's PascalCase paths, so both sources land under the same input.
 *
 * Capacity is deliberately absent: whether a party fits is a *business*
 * decision made by the Fleet aggregate, which is why an unfittable party comes
 * back 201 + outcome "Rejected" rather than 400.
 */
function validate(
  originPlanetId: number | '',
  destinationPlanetId: number | '',
  rows: LifeFormRow[],
  planetIds: Set<number>,
): FieldErrors {
  const errors: FieldErrors = {};
  const push = (key: string, message: string) => {
    (errors[key] ??= []).push(message);
  };

  if (originPlanetId === '' || !planetIds.has(originPlanetId)) {
    push('originPlanetId', 'Origin must be one of the known planets.');
  }
  if (destinationPlanetId === '' || !planetIds.has(destinationPlanetId)) {
    push('destinationPlanetId', 'Destination must be one of the known planets.');
  }
  if (
    originPlanetId !== '' &&
    destinationPlanetId !== '' &&
    originPlanetId === destinationPlanetId
  ) {
    push(
      'destinationPlanetId',
      'Destination must differ from the planet you are calling from.',
    );
  }

  if (rows.length === 0) {
    push('lifeForms', 'A call must include at least one life form.');
  }

  rows.forEach((row, i) => {
    if (!row.species.trim()) {
      // Not the author's intended message: FluentValidation's .WithMessage()
      // binds only to the rule before it, so .NotEmpty() falls back to the
      // framework default. This is what the API actually returns.
      push(`lifeForms[${i}].species`, "'Species' must not be empty.");
    } else if (row.species.length > 100) {
      push(`lifeForms[${i}].species`, 'Every life form needs a species.');
    }

    const weight = Number(row.weightKg);
    if (row.weightKg.trim() === '' || Number.isNaN(weight) || weight <= 0) {
      push(
        `lifeForms[${i}].weightKg`,
        'Every life form must weigh more than zero kilograms.',
      );
    }
  });

  return errors;
}

function CallShuttle() {
  const navigate = useNavigate();
  const planets = usePlanetsStore();
  const fleet = useFleetStore();

  const [originPlanetId, setOriginPlanetId] = useState<number | ''>('');
  const [destinationPlanetId, setDestinationPlanetId] = useState<number | ''>('');
  const [rows, setRows] = useState<LifeFormRow[]>([emptyRow()]);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [banner, setBanner] = useState<ApiError | null>(null);
  const [rejection, setRejection] = useState<CallShuttleResult | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const loadPlanets = planets.startPolling;
  const refreshFleet = fleet.refresh;
  const hasFleet = fleet.data !== null;

  useEffect(() => {
    loadPlanets(); // fetch-once; planets are immutable seed data
  }, [loadPlanets]);

  // One read of the fleet, not a poll: the advisory ceilings below are
  // configuration (Fleet:MaxLifeFormsPerShuttle), so they are derived from a
  // real shuttle rather than hardcoded as 20 / 4000.
  useEffect(() => {
    if (!hasFleet) void refreshFleet();
  }, [hasFleet, refreshFleet]);

  const ceilings = useMemo(() => {
    const shuttles = fleet.data ?? [];
    if (shuttles.length === 0) return { lifeForms: 0, weightKg: 0 };
    return {
      lifeForms: Math.max(...shuttles.map((s) => s.lifeFormsAboard + s.remainingLifeForms)),
      weightKg: Math.max(...shuttles.map((s) => s.weightAboardKg + s.remainingWeightKg)),
    };
  }, [fleet.data]);

  const totalWeight = rows.reduce((n, r) => n + (Number(r.weightKg) || 0), 0);
  const unfittable =
    ceilings.lifeForms > 0 &&
    (rows.length > ceilings.lifeForms || totalWeight > ceilings.weightKg);

  const setRow = (i: number, patch: Partial<LifeFormRow>) =>
    setRows((prev) => prev.map((r, j) => (i === j ? { ...r, ...patch } : r)));

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    // Clear everything first so no message from the previous attempt lingers.
    setFieldErrors({});
    setBanner(null);
    setRejection(null);

    const planetIds = new Set((planets.data ?? []).map((p) => p.id));
    const errors = validate(originPlanetId, destinationPlanetId, rows, planetIds);
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      return;
    }

    setSubmitting(true);
    try {
      const result = await callShuttle({
        originPlanetId: originPlanetId as number,
        destinationPlanetId: destinationPlanetId as number,
        lifeForms: rows.map((r) => ({
          species: r.species.trim(),
          weightKg: Number(r.weightKg),
        })),
      });

      // All three outcomes arrive as 201. A refusal is a business result, not
      // a failure: show it here with its reason, and let Assigned/Queued go
      // straight to the detail page where the call can be watched live.
      if (result.outcome === 'Rejected') {
        setRejection(result);
        return;
      }
      void navigate({
        to: '/requests/$id',
        params: { id: String(result.travelRequestId) },
      });
    } catch (e) {
      const error = e as ApiError;
      // Inline under the inputs, or a banner — never both for one failure.
      if (error.hasFieldErrors) setFieldErrors(error.fieldErrors);
      else setBanner(error);
    } finally {
      setSubmitting(false);
    }
  }

  const input =
    'w-full rounded-md border border-edge bg-panel px-3 py-2 text-sm text-ink placeholder:text-muted focus:border-accent focus:outline-none';

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <header>
        <h1 className="text-xl font-semibold tracking-tight">Call a shuttle</h1>
        <p className="mt-1 text-sm text-muted">
          Name the party and where it is going. The dispatcher batches, queues, or refuses
          the call — and records the decision either way.
        </p>
      </header>

      {planets.error && (
        <ErrorBanner error={planets.error} onRetry={() => void planets.refresh()} />
      )}
      {banner && <ErrorBanner error={banner} />}

      {rejection && (
        <div className="rounded-lg border border-queued/40 bg-queued/10 p-4 text-sm">
          <p className="font-medium text-queued">
            Call #{rejection.travelRequestId} refused
          </p>
          <p className="mt-1 text-ink">{rejection.rejectionReason}</p>
          <p className="mt-2 text-muted">
            It was still recorded, and counts against its destination on the stats page.
          </p>
          <Link
            to="/requests/$id"
            params={{ id: String(rejection.travelRequestId) }}
            className="mt-2 inline-block text-accent hover:underline"
          >
            View the call →
          </Link>
        </div>
      )}

      <form onSubmit={onSubmit} className="grid gap-6 lg:grid-cols-[2fr_1fr]" noValidate>
        <div className="space-y-6">
          <section className="grid gap-4 rounded-xl border border-edge bg-panel p-4 sm:grid-cols-2">
            <div>
              <label htmlFor="originPlanetId" className="mb-1 block text-sm text-muted">
                Calling from
              </label>
              <select
                id="originPlanetId"
                className={input}
                value={originPlanetId}
                onChange={(e) =>
                  setOriginPlanetId(e.target.value === '' ? '' : Number(e.target.value))
                }
                {...fieldErrorProps(fieldErrors, 'originPlanetId')}
              >
                <option value="">Select a planet…</option>
                {(planets.data ?? []).map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
              <FieldError errors={fieldErrors} name="originPlanetId" />
            </div>

            <div>
              <label
                htmlFor="destinationPlanetId"
                className="mb-1 block text-sm text-muted"
              >
                Travelling to
              </label>
              <select
                id="destinationPlanetId"
                className={input}
                value={destinationPlanetId}
                onChange={(e) =>
                  setDestinationPlanetId(
                    e.target.value === '' ? '' : Number(e.target.value),
                  )
                }
                {...fieldErrorProps(fieldErrors, 'destinationPlanetId')}
              >
                <option value="">Select a planet…</option>
                {(planets.data ?? []).map((p) => (
                  // Disabled rather than hidden, so the rule stays visible.
                  <option key={p.id} value={p.id} disabled={p.id === originPlanetId}>
                    {p.name}
                    {p.id === originPlanetId ? ' (you are here)' : ''}
                  </option>
                ))}
              </select>
              <FieldError errors={fieldErrors} name="destinationPlanetId" />
            </div>
          </section>

          <section className="space-y-3 rounded-xl border border-edge bg-panel p-4">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-medium">Life forms</h2>
              <button
                type="button"
                onClick={() => setRows((prev) => [...prev, emptyRow()])}
                className="rounded-md border border-edge px-3 py-1.5 text-xs hover:bg-raised"
              >
                + Add life form
              </button>
            </div>
            <FieldError errors={fieldErrors} name="lifeForms" />

            {rows.map((row, i) => (
              <div key={i} className="grid gap-3 sm:grid-cols-[1fr_9rem_auto]">
                <div>
                  <label htmlFor={`species-${i}`} className="sr-only">
                    Species, life form {i + 1}
                  </label>
                  <input
                    id={`species-${i}`}
                    className={input}
                    placeholder="Species"
                    maxLength={100}
                    value={row.species}
                    onChange={(e) => setRow(i, { species: e.target.value })}
                    {...fieldErrorProps(fieldErrors, `lifeForms[${i}].species`)}
                  />
                  <FieldError errors={fieldErrors} name={`lifeForms[${i}].species`} />
                </div>

                <div>
                  <label htmlFor={`weight-${i}`} className="sr-only">
                    Weight in kilograms, life form {i + 1}
                  </label>
                  <input
                    id={`weight-${i}`}
                    className={input}
                    type="number"
                    inputMode="decimal"
                    placeholder="Weight (kg)"
                    min="0.1"
                    step="0.1"
                    value={row.weightKg}
                    onChange={(e) => setRow(i, { weightKg: e.target.value })}
                    {...fieldErrorProps(fieldErrors, `lifeForms[${i}].weightKg`)}
                  />
                  <FieldError errors={fieldErrors} name={`lifeForms[${i}].weightKg`} />
                </div>

                {rows.length > 1 && (
                  <button
                    type="button"
                    aria-label={`Remove life form ${i + 1}`}
                    onClick={() => setRows((prev) => prev.filter((_, j) => j !== i))}
                    className="h-[38px] rounded-md border border-edge px-3 text-xs text-muted hover:bg-raised hover:text-rejected"
                  >
                    Remove
                  </button>
                )}
              </div>
            ))}
          </section>
        </div>

        <aside className="h-fit space-y-3 rounded-xl border border-edge bg-panel p-4 text-sm">
          <h2 className="text-sm font-medium">This party</h2>
          <dl className="space-y-1 text-muted">
            <div className="flex justify-between">
              <dt>Party size</dt>
              <dd className="tnum text-ink">{rows.length}</dd>
            </div>
            <div className="flex justify-between">
              <dt>Total weight</dt>
              <dd className="tnum text-ink">{Math.round(totalWeight * 100) / 100} kg</dd>
            </div>
          </dl>

          {ceilings.lifeForms > 0 && (
            <p className="text-xs text-muted">
              Largest shuttle: {ceilings.lifeForms} life forms or {ceilings.weightKg} kg —
              whichever cap is reached first.
            </p>
          )}

          {unfittable && (
            // Advisory, never blocking. The backend records the rejection, and
            // rejections are exactly the signal the stats page exists to show.
            <p className="rounded-md border border-queued/40 bg-queued/10 p-2 text-xs text-queued">
              No shuttle can carry this party — the call will be rejected. You can still
              send it; it will be recorded.
            </p>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="w-full rounded-md bg-accent px-4 py-2 text-sm font-medium text-void hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? 'Calling…' : 'Call a shuttle'}
          </button>

          <p className="text-xs text-muted">
            A call cannot be edited or cancelled once placed — the API exposes no write for
            an existing call.
          </p>
        </aside>
      </form>
    </div>
  );
}
