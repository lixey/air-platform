import type { GridColumnDto } from '../../core/generated/contracts';
import { valueAt } from '../../core/json';
import type { GridCellContext } from '../cell/grid-cell-context';
import { SELECTION_PATH } from '../column/grid-selection-column';
import type { GridRow } from '../grid-row';
import type { GridHeaderRowCell } from '../header/grid-header-rows';

/**
 * The cells of one row, one per drawn column: the value at the column's path, none for the selection column. A body
 * row's cells carry its index in the page; a row the page pins carries -1 and where it is pinned.
 */
export function rowCells(row: GridRow, columns: readonly GridColumnDto[], index: number, pinned?: 'top' | 'bottom'): GridCellContext[] {
  return columns.map(column => ({
    row,
    column,
    value: column.path === SELECTION_PATH ? undefined : valueAt(row, column.path),
    index,
    ...(pinned ? { pinned } : {}),
  }));
}

/** Whether two lists of columns are the same to a cell: the same length and, column by column, every member but `width` the same. */
export function sameColumnsLessWidth(a: readonly GridColumnDto[], b: readonly GridColumnDto[]): boolean {
  return a.length === b.length && a.every((column, index) => isSameLessWidth(column, b[index]));
}

/** Whether two columns differ in nothing but their width: a copy the column state made keeps every other member's value. */
function isSameLessWidth(a: GridColumnDto, b: GridColumnDto): boolean {
  if (a === b) {
    return true;
  }
  const left: Readonly<Record<string, unknown>> = { ...a, width: undefined };
  const right: Readonly<Record<string, unknown>> = { ...b, width: undefined };
  return Object.keys({ ...left, ...right }).every(key => Object.is(left[key], right[key]));
}

/**
 * The cells of the column header, one per drawn column, without a row: a column's cell is taken from `kept` while its
 * object stands, made and kept otherwise.
 */
export function headerCells(columns: readonly GridColumnDto[], kept: WeakMap<GridColumnDto, GridCellContext>): GridCellContext[] {
  return columns.map(column => {
    let cell = kept.get(column);
    if (!cell) {
      cell = { row: null, column, value: undefined, index: -1 };
      kept.set(column, cell);
    }
    return cell;
  });
}

/** A header row's cell as its component is given it: a synthetic column under the cell's label, and the cell. */
export function headerRowCell(item: GridHeaderRowCell): GridCellContext {
  return {
    row: null,
    column: {
      path: '',
      label: item.cell.label,
      type: 'text',
      isSortable: false,
      isFilterable: false,
      isResizable: false,
      isSticky: item.isSticky,
      isStickyRight: false,
      isHidden: false,
      isDisabled: false,
      isRequired: false,
      cell: { kind: 'GridCellText' },
    },
    value: item.cell.label,
    index: -1,
    header: item.cell,
  };
}
