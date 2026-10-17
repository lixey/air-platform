import { linkedSignal, untracked } from '@angular/core';
import type { GridColumnDto } from '../core/generated/contracts';
import { valueAt } from '../core/json';
import { SELECTION_PATH } from './column/grid-selection-column';
import { type GridRow, type GridRowKey, keyOf } from './grid-row';

/** The marked cells: by row key, the paths of the columns whose cells are marked. */
type Marks = ReadonlyMap<GridRowKey, ReadonlySet<string>>;

/**
 * The body cells a save changed, marked on the page its refresh loads. The grid says which record a save is about to
 * reload (`expect`), with the rows on screen; when the next page arrives (`settle`) the record's row there is compared
 * with the row it had, per drawn column, at the column's path and at its cell's `colorPath`, by structure, as a reference
 * value is an object. A record that was not on the page before — one the form added — has every cell whose value is not
 * empty marked. The marks add up over saves, stay with the rows still on the page across a reload of it, and are dropped
 * for the rows it no longer holds; `clear` drops them all, and a change of `resetBy` starts over with none.
 */
export class GridChangedCells {
  /** The rows the expected records had before their saves, by key; none for a record that was not on the page. */
  private readonly expected = new Map<GridRowKey, GridRow | null>();

  private readonly marks = linkedSignal<unknown, Marks>({
    source: () => this.resetBy(),
    computation: () => {
      this.expected.clear();
      return new Map();
    },
  });

  constructor(private readonly resetBy: () => unknown = () => null) {}

  /**
   * The record with this key is about to be reloaded after a save: its row among `rows`, the rows on screen, is what the
   * next page compares with. A record already expected keeps the row it had before the first save.
   */
  expect(id: GridRowKey, rows: readonly GridRow[]): void {
    // Reading the marks starts them over first when `resetBy` changed, so the expectation belongs to the new state
    untracked(this.marks);
    if (!this.expected.has(id)) {
      this.expected.set(id, rows.find(row => keyOf(row) === id) ?? null);
    }
  }

  /**
   * A page arrived: the expected records found on it add their changed cells to the marks, the marks of the rows it
   * does not hold are dropped, and every expectation is consumed.
   */
  settle(rows: readonly GridRow[], columns: readonly GridColumnDto[]): void {
    const marks = untracked(this.marks);
    const next = new Map<GridRowKey, ReadonlySet<string>>();
    for (const row of rows) {
      const key = keyOf(row);
      if (key === null) {
        continue;
      }
      const paths = new Set(marks.get(key));
      if (this.expected.has(key)) {
        for (const path of changedPaths(this.expected.get(key) ?? null, row, columns)) {
          paths.add(path);
        }
      }
      if (paths.size > 0) {
        next.set(key, paths);
      }
    }
    this.expected.clear();
    if (!sameMarks(marks, next)) {
      this.marks.set(next);
    }
  }

  /** Whether the cell of this row at the column's path is marked. Reads the marks signal, so a `computed` follows it. */
  isChanged(row: GridRow | null | undefined, path: string | null | undefined): boolean {
    const key = keyOf(row);
    return key !== null && !!path && (this.marks().get(key)?.has(path) ?? false);
  }

  /** Drops the marks and the expectations: the request changed. */
  clear(): void {
    this.expected.clear();
    if (untracked(this.marks).size > 0) {
      this.marks.set(new Map());
    }
  }
}

/**
 * The paths of the columns whose cell changed from `before` to `after`: the value at the path or at the cell's
 * `colorPath` differs by structure; with no row before, every column whose value is not empty.
 */
function changedPaths(before: GridRow | null, after: GridRow, columns: readonly GridColumnDto[]): string[] {
  return columns.flatMap(column => {
    const path = column.path;
    if (!path || path === SELECTION_PATH) {
      return [];
    }
    if (before === null) {
      return isEmpty(valueAt(after, path)) ? [] : [path];
    }
    const colorPath = 'colorPath' in column.cell ? column.cell.colorPath : null;
    const changed = differs(before, after, path) || (!!colorPath && differs(before, after, colorPath));
    return changed ? [path] : [];
  });
}

function differs(before: GridRow, after: GridRow, path: string): boolean {
  return JSON.stringify(valueAt(before, path)) !== JSON.stringify(valueAt(after, path));
}

function isEmpty(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}

function sameMarks(one: Marks, other: Marks): boolean {
  return (
    one.size === other.size &&
    [...one].every(([key, paths]) => {
      const others = other.get(key);
      return others !== undefined && others.size === paths.size && [...paths].every(path => others.has(path));
    })
  );
}
