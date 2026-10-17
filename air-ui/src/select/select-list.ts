/**
 * The least height of an option of a static list, in pixels: a row as tall as a ghost button. The overlay draws the
 * list by this and the two numbers below, and `SelectService` sizes a short list by the same three.
 */
export const OPTION_MIN_HEIGHT = 36;

/** The space between two options of a static list, in pixels. */
export const OPTION_GAP = 1;

/** The padding of a static list's listbox on each side, in pixels. */
export const LIST_PADDING = 4;

/** One option of a static list: a row of the list, as a ghost button draws its label; its least height is `OPTION_MIN_HEIGHT`. */
export const OPTION = 'flex items-center px-3 rounded-lg text-sm text-left text-ink cursor-pointer select-none hover:bg-hover';

/** The width of one column of a grid's list in the overlay, in pixels. */
const COLUMN_WIDTH = 180;

/** The least a select's grid list takes, in pixels. */
export const LEAST_LIST_WIDTH = 360;

/** The most the overlay takes, the panel of the picked included, in pixels. */
export const MOST_OVERLAY_WIDTH = 1200;

/**
 * The height of the header over a list, in pixels: a select's bar and the nav of a picker's list. The menu states it
 * with the list's height, so the host opens it where both fit; a select's card that stands above its field keeps the
 * stated height, and a picker's list hangs under its field only where the stated height fits.
 */
export const BAR_HEIGHT = 48;

/** The height of a grid's list: the platform's select height, about nine rows, whatever the window's height. */
export const SELECT_LIST_HEIGHT = 384;

/** The width a grid's list of `columns` columns wants: a column each, between the least list and the most the overlay takes. */
export function gridListWidth(columns: number): number {
  return Math.min(MOST_OVERLAY_WIDTH, Math.max(LEAST_LIST_WIDTH, Math.max(1, columns) * COLUMN_WIDTH));
}
