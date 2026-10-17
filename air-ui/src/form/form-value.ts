import { hasZone, temporalText } from '../controls/temporal-text';
import type { FormElementDto } from '../core/generated/contracts';

/** The form service owns JSON values; callers cannot mutate nested state behind its guards. */
export function frozenCopy<T>(value: T): T {
  if (Array.isArray(value)) {
    return Object.freeze(value.map(item => frozenCopy(item))) as T;
  }
  if (value !== null && typeof value === 'object') {
    return Object.freeze(Object.fromEntries(Object.entries(value).map(([key, item]) => [key, frozenCopy(item)]))) as T;
  }
  return value;
}

export function isSecret(element: FormElementDto): boolean {
  return Boolean(element.isSecret || element.type === 'password');
}

export function hasSecret(value: unknown): boolean {
  return typeof value === 'string' && value.length > 0;
}

/**
 * Sanitizes form draft key-values before sending across reference queries:
 * strips elements marked secret (e.secret) (Security Rules 2 & 3).
 */
export function sanitizeFormDraft(value: Record<string, unknown>, elements?: readonly FormElementDto[]): Record<string, unknown> {
  const secretPaths = new Set((elements ?? []).filter(isSecret).map(e => e.path));
  const sanitized: Record<string, unknown> = {};
  for (const [key, val] of Object.entries(value)) {
    if (secretPaths.has(key)) {
      continue;
    }
    sanitized[key] = val;
  }
  return sanitized;
}

/**
 * Only editable inputs contribute; reference captions and secret commands have no baseline. A date cut of an instant
 * is its day on the clock of `zone`, the user's, as the form shows it.
 */
export function canonicalJson(value: Record<string, unknown>, elements: readonly FormElementDto[], zone: string): string {
  return JSON.stringify(
    elements
      .filter(e => !e.isDisabled && !e.isServerOwned && !isSecret(e))
      .map(element => {
        let field = value[element.path];
        if (element.valueKind === 'list' && Array.isArray(field)) {
          field = [...new Set(field.map(item => JSON.stringify(stable(item))))].sort();
        } else if (element.temporalType && typeof field === 'string') {
          field = temporalKey(field, element, zone);
        }
        return [element.path, Object.hasOwn(value, element.path), stable(field)];
      }),
  );
}

function stable(value: unknown): unknown {
  if (Array.isArray(value)) {
    return ['array', ...value.map(stable)];
  }
  if (value !== null && typeof value === 'object') {
    return [
      'object',
      ...Object.keys(value)
        .sort()
        .map(key => [key, stable((value as Record<string, unknown>)[key])]),
    ];
  }
  return [typeof value, value];
}

function temporalKey(value: string, element: FormElementDto, zone: string): string {
  if (element.temporalType === 'dateOnly' || element.type === 'date') {
    return temporalText(value, element, zone).slice(0, 10);
  }
  if (hasZone(value)) {
    const instant = Date.parse(value);
    if (Number.isFinite(instant)) {
      // JS dates retain milliseconds; keep the remaining server precision separately.
      const remainder = (value.match(/\.(\d+)/)?.[1].slice(3) ?? '').replace(/0+$/, '');
      return `${instant}:${remainder}`;
    }
  }
  return value
    .replace(/\.0+$/, '')
    .replace(/(\.\d*?)0+$/, '$1')
    .replace(/T(\d{2}:\d{2})$/, 'T$1:00');
}
