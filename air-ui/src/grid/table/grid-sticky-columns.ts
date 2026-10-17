import type { GridColumnDto } from '../../core/generated/contracts';
import { SELECTION_PATH } from '../column/grid-selection-column';

/** The left offset of each sticky column: the widths of the sticky columns before it; -1 for the rest. */
export function leftOffsetsOf(columns: readonly GridColumnDto[], widths: readonly number[]): number[] {
  let cumulative = 0;
  return columns.map((column, index) => {
    if (!column.isSticky) {
      return -1;
    }
    const offset = cumulative;
    cumulative += widths[index];
    return offset;
  });
}

/** The right offset of each column sticky to the right: the widths of such columns after it; -1 for the rest. */
export function rightOffsetsOf(columns: readonly GridColumnDto[], widths: readonly number[]): number[] {
  const offsets = new Array<number>(columns.length).fill(-1);
  let cumulative = 0;
  for (let index = columns.length - 1; index >= 0; index -= 1) {
    if (columns[index].isStickyRight) {
      offsets[index] = cumulative;
      cumulative += widths[index];
    }
  }
  return offsets;
}

/**
 * The selection column keeps its edge at every width; every other sticky column from `lg` up.
 * The edge's offset is a custom property the variant reads, so a static cell is not shifted by
 * it; the stacking is the variant's too, so a header cell made `relative` by its resize handle
 * does not cover the selection column's sticky cell on a narrow window.
 */
export function stickyVariantOf(column: GridColumnDto | undefined): string {
  return column?.path === SELECTION_PATH
    ? 'sticky z-10 left-(--sticky-left) right-(--sticky-right)'
    : 'lg:sticky lg:z-10 lg:left-(--sticky-left) lg:right-(--sticky-right)';
}

/** How far the sticky columns of one edge reach into the viewport: the widest offset plus its column's width. */
export function stickyEdgeOf(offsets: readonly number[], widths: readonly number[]): number {
  return offsets.reduce((edge, offset, index) => (offset >= 0 ? Math.max(edge, offset + widths[index]) : edge), 0);
}

/** A sticky offset as a CSS value, which the sticky variant alone reads; null for a column that does not stick there. */
export function stickyValueOf(offset: number): string | null {
  return offset >= 0 ? `${offset}px` : null;
}
