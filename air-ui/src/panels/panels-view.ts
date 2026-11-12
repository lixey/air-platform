import type { PanelDirection, PanelLayoutDto, PanelNodeDto } from '../core/generated/contracts';
import { isFill, parseSize, toFlex } from './panel-size';
import type { PanelPref, PanelPrefs } from './panels-storage';
import { asFiller, clampStored, DEFAULT_MIN, makeRoom, maxSizeOf, minSizeOf, promoteFill } from './panels-view-fit';
import {
  collapsiblePanels,
  findView,
  pageOf,
  pagesOf,
  type PanelPage,
  type PanelSide,
  type PanelToggle,
  togglesFor,
} from './panels-view-queries';

export {
  collapsiblePanels,
  DEFAULT_MIN,
  findView,
  maxSizeOf,
  minSizeOf,
  pageOf,
  pagesOf,
  togglesFor,
  type PanelPage,
  type PanelSide,
  type PanelToggle,
};

/**
 * The container width under which a panel holding a size of its own hides itself, in a
 * row, when neither it nor its layout states a threshold.
 */
export const DEFAULT_HIDE_UNDER = 1280;

/**
 * Under this window width — Tailwind's `lg` — every panel but the one that fills and the
 * ones the screen pinned is hidden, whatever it holds and whatever the user chose.
 */
export const NARROW_LAYOUT = 1024;

/** Space between panels, and around the outermost layout. Tailwind's spacing 1. */
export const DEFAULT_GAP = 4;

/** What separates flush panels instead: one hairline. */
export const SEAM = 1;

/** How large each container is, along its own direction, keyed by path. */
export type PanelSizes = Readonly<Record<string, number>>;

/** How wide each container is, whatever its direction, keyed by path. What the paging threshold reads. */
export type PanelWidths = Readonly<Record<string, number>>;

/**
 * The page open in each exclusive container, keyed by the container's path: a page's key, or
 * `null` for none. A container absent here shows its first page. This visit's, never stored.
 */
export type PanelActive = Readonly<Record<string, string | null>>;

/**
 * What the layout looks like right now: the screen's definition, plus what the user
 * arranged, plus how large each container currently is. A pure function of those three —
 * no state of its own, nothing to keep in sync.
 */
export interface PanelView {
  /** The node as the server defined it. Never modified. */
  readonly node: PanelNodeDto;

  /** Address of this node: `side/log`. */
  readonly path: string;

  /** Its label, or the key when it has none. Used wherever a panel is named. */
  readonly name: string;

  /** Size in effect, after the user's own. */
  readonly size: string;

  /** That size as a CSS `flex` shorthand. */
  readonly flex: string;

  /** It takes the leftover space of its container: it fills by its size, or it was made the one that fills. */
  readonly isFiller: boolean;

  readonly isContainer: boolean;
  readonly isCollapsed: boolean;

  /**
   * The screen gave it a size of its own rather than letting it fill. Read from the
   * screen's definition, never from what the user has dragged.
   */
  readonly isSecondary: boolean;

  /** Whether the user is allowed to close it. */
  readonly canCollapse: boolean;

  /**
   * A container whose pinned children are always shown and whose other children are pages,
   * of which at most one is open.
   */
  readonly isExclusive: boolean;

  /**
   * An exclusive container one of whose pinned children has become a page, because the
   * container is narrower than that child's `pageUnder` or shorter than its `pageUnderHeight`.
   * A paged container always has a page open.
   */
  readonly isPaged: boolean;

  /** A child of an exclusive container that is not pinned right now: shown only while it is the open page. */
  readonly isPage: boolean;

  /** How this node arranges its own children. */
  readonly direction: PanelDirection;

  /** How the container around it runs — the axis this node is sized and closed along. */
  readonly axis: PanelDirection;

  /** Every child, closed ones included, so paths stay stable. */
  readonly children: readonly PanelView[];
}

/** What a layout is projected with besides itself: what the user arranged, what was measured, and how it is drawn. */
export interface PanelViewOptions {
  /** The user's arrangement, by panel path. */
  readonly prefs: PanelPrefs;

  /** How large each container is along its own direction, by path. */
  readonly sizes: PanelSizes;

  /** How wide each container is, by path: what the paging threshold reads. */
  readonly widths: PanelWidths;

  /** The page open in each exclusive container, by the container's path. */
  readonly active: PanelActive;

  /** The space between flush panels, in pixels; the layout's gap, or `DEFAULT_GAP`, when absent. */
  readonly seam?: number;

  /** Whether the window is narrower than `NARROW_LAYOUT`: every panel but the filling and the pinned ones is hidden. */
  readonly isNarrow: boolean;
}

/**
 * Builds the view.
 *
 * A panel closes when the screen says it starts closed, when its container is smaller than
 * its `hideUnder`, or when there is simply no room left for it. **The moment the user opens
 * or closes it themselves, their choice wins** — except over the last of those three: a
 * layout is never allowed to overflow, so opening one panel may push another aside.
 */
export function buildView(layout: PanelLayoutDto, options: PanelViewOptions): PanelView {
  const { seam, ...rest } = options;
  const context: BuildContext = { layout, ...rest, gap: seam ?? layout.gap ?? DEFAULT_GAP };
  return buildNode(layout.root, '', context, layout.root.direction ?? 'row');
}

/** What every node of one projection is built from. */
interface BuildContext extends Omit<PanelViewOptions, 'seam'> {
  readonly layout: PanelLayoutDto;
  readonly gap: number;
}

function buildNode(node: PanelNodeDto, path: string, context: BuildContext, axis: PanelDirection): PanelView {
  const isSecondary = !isFill(node.size ?? '1fr');
  const stored = node.isRigid ? undefined : context.prefs[path]?.size;
  const size = stored !== undefined ? clampStored(stored, node, axis) : (node.size ?? '1fr');
  const children = buildChildren(node, path, context);
  const isExclusive = !!node.isExclusive && children.length > 0;

  return {
    node,
    path,
    name: node.label ?? node.key,
    size,
    flex: toFlex(size, !!node.isRigid),
    isFiller: isFill(size),
    isContainer: children.length > 0,
    isCollapsed: false,
    isSecondary,
    canCollapse: node.isCollapsible ?? isSecondary,
    isExclusive,
    isPaged: isExclusive && children.some(child => !!child.node.isPinned && child.isPage),
    isPage: false,
    direction: node.direction ?? 'row',
    axis,
    children,
  };
}

function buildChildren(node: PanelNodeDto, path: string, context: BuildContext): PanelView[] {
  const nodes = node.children ?? [];

  if (nodes.length === 0) {
    return [];
  }

  const axis = node.direction ?? 'row';
  const children = nodes.map(child => buildNode(child, join(path, child.key), context, axis));

  if (node.isExclusive) {
    const height = axis === 'column' ? (context.sizes[path] ?? 0) : 0;
    return buildPages(children, context.active[path], context.widths[path] ?? 0, height);
  }

  const available = context.sizes[path] ?? 0;
  const closed = children.map(
    child => isForcedClosed(child, context.isNarrow) || isClosed(child, context.prefs, context.layout, available),
  );

  makeRoom(children, closed, context.prefs, available, context.gap);

  return promoteFill(children, closed);
}

function buildPages(children: readonly PanelView[], chosen: string | null | undefined, width: number, height: number): PanelView[] {
  const pinned = children.map(child => isPinnedNow(child, width, height));
  const pages = children.filter((_, index) => !pinned[index]);
  const isPaged = children.some((child, index) => !!child.node.isPinned && !pinned[index]);
  const active = activePage(chosen, pages, isPaged);

  const views = children.map((child, index) => ({
    ...child,
    isPage: !pinned[index],
    canCollapse: !pinned[index],
    isCollapsed: pinned[index] ? false : child.node.key !== active,
  }));

  const open = views.filter(view => !view.isCollapsed);
  const filler = open.find(view => view.isPage) ?? open[open.length - 1];

  return views.map(view => (view === filler ? asFiller(view) : view));
}

function isPinnedNow(child: PanelView, width: number, height: number): boolean {
  return !!child.node.isPinned && !under(width, child.node.pageUnder) && !under(height, child.node.pageUnderHeight);
}

function under(size: number, threshold: number | null | undefined): boolean {
  return threshold != null && size > 0 && size < threshold;
}

function activePage(chosen: string | null | undefined, pages: readonly PanelView[], isPaged: boolean): string | null {
  if (chosen !== undefined && (chosen === null ? !isPaged : pages.some(page => page.node.key === chosen))) {
    return chosen;
  }

  return pages[0]?.node.key ?? null;
}

function isClosed(child: PanelView, prefs: PanelPrefs, layout: PanelLayoutDto, available: number): boolean {
  if (!child.canCollapse) {
    return false;
  }

  const threshold = thresholdOf(child, layout);
  const choice = prefs[child.path];

  if (choice?.isCollapsed !== undefined && holds(choice, threshold, available)) {
    return choice.isCollapsed;
  }

  return (!!threshold && available > 0 && available < threshold) || !!child.node.isCollapsed;
}

/**
 * Below `lg` every panel but the one that fills and the ones the screen pinned is hidden,
 * before any choice or threshold is read. A page is outside the rule.
 */
export function isForcedClosed(child: PanelView, isNarrow: boolean): boolean {
  return isNarrow && child.isSecondary && !child.node.isPinned && !child.isPage;
}

/**
 * The size under which a panel hides itself: what it states, what its layout states for
 * every panel that does not fill, or the library's default along a row.
 */
export function thresholdOf(child: PanelView, layout: PanelLayoutDto): number | undefined {
  if (child.isPage) {
    return undefined;
  }

  if (child.node.hideUnder != null) {
    return child.node.hideUnder;
  }

  if (!child.isSecondary) {
    return undefined;
  }

  if (layout.hideSecondaryUnder != null) {
    return layout.hideSecondaryUnder;
  }

  if (child.axis !== 'row' || parseSize(child.node.size ?? '1fr').unit === 'auto') {
    return undefined;
  }

  return DEFAULT_HIDE_UNDER;
}

function holds(choice: PanelPref, threshold: number | undefined, available: number): boolean {
  if (!threshold) {
    return true;
  }

  if (choice.at === undefined) {
    return false;
  }

  return available <= 0 || isSameSide(choice.at, available, threshold);
}

function isStale(choice: PanelPref | undefined, threshold: number | undefined, available: number): boolean {
  if (choice?.isCollapsed === undefined || choice.at === undefined) {
    return false;
  }

  if (!threshold || available <= 0) {
    return false;
  }

  return !isSameSide(choice.at, available, threshold);
}

function isSameSide(at: number, available: number, threshold: number): boolean {
  return at < threshold === available < threshold;
}

/**
 * The choices whose reason has gone: the container has crossed the panel's threshold since
 * the choice was made.
 */
export function staleChoices(view: PanelView, prefs: PanelPrefs, sizes: PanelSizes, layout: PanelLayoutDto): string[] {
  const stale: string[] = [];

  const walk = (container: PanelView): void => {
    const available = sizes[container.path] ?? 0;

    for (const child of container.children) {
      const choice = prefs[child.path];
      const threshold = container.isExclusive ? undefined : thresholdOf(child, layout);

      if (isStale(choice, threshold, available)) {
        stale.push(child.path);
      }

      walk(child);
    }
  };

  walk(view);
  return stale;
}

function join(path: string, key: string): string {
  return path ? `${path}/${key}` : key;
}
