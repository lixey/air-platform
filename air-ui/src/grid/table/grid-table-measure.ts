import type { GridColumnDto, GridPageDto } from '../../core/generated/contracts';
import { cellTextOf, FIT_ROWS, fitWidthOf, headerWidthOf, type TextMeasure } from '../column/grid-column-fit';
import type { GridColumnState } from '../column/grid-column-state';
import { CELL_EDGES, isFittable } from '../column/grid-column-width';
import type { GridRow } from '../grid-row';

/** A character's width where no canvas measures the text: a test DOM, a server render. */
const FALLBACK_CHAR_WIDTH = 8;

/**
 * The height of the pinned bottom rows, what floats over the table's bottom edge sits above: their foot's height while
 * it sticks to the edge, nothing while it stands in the flow (below `md`) or the page pins no row at the bottom.
 */
export function pinnedInset(foot: HTMLElement | undefined): number {
  return foot && getComputedStyle(foot).position === 'sticky' ? foot.getBoundingClientRect().height : 0;
}

/** The pixel a content's `scrollWidth` may lack: the browser reports the content's width as a whole number. */
const ROUNDING = 1;

/**
 * The widest of a column's rendered cells: what its text would take unclipped, the pixel of that width's rounding and
 * the cell's edges (`CELL_EDGES`); 0 when no row draws the column.
 */
export function renderedContentWidth(rows: readonly HTMLTableRowElement[], column: number): number {
  let widest = 0;
  for (const row of rows) {
    // The cell is the outlet, its first child the cell's component, whose first child holds what the cell draws
    const content = row.cells[column]?.firstElementChild?.firstElementChild;
    if (content) {
      widest = Math.max(widest, content.scrollWidth + ROUNDING + CELL_EDGES);
    }
  }
  return widest;
}

/**
 * The widest of a column's header and its rendered cells: the label by `measure` in the table's own text, 8 pixels a
 * character where there is no canvas, with the header's controls where the column draws them (`headerWidthOf`), the
 * cells by what their text would take unclipped.
 */
export function contentWidthOf(
  column: GridColumnDto,
  index: number,
  rows: readonly HTMLTableRowElement[],
  measure: TextMeasure | null,
): number {
  const label = headerWidthOf(column, measure ?? (text => text.length * FALLBACK_CHAR_WIDTH));
  return Math.max(label, renderedContentWidth(rows, index));
}

/**
 * The width that draws a column `drawn` pixels wide beside columns of `others` together in a table spanning `room`,
 * where the columns share the table by their widths: `drawn` itself where the columns at their widths fill or overflow
 * the room, the table then as wide as their sum, or where the table spans none; otherwise the width whose share of the
 * room is `drawn`. The two meet where the columns just fill the room, so a dragged edge never jumps.
 */
export function sharedWidthOf(drawn: number, others: number, room: number): number {
  return room <= 0 || drawn + others >= room ? drawn : (drawn * others) / (room - drawn);
}

/** The rows the opening fit reads of a page: the pinned top rows, the first `FIT_ROWS` rows and the pinned bottom rows. */
export function fitSampleOf(page: GridPageDto<GridRow>): readonly GridRow[] {
  return [...(page.topRows ?? []), ...page.rows.slice(0, FIT_ROWS), ...(page.bottomRows ?? [])];
}

/**
 * The opening fit, taken once: every column the server left without a width — the selection column, a checkbox and a
 * wrapping column apart — takes the width of its label and of the sample's texts as the cells format them, measured by
 * `measure` in the table's own text (`tableMeasure`). A column that has its opening width keeps it, so paging and
 * sorting move nothing, unless `keepsWider`, which a picker's list passes at every page it reads: such a column is
 * fitted again to the sample and keeps the wider of that and the width it has (`GridColumnState.setOpeningWidth`), so
 * its columns only widen while it is open. Without a canvas nothing is fitted.
 */
export function fitColumnsAtOpening(
  state: GridColumnState,
  columns: readonly GridColumnDto[],
  sample: readonly GridRow[],
  measure: TextMeasure | null,
  locale: string,
  zone: string,
  keepsWider = false,
): void {
  if (!measure) {
    return;
  }
  for (const column of columns) {
    const path = column.path;
    const isOpened = path ? state.hasOpeningWidth(path) : false;
    if (path && (isOpened ? keepsWider : isFittable(column))) {
      const texts = sample.map(row => cellTextOf(column, row, locale, zone));
      state.setOpeningWidth(path, fitWidthOf(column, texts, measure), keepsWider);
    }
  }
}
