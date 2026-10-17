/** A JSON object: not null, not an array. `{ value: null }` and `[]` are not records. */
export function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/** The value at a dotted path: `unit.name` reads `row.unit.name`. */
export function valueAt(row: Record<string, unknown> | null | undefined, path: string | null | undefined): unknown {
  if (!row || !path) {
    return undefined;
  }

  let current: unknown = row;
  for (const segment of path.split('.')) {
    if (!isRecord(current)) {
      return undefined;
    }
    current = current[segment];
  }

  return current;
}
