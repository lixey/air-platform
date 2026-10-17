import { InjectionToken, type Signal } from '@angular/core';
import type { PanelPage, PanelToggle, PanelView } from './panels-view';

/**
 * How anything inside a layout opens and closes its panels — a nav, a panel's own
 * header, a keyboard shortcut.
 *
 * ```ts
 * private readonly panels = inject(PANELS);
 * protected readonly toggles = this.panels.togglePanels;   // draw your own buttons
 * // ...
 * this.panels.toggle('details');
 * ```
 *
 * Panels are addressed by the path they have: `side/log`, or just `nav` at the top. A
 * layout nested inside another reaches it through `parent`, or the outermost one through
 * `root`.
 */
export interface Panels {
  /** 0 for the outermost layout on the screen, 1 for one nested inside it, and so on. */
  readonly level: number;

  /** The layout this one sits inside, or `null` at the top. */
  readonly parent: Panels | null;

  /** The outermost layout on the screen. `this`, at the top. */
  readonly root: Panels;

  /** Every panel of this layout the user may close, with its current state. */
  readonly togglePanels: Signal<readonly PanelToggle[]>;

  /**
   * The controls belonging in one panel's own nav: the panels arranged around it, while
   * it is the one filling its container. Any other panel gets nothing.
   */
  togglesFor(path: string): readonly PanelToggle[];

  /**
   * The pages of the exclusive container at that path, in declared order, each with whether
   * it is open; nothing for an ordinary container. Not emptied below `lg`: switching pages is
   * what a narrow window needs.
   */
  pagesOf(path: string): readonly PanelPage[];

  /** The page at that path and the container it belongs to, or nothing for a panel that is not a page. */
  pageOf(path: string): { container: string; page: PanelPage } | undefined;

  /** Whether that panel is currently closed. */
  isCollapsed(path: string): boolean;

  /**
   * Closes it if open, opens it if closed. On a page: opening it closes the page that was
   * open; closing the open page leaves none open, or opens the first page while the
   * container is paged.
   */
  toggle(path: string): void;

  open(path: string): void;

  close(path: string): void;

  /** Forgets what the user changed — one panel, or the whole layout when given nothing. */
  reset(path?: string): void;
}

export const PANELS = new InjectionToken<Panels>('PANELS');

/**
 * The panel a component stands in, reached through the injector, and the chain above it.
 * A nested layout's root node reports the outer layout's leaf as its parent, so the chain
 * crosses layouts: a nav in a grid mounted in a page walks up to the page, then to the
 * exclusive container around it, without being told a path.
 */
export interface PanelNode {
  /** The node's path in its own layout: `side/log`, or `` for a layout's root. */
  readonly path: Signal<string>;

  /** The node as it looks right now. */
  readonly view: Signal<PanelView>;

  /** The layout host the node belongs to. */
  readonly panels: Panels;

  /** The node above, in this layout or the one this layout is mounted in; `null` at the top of the screen. */
  readonly parent: PanelNode | null;
}

export const PANEL_NODE = new InjectionToken<PanelNode>('PANEL_NODE');

/**
 * The nearest exclusive container around a node, found by walking up the chain across nested
 * layouts, or `null` outside one. What a nav's tabs and a page's strip both ask.
 */
export function exclusiveContainerOf(node: PanelNode | null): PanelNode | null {
  for (let member = node; member; member = member.parent) {
    const container = member.parent;

    if (container && container.view().isExclusive && container.panels === member.panels) {
      return container;
    }
  }

  return null;
}

/**
 * How a layout is drawn: `cards`, each panel its own card a gap apart, or `flush`, the
 * panels edge to edge with a hairline between them and no card of their own. Given to a
 * layout by whatever mounts it — the overlay host provides `flush` — and inherited by a
 * layout nested inside. Absent, a layout is `cards`.
 */
export type PanelDensity = 'cards' | 'flush';

export const PANEL_DENSITY = new InjectionToken<PanelDensity>('PANEL_DENSITY');

/**
 * Whether the window's `lg` rule reaches a layout: below 1024 pixels of window a routed
 * layout keeps only its filling and its pinned panels. Absent, the rule applies. Whatever
 * mounts a layout may provide `false` — the overlay host does, because an overlay is a
 * window of its own and holds what its model says at every width — and a layout nested
 * inside inherits it, as it inherits the density.
 */
export const PANEL_WINDOW_RULE = new InjectionToken<boolean>('PANEL_WINDOW_RULE');
