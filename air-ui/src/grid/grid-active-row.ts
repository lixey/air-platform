import { linkedSignal } from '@angular/core';
import { type GridRow, type GridRowKey, keyOf } from './grid-row';

/**
 * What the active row becomes when the next page arrives: nothing, the first row, or the row
 * with a key — the first when the key is not on the page.
 */
type PendingActive = 'none' | 'first' | { readonly key: GridRowKey };

/**
 * The active row of one grid, and the rule that moves it: none when the grid opens; after a change of sort,
 * filter, search or page the first row, if a row was active; after a reload of the same page the row with the same
 * key, or the first when it is gone. The grid says what is about to happen (`leave`, `keepKey`) and the rule is
 * applied when the next page of rows arrives; a change of `resetBy` starts over with no active row. A grid that
 * `startsOnFirst`, the list of a picker, has the first row of every page active instead.
 */
export class GridActiveRow {
  /** What the next page of rows makes active, consumed when that page is settled. */
  private pending: PendingActive = 'none';

  /** The key of the active row, so a reload finds it again. */
  private key: GridRowKey | null = null;

  /** The active index: what the user set, or, once another page of rows arrived, what the last change said. */
  private readonly _index = linkedSignal<{ readonly rows: readonly GridRow[]; readonly reset: unknown }, number>({
    source: () => ({ rows: this.rows(), reset: this.resetBy() }),
    computation: (source, previous) => {
      if (!previous || previous.source.reset !== source.reset) {
        this.pending = 'none';
        this.key = null;
        return this.startsOnFirst() ? this.first(source.rows) : -1;
      }
      if (previous.source.rows === source.rows) {
        return previous.value;
      }
      return this.startsOnFirst() ? this.first(source.rows) : this.settle(source.rows, previous.value);
    },
  });
  readonly index = this._index.asReadonly();

  constructor(
    private readonly rows: () => readonly GridRow[],
    private readonly resetBy: () => unknown = () => null,
    /** Whether the first row of every page is active, as in the list of a picker; a grid starts with none. */
    private readonly startsOnFirst: () => boolean = () => false,
  ) {}

  set(index: number): void {
    const rows = this.rows();
    const bounded = rows.length === 0 ? -1 : Math.min(Math.max(0, index), rows.length - 1);
    this._index.set(bounded);
    this.key = bounded < 0 ? null : keyOf(rows[bounded]);
  }

  /** Moves the active row by `delta`; from no active row either direction lands on the first. */
  move(delta: number): void {
    this.set(this._index() + delta);
  }

  /** The page is about to change: the first row of the next one is active if a row is active now. */
  leave(): void {
    this.pending = this._index() >= 0 ? 'first' : 'none';
    this._index.set(-1);
    this.key = null;
  }

  /** The same page is asked again: the row with the active key is active on it, or the first when it is gone. */
  keepKey(): void {
    const key = this.key;
    this.pending = key === null ? 'none' : { key };
  }

  /** The first row of a page, active whatever was pending; none on a page of no rows. */
  private first(rows: readonly GridRow[]): number {
    this.pending = 'none';
    this.key = rows.length === 0 ? null : keyOf(rows[0]);
    return rows.length === 0 ? -1 : 0;
  }

  /** The index a page of rows settles on, by the pending rule, which it consumes. */
  private settle(rows: readonly GridRow[], current: number): number {
    const pending = this.pending;
    this.pending = 'none';
    if (pending === 'none' && current < rows.length) {
      return current;
    }
    const index = pending === 'none' || pending === 'first' ? 0 : rows.findIndex(row => keyOf(row) === pending.key);
    const bounded = rows.length === 0 ? -1 : Math.min(Math.max(0, index), rows.length - 1);
    this.key = bounded < 0 ? null : keyOf(rows[bounded]);
    return bounded;
  }
}
