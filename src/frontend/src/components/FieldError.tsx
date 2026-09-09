import type { FieldErrors } from '../api/client';

/**
 * Field errors are shown inline under their input and never also as a banner —
 * the same message in two places reads as two separate failures.
 *
 * `name` is the normalized camelCase key (e.g. "lifeForms[0].weightKg"), which
 * is what normalizeFieldKey turns the server's PascalCase path into.
 */
export function FieldError({ errors, name }: { errors: FieldErrors; name: string }) {
  const messages = errors[name];
  if (!messages?.length) return null;

  return (
    <ul id={`${name}-error`} className="mt-1 space-y-0.5 text-xs text-rejected">
      {messages.map((m) => (
        <li key={m}>{m}</li>
      ))}
    </ul>
  );
}

/** Wire an input to its messages for screen readers. */
export const fieldErrorProps = (errors: FieldErrors, name: string) =>
  errors[name]?.length
    ? { 'aria-invalid': true, 'aria-describedby': `${name}-error` }
    : {};
