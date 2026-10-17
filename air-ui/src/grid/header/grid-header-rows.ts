import type { GridColumnDto, GridHeaderCellDto } from '../../core/generated/contracts';
import { SELECTION_PATH } from '../column/grid-selection-column';

/** One cell of a header row as the table draws it: over the visible columns from `index`, `span` of them. */
export interface GridHeaderRowCell {
  readonly cell: GridHeaderCellDto;
  /** The index of the first column the cell spans, among the columns the table draws. */
  readonly index: number;
  /** How many of the drawn columns the cell spans: the declared span less the hidden ones. */
  readonly span: number;
  /** Whether every column the cell spans is sticky, so the cell is sticky with them. */
  readonly isSticky: boolean;
}

const EMPTY: GridHeaderCellDto = { label: '', colspan: 1, model: { kind: 'GridHeaderLabel' } };

/**
 * Lays a header row over the columns the table draws. The server's spans count the declared
 * columns in order, hidden ones included; a cell's drawn span is the number of them that are
 * visible, so hiding a column narrows its group and hiding them all removes the cell. The
 * selection column, the client's own, gets an empty cell in front.
 */
export function spanHeaderRow(
  row: readonly GridHeaderCellDto[],
  declared: readonly GridColumnDto[],
  drawn: readonly GridColumnDto[],
): GridHeaderRowCell[] {
  const result: GridHeaderRowCell[] = [];
  let index = 0;

  if (drawn[0]?.path === SELECTION_PATH) {
    result.push({ cell: EMPTY, index: 0, span: 1, isSticky: true });
    index = 1;
  }

  let declaredIndex = 0;
  for (const cell of row) {
    const spanned = declared.slice(declaredIndex, declaredIndex + Math.max(1, cell.colspan));
    declaredIndex += Math.max(1, cell.colspan);
    const visible = spanned.filter(column => !column.isHidden);
    if (visible.length === 0) {
      continue;
    }
    result.push({ cell, index, span: visible.length, isSticky: visible.every(column => column.isSticky) });
    index += visible.length;
  }

  return result;
}

/**
 * The paths of the columns that start a group of more than one drawn column in any header row,
 * where the table draws a rule so the groups are told apart.
 */
export function groupStartPaths(
  rows: readonly (readonly GridHeaderCellDto[])[],
  declared: readonly GridColumnDto[],
  drawn: readonly GridColumnDto[],
): ReadonlySet<string> {
  const paths = new Set<string>();
  for (const row of rows) {
    for (const item of spanHeaderRow(row, declared, drawn)) {
      const path = drawn[item.index]?.path;
      if (item.span > 1 && path && path !== SELECTION_PATH) {
        paths.add(path);
      }
    }
  }
  return paths;
}
