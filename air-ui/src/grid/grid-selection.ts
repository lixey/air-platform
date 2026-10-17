import { computed, linkedSignal } from '@angular/core';
import { type GridRow, type GridRowKey, keyOf } from './grid-row';

/**
 * The rows the user selected, by key: a click toggles one row and sets the anchor, a shift-click
 * selects or clears the range from the anchor, the header toggles the page. The rows
 * themselves are never written. It starts empty again whenever `resetBy` changes.
 */
export class GridSelection {
  /** The selected rows by key, and the anchor of a shift-click range. */
  private readonly state = linkedSignal<unknown, { readonly rows: ReadonlyMap<GridRowKey, GridRow>; readonly anchor: GridRowKey | null }>({
    source: () => this.resetBy(),
    computation: () => ({ rows: new Map(), anchor: null }),
  });
  private readonly rows = computed(() => this.state().rows);
  readonly ids = computed<readonly GridRowKey[]>(() => [...this.rows().keys()]);
  readonly selected = computed<readonly GridRow[]>(() => [...this.rows().values()]);
  readonly count = computed(() => this.rows().size);
  readonly isEmpty = computed(() => this.rows().size === 0);

  constructor(private readonly resetBy: () => unknown = () => null) {}

  retain(rows: readonly GridRow[]): void {
    const selected = this.rows();
    this.write(
      new Map(
        rows.flatMap(row => {
          const key = keyOf(row);
          return key !== null && selected.has(key) ? [[key, row] as const] : [];
        }),
      ),
    );
  }

  isSelected(row: GridRow | null | undefined): boolean {
    const key = keyOf(row);
    return key !== null && this.rows().has(key);
  }

  clear(): void {
    if (this.rows().size > 0 || this.state().anchor !== null) {
      this.state.set({ rows: new Map(), anchor: null });
    }
  }

  remove(ids: readonly GridRowKey[]): void {
    const removed = new Set(ids);
    this.write(new Map([...this.rows()].filter(([key]) => !removed.has(key))));
  }

  /**
   * Toggles a row; with shift, the range between the anchor and the row takes the row's new state.
   * `rows` is the page, for the range.
   */
  toggle(row: GridRow, rows: readonly GridRow[], shift = false): void {
    const key = keyOf(row);
    if (key === null) {
      return;
    }

    const next = new Map(this.rows());
    const wasSelected = next.has(key);

    const anchor = this.state().anchor;
    if (shift && anchor !== null) {
      const keys = rows.map(r => keyOf(r));
      const from = keys.indexOf(anchor);
      const to = keys.indexOf(key);
      if (from >= 0 && to >= 0) {
        const [start, end] = from < to ? [from, to] : [to, from];
        for (let i = start; i <= end; i += 1) {
          const k = keys[i];
          if (k === null) {
            continue;
          }
          if (wasSelected) {
            next.delete(k);
          } else {
            next.set(k, rows[i]);
          }
        }
        this.state.set({ rows: next, anchor });
        return;
      }
    }

    if (wasSelected) {
      next.delete(key);
    } else {
      next.set(key, row);
    }
    this.state.set({ rows: next, anchor: key });
  }

  /** Selects every selectable row of the page, or clears the selection. */
  toggleAll(rows: readonly GridRow[], checked: boolean): void {
    if (!checked) {
      this.clear();
      return;
    }

    const next = new Map<GridRowKey, GridRow>();
    for (const row of rows) {
      const key = keyOf(row);
      if (key !== null) {
        next.set(key, row);
      }
    }
    this.state.set({ rows: next, anchor: next.size > 0 ? [...next.keys()][0] : null });
  }

  /** How many of the page's selectable rows are selected: for the header's checked and indeterminate states. */
  countIn(rows: readonly GridRow[]): { selectable: number; selected: number } {
    let selectable = 0;
    let selected = 0;
    for (const row of rows) {
      const key = keyOf(row);
      if (key !== null) {
        selectable += 1;
        if (this.rows().has(key)) {
          selected += 1;
        }
      }
    }
    return { selectable, selected };
  }

  /** New selected rows; the anchor goes when its row is no longer among them. */
  private write(rows: ReadonlyMap<GridRowKey, GridRow>): void {
    const anchor = this.state().anchor;
    this.state.set({ rows, anchor: anchor !== null && rows.has(anchor) ? anchor : null });
  }
}
