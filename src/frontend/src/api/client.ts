export type FieldErrors = Record<string, string[]>;

/**
 * One error type for all three of the API's error envelopes (see docs/frontend-pdr.md §7).
 * `status` is 0 for a network-level failure, which is not an HTTP outcome at all.
 */
export class ApiError extends Error {
  // Written out longhand rather than as constructor parameter properties:
  // `erasableSyntaxOnly` forbids syntax that emits runtime code from a type
  // position, and parameter properties do exactly that.
  readonly status: number;
  readonly fieldErrors: FieldErrors;
  readonly traceId?: string;

  constructor(
    status: number,
    message: string,
    fieldErrors: FieldErrors = {},
    traceId?: string,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.fieldErrors = fieldErrors;
    this.traceId = traceId;
  }

  /** True when at least one error can be shown against a form input. */
  get hasFieldErrors(): boolean {
    return Object.keys(this.fieldErrors).length > 0;
  }
}

/**
 * Validation error keys are PascalCase C# property paths, but the payload we
 * posted was camelCase — so the keys never match the form fields without this.
 * Extend both this map and INDEXED whenever a new field can fail validation.
 */
const FIELD_KEY_MAP: Record<string, string> = {
  OriginPlanetId: 'originPlanetId',
  DestinationPlanetId: 'destinationPlanetId',
  LifeForms: 'lifeForms',
  Page: 'page',
  PageSize: 'pageSize',
};

/** e.g. "LifeForms[0].WeightKg" — an indexer inside the path, not a flat name. */
const INDEXED = /^LifeForms\[(\d+)\]\.(Species|WeightKg)$/;

export function normalizeFieldKey(key: string): string {
  const m = INDEXED.exec(key);
  if (m) {
    const [, i, prop] = m;
    return `lifeForms[${i}].${prop!.charAt(0).toLowerCase()}${prop!.slice(1)}`;
  }
  return FIELD_KEY_MAP[key] ?? key.charAt(0).toLowerCase() + key.slice(1);
}

const UNREACHABLE = 'Cannot reach the API. Is the backend running on port 5095?';

/** Statuses the dev proxy invents when the backend is not answering. */
const GATEWAY = new Set([502, 503, 504]);

/** The shape of the three envelopes, unified — every key optional. */
interface ErrorBody {
  title?: string | null;
  detail?: string | null;
  errors?: FieldErrors | null;
  traceId?: string | null;
}

/**
 * The single way this app talks to the API. Paths are relative on purpose:
 * the Vite proxy makes them same-origin, because the backend has no CORS.
 */
export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response;
  try {
    res = await fetch(path, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        ...init?.headers,
      },
    });
  } catch {
    // fetch() itself rejected — DNS, connection reset, offline.
    throw new ApiError(0, UNREACHABLE);
  }

  // A 404 from the routing layer (a non-int id) has no JSON body at all.
  const body = (await res.json().catch(() => null)) as ErrorBody | null;

  if (!res.ok) {
    const fieldErrors: FieldErrors = {};
    // Envelope 1 (validation) is the only one carrying `errors`.
    if (body?.errors && typeof body.errors === 'object') {
      for (const [k, v] of Object.entries(body.errors)) {
        fieldErrors[normalizeFieldKey(k)] = v;
      }
    }
    // Envelope 2 (not found) and the malformed-body case use `detail`;
    // envelopes 1 and 3 put the message in `title`.
    //
    // A gateway status with no envelope at all is not the API answering — it is
    // the Vite proxy reporting that it could not reach the API. Verified: with
    // the backend stopped the proxy returns 502 and an empty body, so without
    // this the user would be told "Request failed (502)" instead of the one
    // thing that would actually help them.
    const unreachable = body === null && GATEWAY.has(res.status);
    const message = unreachable
      ? UNREACHABLE
      : // Never show a raw status code to a user; every real envelope has text.
        body?.detail || body?.title || 'Something went wrong. Please try again.';
    throw new ApiError(res.status, message, fieldErrors, body?.traceId ?? undefined);
  }

  return body as T;
}
