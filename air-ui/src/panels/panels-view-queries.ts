import type { PanelDirection } from '../core/generated/contracts';
import type { PanelView } from './panels-view';

/** Which edge a panel sits on, relative to the one that fills its container. */
export type PanelSide = 'left' | 'right' | 'top' | 'bottom';

/** Everything needed to draw one show/hide control: what it is, where it is, how it stands. */
export interface PanelToggle {
  readonly path: string;
  readonly name: string;
  readonly isCollapsed: boolean;
  readonly side: PanelSide;
}

/** One page of an exclusive container, as a tab draws it. */
export interface PanelPage {
  readonly path: string;
  readonly name: string;
  readonly isOpen: boolean;
}

/** Every panel the user is allowed to close, in layout order, with how each one stands. */
export function collapsiblePanels(view: PanelView): PanelToggle[] {
  const found: PanelToggle[] = [];

  const walk = (container: PanelView): void => {
    const filler = fillerIndex(container);

    container.children.forEach((child, index) => {
      if (child.canCollapse) {
        found.push(describe(child, container, index, filler));
      }

      walk(child);
    });
  };

  walk(view);
  return found;
}

/**
 * The controls that belong in one panel's own nav.
 *
 * A panel that fills its container speaks for the panels around it, so its nav carries
 * their controls — and, while it goes on filling, those of the containers above it too.
 * Ask any other panel and you get nothing: it is not the one in charge.
 */
export function togglesFor(view: PanelView, path: string): PanelToggle[] {
  const ancestors: PanelView[] = [];
  const node = trace(view, path, ancestors);

  if (!node) {
    return [];
  }

  const found: PanelToggle[] = [];
  let current = node;

  for (let i = ancestors.length - 1; i >= 0; i--) {
    const container = ancestors[i];
    const filler = fillerIndex(container);

    if (container.children[filler] !== current) {
      break;
    }

    container.children.forEach((child, index) => {
      if (child !== current && child.canCollapse) {
        found.push(describe(child, container, index, filler));
      }
    });

    current = container;
  }

  return found;
}

/**
 * The pages of the exclusive container at `path`, in declared order — a pinned child that
 * has become a page first — each with whether it is open. Nothing for an ordinary container.
 */
export function pagesOf(view: PanelView, path: string): PanelPage[] {
  const container = findView(view, path);

  if (!container?.isExclusive) {
    return [];
  }

  return container.children.filter(child => child.isPage).map(describePage);
}

/** The page at `path` and the exclusive container it belongs to, or nothing for a panel that is not a page. */
export function pageOf(view: PanelView, path: string): { container: string; page: PanelPage } | undefined {
  const ancestors: PanelView[] = [];
  const found = trace(view, path, ancestors);
  const container = ancestors[ancestors.length - 1];

  if (!found?.isPage || !container?.isExclusive) {
    return undefined;
  }

  return { container: container.path, page: describePage(found) };
}

/** Finds a node in the view by path. */
export function findView(view: PanelView, path: string): PanelView | undefined {
  if (view.path === path) {
    return view;
  }

  for (const child of view.children) {
    const found = findView(child, path);

    if (found) {
      return found;
    }
  }

  return undefined;
}

/** Walks to `path`, collecting the containers passed through on the way. */
function trace(node: PanelView, path: string, ancestors: PanelView[]): PanelView | null {
  if (node.path === path) {
    return node;
  }

  for (const child of node.children) {
    ancestors.push(node);

    const found = trace(child, path, ancestors);
    if (found) {
      return found;
    }

    ancestors.pop();
  }

  return null;
}

/** The child taking the leftover space, which is the one the others are arranged around. */
function fillerIndex(container: PanelView): number {
  return container.children.findIndex(child => !child.isCollapsed && child.isFiller);
}

function describe(child: PanelView, container: PanelView, index: number, filler: number): PanelToggle {
  const isBefore = filler >= 0 && index < filler;

  return {
    path: child.path,
    name: child.name,
    isCollapsed: child.isCollapsed,
    side: sideOf(container.direction, isBefore),
  };
}

function sideOf(direction: PanelDirection, isBefore: boolean): PanelSide {
  if (direction === 'column') {
    return isBefore ? 'top' : 'bottom';
  }
  return isBefore ? 'left' : 'right';
}

function describePage(page: PanelView): PanelPage {
  return { path: page.path, name: page.name, isOpen: !page.isCollapsed };
}
