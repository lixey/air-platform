import type { PanelDirection, PanelNodeDto } from '../core/generated/contracts';
import { fromPixels, isFill, parseSize, toBound, toFlex } from './panel-size';
import type { PanelPrefs } from './panels-storage';
import type { PanelView } from './panels-view';

/**
 * The least a panel holding a size of its own may be, per axis, when the screen states no
 * minimum. A panel sized to its content has none: its content is its size.
 */
export const DEFAULT_MIN: Readonly<Record<PanelDirection, number>> = {
  row: 280,
  column: 160,
};

/** The least a panel may be: what the screen states, or the default for its axis. */
export function minSizeOf(view: PanelView): number {
  return boundsOf(view.node, view.axis).min;
}

/** The most a panel may be: what the screen states, or no ceiling at all. */
export function maxSizeOf(view: PanelView): number {
  return boundsOf(view.node, view.axis).max;
}

function boundsOf(node: PanelNodeDto, axis: PanelDirection): { min: number; max: number } {
  const isAuto = parseSize(node.size ?? '1fr').unit === 'auto';

  return {
    min: toBound(node.minSize) ?? (isAuto ? 0 : DEFAULT_MIN[axis]),
    max: toBound(node.maxSize) ?? Number.POSITIVE_INFINITY,
  };
}

/** A stored size is held to the bounds the screen has now, which may have moved since. */
export function clampStored(stored: string, node: PanelNodeDto, axis: PanelDirection): string {
  const { value, unit } = parseSize(stored);

  if (unit !== 'px') {
    return stored;
  }

  const { min, max } = boundsOf(node, axis);
  return fromPixels(Math.min(Math.max(value, min), Math.max(min, max)));
}

/**
 * Closes panels until the rest fit. Panels the user has not touched step aside first, so
 * opening one deliberately pushes the automatic ones out of the way rather than itself.
 */
export function makeRoom(children: readonly PanelView[], closed: boolean[], prefs: PanelPrefs, available: number, gap: number): void {
  if (available <= 0) {
    return;
  }

  const order = children
    .map((child, index) => ({ child, index }))
    .filter(({ child, index }) => child.canCollapse && child.isSecondary && !closed[index])
    .sort((a, b) => rank(a) - rank(b));

  function rank({ child, index }: { child: PanelView; index: number }): number {
    // Untouched panels first, then the ones the user opened on purpose; last declared first.
    const wasOpenedOnPurpose = prefs[child.path]?.isCollapsed === false ? 1 : 0;
    return wasOpenedOnPurpose * children.length * 2 - index;
  }

  for (const { index } of order) {
    if (needed(children, closed, gap) <= available) {
      return;
    }

    closed[index] = true;
  }
}

/** How much room the open panels ask for, at their smallest. */
function needed(children: readonly PanelView[], closed: readonly boolean[], gap: number): number {
  const open = children.filter((_, index) => !closed[index]);
  const gaps = Math.max(open.length - 1, 0) * gap;

  return open.reduce((total, child) => total + smallest(child, gap), gaps);
}

/**
 * The least room a panel can be given.
 *
 * A container that runs the same way as its parent asks for whatever its own open panels
 * add up to. One that runs across its parent cannot: nothing in the model says how narrow
 * a column may get, so give such a container a `minSize` of its own.
 */
function smallest(child: PanelView, gap: number): number {
  const min = minSizeOf(child);
  const { value, unit } = parseSize(child.size);
  const own = unit === 'px' ? Math.max(value, min) : min;

  if (!child.isContainer || child.direction !== child.axis) {
    return own;
  }

  const inner = child.children.map(grandchild => grandchild.isCollapsed);
  return Math.max(own, needed(child.children, inner, gap));
}

/**
 * Something always fills. Close the panel that was taking the leftover space and the last
 * one still open takes it instead, rather than leaving a gap at the end. A box that never
 * had a filling panel — an overlay sized by its content — is left as the screen wrote it:
 * promoting its one content-sized panel would give the box the window's height instead.
 */
export function promoteFill(children: readonly PanelView[], closed: readonly boolean[]): PanelView[] {
  const open = children.filter((_, index) => !closed[index]);
  const needsFiller = open.length > 0 && children.some(child => isFill(child.size)) && !open.some(child => isFill(child.size));
  const filler = needsFiller ? open[open.length - 1] : null;

  return children.map((child, index) => {
    const view = { ...child, isCollapsed: closed[index] };
    return child === filler ? asFiller(view) : view;
  });
}

/** A view made the one that fills its container, whatever size it holds. */
export function asFiller(view: PanelView): PanelView {
  return { ...view, flex: toFlex('1fr'), isFiller: true };
}
