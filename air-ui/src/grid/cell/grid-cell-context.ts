import type { GridColumnDto, GridHeaderCellDto } from '../../core/generated/contracts';
import type { GridRow } from '../grid-row';

/** What a cell or a header component is given: the row (none for a header), the column and the value at its path. */
export interface GridCellContext {
  readonly row: GridRow | null;
  /**
   * The cell's column. A body or pinned cell holds the column as it stood when the set of columns last changed in
   * anything but a width, so its `width` does not follow a drag or a fit; a column header's does.
   */
  readonly column: GridColumnDto;
  readonly value: unknown;
  /** The row's index in the page; -1 for a header and for a pinned row. */
  readonly index: number;
  /** Where the row is pinned, when it is one the page pins above or below itself: not selectable, not active. */
  readonly pinned?: 'top' | 'bottom';
  /** The header cell this stands for, when it is a cell of a header row above the columns. */
  readonly header?: GridHeaderCellDto;
}
