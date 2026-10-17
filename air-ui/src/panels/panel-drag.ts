import { fromPixels, isFill } from './panel-size';
import { maxSizeOf, minSizeOf, type PanelView } from './panels-view';

/** One neighbour of a divider, as the drag found it. */
export interface DragSide {
  readonly path: string;
  /** Measured size when the drag began, in pixels. */
  readonly from: number;
  readonly min: number;
  readonly max: number;
  /** Whether a move writes this panel's size. A filling neighbour absorbs instead. */
  readonly isWritten: boolean;
}

/** Everything a drag needs, read once when it begins. */
export interface DragPlan {
  readonly before: DragSide;
  readonly after: DragSide;
}

/**
 * Which of a divider's two neighbours a move writes.
 *
 * A neighbour that fills is never written: it takes whatever the other gives up. Every
 * other neighbour is, so that when neither fills both are and the divider still moves by
 * exactly what the pointer moved. When both fill there is nothing to absorb the change,
 * and the one marked resizable is given a size of its own.
 */
export function writtenSides(before: PanelView, after: PanelView): { before: boolean; after: boolean } {
  const sides = { before: !isFill(before.size), after: !isFill(after.size) };

  if (!sides.before && !sides.after) {
    return before.node.isResizable || !after.node.isResizable ? { before: true, after: false } : { before: false, after: true };
  }

  return sides;
}

export function planDrag(before: PanelView, after: PanelView, beforePx: number, afterPx: number): DragPlan {
  const written = writtenSides(before, after);

  return {
    before: side(before, beforePx, written.before),
    after: side(after, afterPx, written.after),
  };
}

/**
 * The sizes to write for a divider moved `delta` pixels from where the drag began. The pair
 * share what they had between them; each stays within its own bounds and neither is taken
 * below its minimum.
 */
export function moveDrag(plan: DragPlan, delta: number): readonly { path: string; size: string }[] {
  const { before, after } = plan;
  const total = before.from + after.from;
  const low = Math.max(before.min, total - after.max);
  const high = Math.min(before.max, total - after.min);
  const next = Math.min(Math.max(before.from + delta, low), Math.max(low, high));
  const writes = [];

  if (before.isWritten) {
    writes.push({ path: before.path, size: fromPixels(next) });
  }

  if (after.isWritten) {
    // Never below its minimum, even when the pair measured less than their minimums add
    // up to — a layout not yet laid out, or one measured while hidden.
    writes.push({ path: after.path, size: fromPixels(Math.max(after.min, total - next)) });
  }

  return writes;
}

function side(view: PanelView, from: number, isWritten: boolean): DragSide {
  return {
    path: view.path,
    from,
    min: minSizeOf(view),
    max: maxSizeOf(view),
    isWritten,
  };
}
