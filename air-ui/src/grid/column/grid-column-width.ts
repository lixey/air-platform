import type { GridColumnDto } from '../../core/generated/contracts';
import { SELECTION_PATH } from './grid-selection-column';

/** The least a column can be, the most, and what the selection column is: the whole width vocabulary. */
export const MIN_COLUMN_WIDTH = 72;
export const SELECTION_COLUMN_WIDTH = 36;
export const MAX_COLUMN_WIDTH = 1920;

/**
 * What a header cell's own padding takes beside its label, and what a header's controls take beside it, so a fitted
 * label is not covered when they appear.
 */
export const CELL_PADDING = 16;
export const HEADER_CONTROLS = 48;

/** What a body cell takes beside its text: its padding on both sides and its right line. */
export const CELL_EDGES = 17;

/** The width a column of each kind takes when the server declared none; `text` is what every other kind takes. */
export const DEFAULT_WIDTHS = {
  selection: SELECTION_COLUMN_WIDTH,
  openForm: 80,
  checkbox: 100,
  number: 110,
  date: 110,
  datetime: 150,
  time: 90,
  text: 140,
} as const;

/** The default widths a column's type names; a type without one takes `text`'s. */
const WIDTH_BY_TYPE: Partial<Record<GridColumnDto['type'], number>> = DEFAULT_WIDTHS;

/** A width held to the column's maximum and to the least the column can be. */
export function boundedWidth(width: number, column: Pick<GridColumnDto, 'path' | 'width' | 'maxWidth'>): number {
  return Math.max(minWidthOf(column), Math.min(column.maxWidth ?? MAX_COLUMN_WIDTH, Math.round(width)));
}

/**
 * The least a column can be: 72 pixels for a drag and a fit; the selection column is as wide as
 * its checkbox, and a width the server declared below 72 — a calendar's 24-pixel week — stands.
 */
export function minWidthOf(column: Pick<GridColumnDto, 'path' | 'width'>): number {
  if (column.path === SELECTION_PATH) {
    return SELECTION_COLUMN_WIDTH;
  }
  const declared = column.width ?? null;
  return declared !== null && declared > 0 && declared < MIN_COLUMN_WIDTH ? declared : MIN_COLUMN_WIDTH;
}

/** The width a column takes when the server declared none: by its cell first, by its type otherwise. */
export function defaultWidthOf(column: Pick<GridColumnDto, 'path' | 'type' | 'cell'>): number {
  if (column.path === SELECTION_PATH) {
    return DEFAULT_WIDTHS.selection;
  }
  if (column.cell.kind === 'GridCellOpenForm') {
    return DEFAULT_WIDTHS.openForm;
  }
  return WIDTH_BY_TYPE[column.type] ?? DEFAULT_WIDTHS.text;
}

/** The width a column has in effect: the server's, else the kind's default, within the bounds. */
export function effectiveWidthOf(column: Pick<GridColumnDto, 'path' | 'type' | 'cell' | 'width' | 'maxWidth'>): number {
  return boundedWidth(column.width ?? defaultWidthOf(column), column);
}

/**
 * Whether a column without a width from the server is fitted to the first page when the grid
 * opens: the selection column, a checkbox and a column that wraps keep their kind's default.
 */
export function isFittable(column: Pick<GridColumnDto, 'path' | 'type' | 'width' | 'isWrapped'>): boolean {
  if (column.width !== null && column.width !== undefined) {
    return false;
  }
  return column.path !== SELECTION_PATH && column.type !== 'checkbox' && column.isWrapped !== true;
}
