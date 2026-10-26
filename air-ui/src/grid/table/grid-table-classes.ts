import type { GridColumnDto } from '../../core/generated/contracts';
import type { GridHeaderRowCell } from '../header/grid-header-rows';
import { stickyVariantOf } from './grid-sticky-columns';

/** The keyboard's ring of the table, drawn by the scrolling box around it, which the table's own outline would be clipped by. */
export const TABLE_RING =
  'has-[>table:focus-visible]:outline-solid has-[>table:focus-visible]:outline-2 has-[>table:focus-visible]:-outline-offset-2 has-[>table:focus-visible]:outline-accent';

/** The active cell's ring, while the table has the keyboard's focus. */
export const ACTIVE_CELL = 'in-focus-visible:inset-ring-2 in-focus-visible:inset-ring-accent';

export const HEADER_CELL = 'h-9 p-0 bg-surface-2 border-b border-r border-line text-left font-medium text-ink select-none overflow-hidden';
export const BODY_CELL =
  'h-9 p-0 border-0 border-r border-line overflow-hidden whitespace-nowrap hover:inset-ring-1 hover:inset-ring-cell-ring';
/**
 * A body cell a save changed: a triangle of 6 pixels in the `mark` role in its top right corner, a background image,
 * so it needs no positioning of the cell and lies over a painted cell's ground and a sticky cell's inherited one.
 */
export const CHANGED_CELL =
  'bg-[linear-gradient(to_bottom_left,var(--color-mark)_50%,transparent_50%)] bg-no-repeat bg-top-right bg-size-[6px_6px]';
/**
 * The cell being edited: what its editor draws under it, the messages of a refused save, is not clipped; it draws the
 * field's frame, a 1 px inset ring in `field-focus` kept under the pointer and over the active cell's ring, on the
 * field's ground, which wins over a sticky column's inherited ground and the active row's.
 */
export const EDITING_CELL = 'overflow-visible! inset-ring-1! inset-ring-field-focus! bg-field!';
/** The cell being edited while the messages of a refused save stand under it: its frame in the red hue's ink. */
export const REFUSED_CELL = 'overflow-visible! inset-ring-1! hue-red inset-ring-(--hue-ink)! bg-field!';
/** The filler after the last column: what the columns leave of the panel's width. */
export const FILLER = 'p-0 border-0 border-line';
/** A cell of a header row above the columns: 24 pixels, on the header's ground, its label centred. */
export const HEADER_ROW_CELL = 'h-6 p-0 bg-surface-2 border-b border-r border-line text-xs text-ink select-none overflow-hidden';
export const ROW = 'cursor-default';
/** A row the page pins above or below itself: on the header's ground, set off by a line, outside the scrolling. */
export const PINNED_ROW = 'cursor-default bg-surface-2';
export const PINNED_TOP_CELL = `${BODY_CELL} border-b`;
export const PINNED_BOTTOM_CELL = `${BODY_CELL} border-t`;

/** The first column of a group of more than one: a rule on its left, so the groups are told apart. */
const GROUP_START = 'border-l border-l-edge';

/**
 * The classes a column gives its header and body cells, by column index: its sticky variant where it has a left or a
 * right offset, and a group's rule where it starts one.
 */
export function columnClassesOf(
  columns: readonly GridColumnDto[],
  left: readonly number[],
  right: readonly number[],
  groupStarts: ReadonlySet<number>,
): string[] {
  return columns.map((column, index) => {
    const sticky = left[index] >= 0 || right[index] >= 0 ? `${stickyVariantOf(column)} bg-inherit` : '';
    return groupStarts.has(index) ? `${sticky} ${GROUP_START}` : sticky;
  });
}

/**
 * The classes of each header row's cells, by row and cell: a cell sticky with its columns takes its first column's
 * classes, any other cell only the rule of a group it starts.
 */
export function headerRowClassesOf(
  headerRows: readonly (readonly GridHeaderRowCell[])[],
  columnClasses: readonly string[],
  groupStarts: ReadonlySet<number>,
): string[][] {
  return headerRows.map(row =>
    row.map(item => {
      if (item.isSticky) {
        return columnClasses[item.index];
      }
      return groupStarts.has(item.index) ? GROUP_START : '';
    }),
  );
}

/**
 * A body or pinned cell's classes, in this order: its section's, its column's, the active cell's ring and the mark of a
 * cell a save changed.
 */
export function cellClassOf(sectionClass: string, columnClass: string, isActive: boolean, isChanged: boolean): string {
  const active = isActive ? ` ${ACTIVE_CELL}` : '';
  const changed = isChanged ? ` ${CHANGED_CELL}` : '';
  return `${sectionClass} ${columnClass}${active}${changed}`;
}

/** A body row's ground through the grid roles: stripes, a hovered row, a selected row, and the active row over both. */
export function rowClassesOf(index: number, isActive: boolean, isSelected: boolean, flagHue?: string | null): string {
  if (isActive) {
    return `${ROW} bg-row-active`;
  }
  if (isSelected) {
    return `${ROW} ${index % 2 === 0 ? 'bg-row-selected' : 'bg-row-selected-2'} hover:bg-row-hover`;
  }
  if (flagHue) {
    return `${ROW} hue-${flagHue} bg-(--btn-ground) hover:bg-(--btn-hover)`;
  }
  return `${ROW} ${index % 2 === 0 ? 'bg-row-stripe' : 'bg-surface'} hover:bg-row-hover`;
}

/**
 * The classes the cell being edited adds to its own: the field's frame, red in place of `field-focus` while a refused
 * save's messages stand, so no two rings' colours compete.
 */
export function editingCellClassOf(isRefused: boolean): string {
  return isRefused ? REFUSED_CELL : EDITING_CELL;
}
