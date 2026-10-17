import { Component, computed, DestroyRef, effect, inject, input, signal, type Signal, untracked } from '@angular/core';
import type { PanelLayoutDto } from '../core/generated/contracts';
import { provideScreenMutations, ScreenMutations } from '../screen/screen-mutations';
import { PanelNodeComponent } from './panel-node.component';
import { PanelsContext } from './panels-context';
import { PanelsState } from './panels-state';
import { PANEL_DENSITY, PANEL_WINDOW_RULE, PANELS, type Panels } from './panels-tokens';
import {
  buildView,
  collapsiblePanels,
  DEFAULT_GAP,
  findView,
  isForcedClosed,
  NARROW_LAYOUT,
  pageOf,
  pagesOf,
  type PanelPage,
  type PanelToggle,
  type PanelView,
  SEAM,
  staleChoices,
  togglesFor,
} from './panels-view';

/**
 * Renders a panel layout.
 *
 * ```html
 * <ui-panels [layout]="screen().layout" [data]="screen().data" />
 * ```
 *
 * The layout comes from the server and is never modified. What the user drags and closes
 * is kept apart in `PanelsState`; what you see is the two of them combined, together with
 * how much room each container actually has. That is why a new release can change a screen
 * without throwing away how people had arranged it.
 *
 * It provides four things to everything inside it: `PANELS` to open and close panels,
 * `PanelsContext` for the panels to share values, the screen's mutation scope — the one around
 * it, or under none its own, started over when the layout is replaced — and, where one of its
 * panels renders a layout of its own, a parent for that layout to reach through.
 */
@Component({
  selector: 'ui-panels',
  imports: [PanelNodeComponent],
  template: '<ui-panel-node [view]="view()" [data]="data()" />',
  host: {
    class: 'flex w-full h-full min-w-0 min-h-0 text-ink',
    '[style.--ui-panel-gap]': 'gap()',
    '[style.padding]': 'padding()',
  },
  providers: [PanelsState, PanelsContext, { provide: PANELS, useExisting: PanelsComponent }, provideScreenMutations()],
})
export class PanelsComponent implements Panels {
  private readonly state = inject(PanelsState);

  /** How the screen is arranged. Comes from the server; read-only here. */
  readonly layout = input.required<PanelLayoutDto>();

  /** The screen's data. Reaches any panel component that declares a `data` input. */
  readonly data = input<unknown>();

  /** The layout this one sits inside, or `null` at the top. */
  readonly parent = inject(PANELS, { optional: true, skipSelf: true });

  /** 0 for the outermost layout on the screen, 1 for one nested inside it, and so on. */
  readonly level: number = this.parent ? this.parent.level + 1 : 0;

  /** The outermost layout on the screen. */
  readonly root: Panels = this.parent ? this.parent.root : this;

  /** Cards a gap apart, or flush panels a hairline apart. Whoever mounted the layout says. */
  readonly density = inject(PANEL_DENSITY, { optional: true }) ?? 'cards';

  /**
   * Whether the window is narrower than `lg` — the one thing read from the window, and only
   * for a layout the rule reaches. Whatever mounted this one may have said it does not, as
   * the overlay host does; then the answer is `false` at every width and the query is never
   * read.
   */
  readonly isNarrow: Signal<boolean> = narrowWindow(inject(DestroyRef), inject(PANEL_WINDOW_RULE, { optional: true }));

  protected readonly view = computed(() =>
    buildView(this.layout(), {
      prefs: this.state.prefs(),
      sizes: this.state.sizes(),
      widths: this.state.widths(),
      active: this.state.active(),
      seam: this.density === 'flush' ? SEAM : undefined,
      isNarrow: this.isNarrow(),
    }),
  );

  /**
   * Every panel of this layout the user may close, with its current state. Nothing below
   * `lg`: a control for a panel that cannot be shown is a lie.
   */
  readonly togglePanels = computed<readonly PanelToggle[]>(() => (this.isNarrow() ? [] : collapsiblePanels(this.view())));

  /**
   * A layout mounted inside another says nothing about the gap unless it means to, and
   * inherits the one around it — so a nested screen lines up with its host instead of
   * ruling its own grid.
   */
  protected readonly gap = computed(() => {
    const gap = this.layout().gap;

    if (gap !== undefined) {
      return `${gap}px`;
    }

    return this.level === 0 ? `${DEFAULT_GAP}px` : null;
  });

  /**
   * The outermost layout keeps the same distance from the window edge as its panels keep
   * from each other, so one number governs the whole grid. A nested one adds none: the gap
   * around it is already there. A flush layout adds none either: its panels reach the edge
   * of whatever mounted it. The gap variable itself is kept in every case, because it is
   * also the unit everything inside spaces itself by.
   */
  protected readonly padding = computed(() => (this.level === 0 && this.density === 'cards' ? 'var(--ui-panel-gap)' : null));

  constructor() {
    // Under a screen or a layout the scope is theirs to reset; under none it is this layout's own, and a replaced
    // layout starts it over.
    if (!inject(ScreenMutations, { optional: true, skipSelf: true })) {
      const scope = inject(ScreenMutations);
      let key: string | undefined;
      effect(() => {
        const next = this.layout().key;
        if (key !== undefined && next !== key) {
          untracked(() => scope.reset());
        }
        key = next;
      });
    }
    effect(() => this.state.open(this.layout().key));

    // A user's open or close lasts as long as its reason: once the container has crossed
    // the panel's threshold, the choice is forgotten and the threshold decides again.
    effect(() => {
      const stale = staleChoices(this.view(), this.state.prefs(), this.state.sizes(), this.layout());

      for (const path of stale) {
        this.state.forgetChoice(path);
      }
    });
  }

  /** The controls belonging in that panel's own nav. Nothing below `lg`. */
  togglesFor(path: string): readonly PanelToggle[] {
    return this.isNarrow() ? [] : togglesFor(this.view(), path);
  }

  /** The pages of the exclusive container at that path. Whatever the window: switching pages is what a narrow window needs. */
  pagesOf(path: string): readonly PanelPage[] {
    return pagesOf(this.view(), path);
  }

  /** The page at that path and its container, or nothing for a panel that is not a page. */
  pageOf(path: string): { container: string; page: PanelPage } | undefined {
    return pageOf(this.view(), path);
  }

  /** Whether that panel is currently closed. */
  isCollapsed(path: string): boolean {
    return findView(this.view(), path)?.isCollapsed === true;
  }

  /** Closes it if open, opens it if closed. */
  toggle(path: string): void {
    const panel = this.closable(path);

    if (panel) {
      this.collapse(panel, !panel.isCollapsed);
    }
  }

  open(path: string): void {
    this.setCollapsed(path, false);
  }

  close(path: string): void {
    this.setCollapsed(path, true);
  }

  /** Forgets what the user changed — one panel, or the whole layout when given nothing. */
  reset(path?: string): void {
    if (path === undefined) {
      this.state.resetAll();
    } else {
      this.state.reset(path);
    }
  }

  private setCollapsed(path: string, isCollapsed: boolean): void {
    const panel = this.closable(path);

    if (panel) {
      this.collapse(panel, isCollapsed);
    }
  }

  private collapse(panel: PanelView, isCollapsed: boolean): void {
    if (panel.isPage) {
      this.setActive(panel, isCollapsed);
      return;
    }

    this.state.setCollapsed(panel.path, isCollapsed, this.roomAround(panel.path));
  }

  /**
   * A command on a page is a choice of its container's open page: opening it names it,
   * closing it names none — or the first page, because a paged container never shows nothing.
   * The choice is this visit's: nothing is stored, and the first page opens next time.
   */
  private setActive(page: PanelView, isCollapsed: boolean): void {
    const found = pageOf(this.view(), page.path);

    if (!found) {
      return;
    }

    if (!isCollapsed) {
      this.state.setActive(found.container, page.node.key);
      return;
    }

    const container = findView(this.view(), found.container);
    const first = container?.isPaged ? container.children.find(child => child.isPage) : undefined;

    this.state.setActive(found.container, first?.node.key ?? null);
  }

  /** What the container around a panel measured last — the number its threshold is held against. */
  private roomAround(path: string): number | undefined {
    const parent = path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
    return this.state.sizes()[parent];
  }

  /**
   * The panel a command is about, if it is one the user is allowed to close. Refusing here
   * is what keeps the stored arrangement honest: a preference nothing will ever read is
   * worse than no preference at all.
   */
  private closable(path: string): PanelView | null {
    const panel = findView(this.view(), path);
    const layout = this.layout().key;

    if (!panel) {
      console.warn(`[ui-panels] No panel at "${path}" in layout "${layout}".`);
      return null;
    }

    if (!panel.canCollapse) {
      console.warn(
        `[ui-panels] Panel "${path}" in layout "${layout}" cannot be closed. ` +
          'It fills its container; say .Collapsible() on it if it should be closable.',
      );
      return null;
    }

    if (isForcedClosed(panel, this.isNarrow())) {
      console.warn(`[ui-panels] Panel "${path}" in layout "${layout}" stays closed: ` + `the window is narrower than ${NARROW_LAYOUT}px.`);
      return null;
    }

    return panel;
  }
}

/**
 * A signal that says whether the window is narrower than `lg`, from one media query — or
 * `false` for good when whatever mounted the layout waived the rule, or where there is no
 * window to ask.
 */
export function narrowWindow(destroyRef: DestroyRef, rule: boolean | null = null): Signal<boolean> {
  if (rule === false || typeof matchMedia === 'undefined') {
    return signal(false).asReadonly();
  }

  const query = matchMedia(`(max-width: ${NARROW_LAYOUT - 1}px)`);
  const state = signal(query.matches);
  const follow = (event: MediaQueryListEvent): void => state.set(event.matches);

  query.addEventListener('change', follow);
  destroyRef.onDestroy(() => query.removeEventListener('change', follow));

  return state;
}
