import type { GridColumnDto } from '../../core/generated/contracts';
import { valueAt } from '../../core/json';
import { formatCellValue } from '../cell/grid-cell-format';
import { type GridRow } from '../grid-row';
import { columnLabelOf } from './grid-column-label';
import { boundedWidth, CELL_EDGES, CELL_PADDING, HEADER_CONTROLS, MAX_COLUMN_WIDTH } from './grid-column-width';

/** The most a fitted column takes when the server set no maximum. */
export const FIT_MAX_WIDTH = 640;
/** How many rows of the first page the opening fit reads. */
export const FIT_ROWS = 50;

/** The weight a cell's text is set in. */
const TEXT_WEIGHT = 400;
/** The weight a header's label is set in: semibold, as the default header draws it. */
const LABEL_WEIGHT = 600;

/**
 * Measures a text in the table's font: the label is semibold, a cell's text is not; `isTabular` counts every digit as
 * wide as the font's tabular digit.
 */
export type TextMeasure = (text: string, weight: typeof TEXT_WEIGHT | typeof LABEL_WEIGHT, isTabular?: boolean) => number;

/** The class the table sets its text in: its template draws it, and the probe of the fit's measure carries it. */
export const TABLE_TEXT = 'text-sm';
/** The class a cell draws tabular digits by. */
export const TABULAR_TEXT = 'tabular-nums';
/** The kinds of cell that draw their text in tabular digits. */
export const TABULAR_CELLS: ReadonlySet<GridColumnDto['cell']['kind']> = new Set<GridColumnDto['cell']['kind']>([
  'GridCellDate',
  'GridCellDatetime',
  'GridCellTime',
  'GridCellNumber',
  'GridCellMoney',
  'GridCellPercent',
  'GridCellOpenForm',
]);

/** Whether a column's cell draws its text in tabular digits, so the fit measures it in them. */
export function isTabular(column: Pick<GridColumnDto, 'cell'>): boolean {
  return TABULAR_CELLS.has(column.cell.kind);
}

/** The digits the probe holds: a tenth of its width is one tabular digit's. */
const PROBE_DIGITS = '0000000000';
const DIGIT = /\d/;
const DIGIT_RUNS = /\d+/;

let canvas: HTMLCanvasElement | null = null;
/** A tabular digit's width by the font it was read in, size and family; read once and kept for the life of the page. */
const digitWidths = new Map<string, number>();

/**
 * The one measure of every fit: over one shared canvas in the font the table draws its text in, so no layout is read
 * per text and no caller names an element to measure by; null where there is no document or no 2D context (a server
 * render, a test DOM without canvas). The font is read from a probe that carries the table's classes (`TABLE_TEXT`,
 * `TABULAR_TEXT`), put into the body out of the flow and taken out again. A canvas cannot state the width of a tabular
 * digit, so the probe's ten zeros say it, once per font: a tabular text is its digits at that width and its other runs
 * as the canvas measures them. A probe that measures nothing (a test DOM) leaves the digits to the canvas.
 */
export function tableMeasure(): TextMeasure | null {
  if (typeof document === 'undefined') {
    return null;
  }
  canvas ??= document.createElement('canvas');
  const context = canvas.getContext('2d');
  if (!context) {
    return null;
  }
  const probe = document.createElement('span');
  probe.className = `${TABLE_TEXT} ${TABULAR_TEXT}`;
  probe.style.cssText = 'position:absolute;visibility:hidden;white-space:nowrap';
  probe.setAttribute('aria-hidden', 'true');
  probe.textContent = PROBE_DIGITS;
  document.body.append(probe);
  const { fontSize, fontFamily } = getComputedStyle(probe);
  const font = `${fontSize} ${fontFamily}`;
  let digit = digitWidths.get(font) ?? 0;
  if (!digit) {
    digit = probe.getBoundingClientRect().width / PROBE_DIGITS.length;
    if (digit > 0) {
      digitWidths.set(font, digit);
    }
  }
  probe.remove();

  return (text, weight, isTabular = false) => {
    context.font = `${weight} ${font}`;
    if (!isTabular || !digit || !DIGIT.test(text)) {
      return context.measureText(text).width;
    }
    let width = 0;
    let rest = text.length;
    for (const run of text.split(DIGIT_RUNS)) {
      rest -= run.length;
      if (run) {
        width += context.measureText(run).width;
      }
    }
    return width + rest * digit;
  };
}

/**
 * The width a column takes to show its label and the texts given: the widest of the header (`headerWidthOf`) and the
 * texts as the cell draws them, in tabular digits where it does (`isTabular`) and with the cell's edges beside them
 * (`CELL_EDGES`), held to the column's bounds and to 640 when the server set no maximum. `measure` is the text's width
 * in the table's own text (`tableMeasure`).
 */
export function fitWidthOf(
  column: Pick<GridColumnDto, 'path' | 'label' | 'maxWidth' | 'isSortable' | 'isFilterable' | 'cell'>,
  texts: readonly string[],
  measure: TextMeasure,
): number {
  let widest = headerWidthOf(column, measure);
  const tabular = isTabular(column);
  for (const text of texts) {
    if (text) {
      widest = Math.max(widest, measure(text, TEXT_WEIGHT, tabular) + CELL_EDGES);
    }
  }
  return boundedWidth(Math.ceil(widest), {
    path: column.path,
    width: null,
    maxWidth: column.maxWidth ?? Math.min(FIT_MAX_WIDTH, MAX_COLUMN_WIDTH),
  });
}

/**
 * What a column's header takes: its label in the header's weight and the cell's padding, and the header's controls
 * where the header draws them, for a column that is sortable or filterable. The opening fit, a double click on the
 * column's edge and the fit of a picker's list read it, so the rule is one in every grid.
 */
export function headerWidthOf(column: Pick<GridColumnDto, 'path' | 'label' | 'isSortable' | 'isFilterable'>, measure: TextMeasure): number {
  const controls = column.isSortable || column.isFilterable ? HEADER_CONTROLS : 0;
  return measure(columnLabelOf(column), LABEL_WEIGHT) + CELL_PADDING + controls;
}

/**
 * The value of a row at a column as its standard cell shows it: a number, a date and a stamp by the locale, an instant
 * on the clock of `zone`, the rest as text.
 */
export function cellTextOf(column: Pick<GridColumnDto, 'path' | 'type' | 'cell'>, row: GridRow, locale: string, zone: string): string {
  return formatCellValue(valueAt(row, column.path), column, locale, zone);
}
