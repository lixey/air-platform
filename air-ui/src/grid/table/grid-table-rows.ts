import { UI_COLORS } from '../../theme/ui-color';
import type { GridCellContext } from '../cell/grid-cell-context';
import type { GridRow } from '../grid-row';
import { cellClassOf, rowClassesOf } from './grid-table-classes';

const FLAG_HUES = new Set<string>(UI_COLORS);

function rowFlagHueOf(row: GridRow): string | null {
  const flags = row['flags'];
  if (!Array.isArray(flags)) {
    return null;
  }
  for (const flag of flags) {
    if (typeof flag === 'string') {
      const lower = flag.toLowerCase();
      if (FLAG_HUES.has(lower)) {
        return lower;
      }
    }
  }
  return null;
}

/** A row as the table draws it: its cells and the cell class of its section. */
export interface TableRow {
  readonly row: GridRow;
  readonly cells: readonly GridCellContext[];
  readonly cellClass: string;
}

/** A body row, with what it says of itself while its window, its page and the marks of a save stand. */
export interface BodyRow extends TableRow {
  /** Its index on the page. */
  readonly index: number;
  /** The id of its element. */
  readonly id: string;
  /** Its place in the whole result: after the head, the pages before this one and the rows above it. */
  readonly ariaRowIndex: number;
  /** The ids of its cells' elements, by column. */
  readonly cellIds: readonly string[];
  /** Whether each of its cells is one a save changed, by column. */
  readonly marks: readonly boolean[];
}

/** What a drawn body row says while the active row and the selection move: its ground and its `aria-selected`. */
export interface BodyRowState {
  readonly classes: string;
  readonly ariaSelected: boolean | null;
}

/** What the body rows of one window are built from, beside each row and its cells. */
export interface BodyRowSource {
  readonly tableId: string;
  /** The place of the page's first row in the whole result. */
  readonly ariaStart: number;
  readonly cellClass: string;
  readonly isChanged: (row: GridRow | null, path: string | null | undefined) => boolean;
}

/** The id of a body row's element: the table's and the row's index on the page. */
export function rowElementIdOf(tableId: string, index: number): string {
  return `${tableId}-row-${index}`;
}

/** The id of a body cell's element: its row's and its column's index. */
export function cellElementIdOf(tableId: string, index: number, column: number): string {
  return `${rowElementIdOf(tableId, index)}-c${column}`;
}

/** A body row with its facts, built once: each cell is asked whether a save changed it one time, in the cells' order. */
export function bodyRowOf(row: GridRow, index: number, cells: readonly GridCellContext[], source: BodyRowSource): BodyRow {
  return {
    row,
    cells,
    cellClass: source.cellClass,
    index,
    id: rowElementIdOf(source.tableId, index),
    ariaRowIndex: source.ariaStart + index,
    cellIds: cells.map((_, column) => cellElementIdOf(source.tableId, index, column)),
    marks: cells.map(cell => source.isChanged(cell.row, cell.column.path)),
  };
}

/**
 * The states of the body rows, by row: the row's ground through the grid roles — stripes, a hovered row, a selected
 * row, and the active row over both — and its `aria-selected`, null in a grid that selects nothing.
 */
export function bodyRowStatesOf(
  rows: readonly BodyRow[],
  activeIndex: number,
  isSelectable: boolean,
  isSelected: (row: GridRow) => boolean,
): BodyRowState[] {
  return rows.map(item => {
    const selected = isSelected(item.row);
    return {
      classes: rowClassesOf(item.index, item.index === activeIndex, selected, rowFlagHueOf(item.row)),
      ariaSelected: isSelectable ? selected : null,
    };
  });
}

/**
 * A drawn row's cell classes by column: its section's, its column's, the active cell's ring at `activeColumn` (-1 for
 * a row that is not the active one, and for a pinned row) and a save's mark where `marks` names the cell; a pinned row
 * has no marks.
 */
export function rowCellClassesOf(
  row: TableRow,
  columnClasses: readonly string[],
  activeColumn: number,
  marks?: readonly boolean[],
): string[] {
  return row.cells.map((_, index) => cellClassOf(row.cellClass, columnClasses[index], index === activeColumn, marks?.[index] ?? false));
}
