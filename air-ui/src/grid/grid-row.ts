/** A row as the server answers it: whatever the row type carries, addressed by its `id`. */
export type GridRow = Readonly<Record<string, unknown>>;

export type GridRowKey = number;

/** The row's key, its `id`; `null` when the row has no numeric id. */
export function keyOf(row: GridRow | null | undefined): GridRowKey | null {
  const key = row?.['id'];
  return typeof key === 'number' ? key : null;
}
