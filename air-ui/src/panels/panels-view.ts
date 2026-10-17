import type { PanelDirection, PanelLayoutDto, PanelNodeDto } from '../core/generated/contracts';
import { fromPixels, isFill, parseSize, toBound, toFlex } from './panel-size';
import type { PanelPref, PanelPrefs } from './panels-storage';

/**
 * The least a panel holding a size of its own may be, per axis, when the screen states no
 * minimum. A panel sized to its content has none: its content is its size.
 */
export const DEFAULT_MIN: Readonly<Record<PanelDirection, number>> = {
  row: 280,
  column: 160,
};

/**
 * The container width under which a panel holding a size of its own hides itself, in a
 * row, when neither it nor its layout states a threshold. Beside the frame's navigation a
 * screen's row is this narrow at a window of about 1552 pixels, and the navigation's own
 * threshold of the same number is met at about 1288 — the `2xl` and `xl` breakpoints. A
 * column has no default: a height is not a width. A panel sized to its content has none
 * either, for the reason it has no minimum: it takes what its content takes, and in a box
 * sized by that content — an overlay — a threshold would measure the panel against its own
 * width, close it, measure nothing, and open it again.
 */
export const DEFAULT_HIDE_UNDER = 1280;

/**
 * Under this window width — Tailwind's `lg` — every panel but the one that fills and the
 * ones the screen pinned is hidden, whatever it holds and whatever the user chose. Pixels
 * rather than a rem: a media query is measured against the initial font size, so the root
 * font this product changes with the window cannot move it. The one place the engine reads
 * the window rather than a container — this rule is about the device, not about what fits.
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
  // What a panel *is* comes from the screen. What size it *has* may come from the user —
  // dragging the workspace to a width does not turn it into a side panel. A rigid panel is
  // its content and nothing else: a size stored before it was rigid is not read.
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
    // A container measures itself along its direction, so a column's height is
    // known; a row's is not, and the server refuses a height threshold there
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

/**
 * The children of an exclusive container. The pinned ones are always shown and obey nothing
 * else — no threshold, no making room, no window rule; a pinned child whose container is
 * narrower than its `pageUnder` or shorter than its `pageUnderHeight` has become a page. Of
 * the pages one at most is open: the one chosen this visit while the layout still holds it,
 * else the first. The open page fills whatever it states; with no page open the last shown
 * pinned child fills instead.
 */
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

/** Whether a child of an exclusive container is pinned at this size: pinned, and under neither of its paging thresholds. */
function isPinnedNow(child: PanelView, width: number, height: number): boolean {
  return !!child.node.isPinned && !under(width, child.node.pageUnder) && !under(height, child.node.pageUnderHeight);
}

/** Whether a measured size has crossed a threshold; an unmeasured size, 0, crosses none. */
function under(size: number, threshold: number | null | undefined): boolean {
  return threshold != null && size > 0 && size < threshold;
}

/**
 * The key of the page to open: the user's choice when it names a page the layout still has —
 * `null` meaning none, which a paged container refuses — else the first page. The choice holds
 * on both sides of the paging width: crossing it changes which children are pages, not which
 * one the user asked for.
 */
function activePage(chosen: string | null | undefined, pages: readonly PanelView[], isPaged: boolean): string | null {
  if (chosen !== undefined && (chosen === null ? !isPaged : pages.some(page => page.node.key === chosen))) {
    return chosen;
  }

  return pages[0]?.node.key ?? null;
}

/** Everything the screen and the user have to say about whether a panel is open. */
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
 * before any choice or threshold is read: whatever size it holds, on either axis, with a
 * threshold or without. What a narrow window has room for is the main panel and what the
 * screen says it cannot do without — the frame's strip, a nav a screen needs on a phone.
 * A page is outside the rule: switching pages is what a narrow window needs.
 */
export function isForcedClosed(child: PanelView, isNarrow: boolean): boolean {
  return isNarrow && child.isSecondary && !child.node.isPinned && !child.isPage;
}

/**
 * The size under which a panel hides itself: what it states, what its layout states for
 * every panel that does not fill, or the library's default along a row — which reaches a
 * panel holding a size of its own and leaves one sized to its content alone.
 */
export function thresholdOf(child: PanelView, layout: PanelLayoutDto): number | undefined {
  // A page is governed by the exclusive rule alone.
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

  // The library's default is for a panel holding a size of its own, as the minimum is.
  if (child.axis !== 'row' || parseSize(child.node.size ?? '1fr').unit === 'auto') {
    return undefined;
  }

  return DEFAULT_HIDE_UNDER;
}

/**
 * Whether a user's choice still applies. With no threshold it always does. With one, it
 * applies while the container is on the side of the threshold the choice was made on — and
 * a choice that never recorded its side does not apply at all.
 */
function holds(choice: PanelPref, threshold: number | undefined, available: number): boolean {
  if (!threshold) {
    return true;
  }

  if (choice.at === undefined) {
    return false;
  }

  return available <= 0 || isSameSide(choice.at, available, threshold);
}

/** Whether the container has crossed the panel's threshold since the user's choice was made. */
function isStale(choice: PanelPref | undefined, threshold: number | undefined, available: number): boolean {
  if (choice?.isCollapsed === undefined || choice.at === undefined) {
    return false;
  }

  if (!threshold || available <= 0) {
    return false;
  }

  return !isSameSide(choice.at, available, threshold);
}

/** Whether two sizes stand on the same side of a threshold. */
function isSameSide(at: number, available: number, threshold: number): boolean {
  return at < threshold === available < threshold;
}

/**
 * The choices whose reason has gone: the container has crossed the panel's threshold since
 * the choice was made. The layout host forgets them, so the threshold decides again — and
 * goes on deciding when the container comes back to the side the choice was made on. The
 * choice of a page is not one of them: it is this visit's and holds at every width.
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
function clampStored(stored: string, node: PanelNodeDto, axis: PanelDirection): string {
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
function makeRoom(children: readonly PanelView[], closed: boolean[], prefs: PanelPrefs, available: number, gap: number): void {
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
function promoteFill(children: readonly PanelView[], closed: readonly boolean[]): PanelView[] {
  const open = children.filter((_, index) => !closed[index]);
  const needsFiller = open.length > 0 && children.some(child => isFill(child.size)) && !open.some(child => isFill(child.size));
  const filler = needsFiller ? open[open.length - 1] : null;

  return children.map((child, index) => {
    const view = { ...child, isCollapsed: closed[index] };
    return child === filler ? asFiller(view) : view;
  });
}

/** A view made the one that fills its container, whatever size it holds. */
function asFiller(view: PanelView): PanelView {
  return { ...view, flex: toFlex('1fr'), isFiller: true };
}

function join(path: string, key: string): string {
  return path ? `${path}/${key}` : key;
}

/** Which edge a panel sits on, relative to the one that fills its container. */
export type PanelSide = 'left' | 'right' | 'top' | 'bottom';

/** Everything needed to draw one show/hide control: what it is, where it is, how it stands. */
export interface PanelToggle {
  readonly path: string;
  readonly name: string;
  readonly isCollapsed: boolean;
  readonly side: PanelSide;
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

/** One page of an exclusive container, as a tab draws it. */
export interface PanelPage {
  readonly path: string;
  readonly name: string;
  readonly isOpen: boolean;
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

function describePage(page: PanelView): PanelPage {
  return { path: page.path, name: page.name, isOpen: !page.isCollapsed };
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
