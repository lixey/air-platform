import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  linkedSignal,
  viewChildren,
} from '@angular/core';
import { DynamicComponentRegistry } from '../core/dynamic-component/dynamic-component-registry';
import { DynamicOutletDirective } from '../core/dynamic-component/dynamic-outlet.directive';
import { TextsService } from '../core/texts.service';
import { FOCUS_RING } from '../theme/css-classes';
import { PanelDividerComponent } from './panel-divider.component';
import { type DragPlan, moveDrag, planDrag, writtenSides } from './panel-drag';
import { parseSize } from './panel-size';
import { PanelsState } from './panels-state';
import { PANEL_DENSITY, PANEL_NODE, type PanelDensity, type PanelNode, PANELS } from './panels-tokens';
import { maxSizeOf, minSizeOf, type PanelView } from './panels-view';

/**
 * The classes one panel's card carries.
 *
 * Three decisions, and only the middle one is about looks. The structure is what makes the
 * panel a panel. The card is dropped when the screen says the page should show through, and
 * dropped again — through `has-[ui-panels]` — when a layout inside already draws cards of
 * its own. The container root is kept in every case but one, because content lays itself out
 * by the width the panel has whether or not anything is drawn around it; a panel sized to its
 * content is the exception, since inline-size containment would make its width independent of
 * what is in it.
 */
export function panelCardClasses(isTransparent: boolean, size: string, density: PanelDensity = 'cards'): string {
  const structure = 'flex flex-col flex-1 w-full h-full min-w-0 min-h-0 overflow-hidden';

  // Flush panels stand edge to edge inside whatever mounted them, which draws the one card.
  const card =
    isTransparent || density === 'flush'
      ? ''
      : 'bg-surface border border-edge rounded-xl ' +
        'has-[ui-panels]:border-0 has-[ui-panels]:bg-transparent has-[ui-panels]:rounded-none';

  const container = size === 'auto' ? '' : '@container/panel';

  return [structure, card, container].filter(Boolean).join(' ');
}

/**
 * Renders one node, and — when that node has children — the nodes inside it. The same
 * component all the way down, so a layout three levels deep needs no extra machinery.
 *
 * A container also measures itself and reports its size, which is what lets the view know
 * whether its children still fit. An exclusive container keeps every page that has been
 * opened once mounted and hidden, so a page comes back exactly as it was.
 *
 * The node also provides itself as `PANEL_NODE`, so a component inside can learn which panel
 * it stands in and walk up to the containers around it, across nested layouts.
 */
@Component({
  selector: 'ui-panel-node',
  imports: [DynamicOutletDirective, PanelDividerComponent],
  providers: [{ provide: PANEL_NODE, useExisting: PanelNodeComponent }],
  template: `
    @let v = view();

    @if (v.isContainer) {
      <!--
        An exclusive container keeps every page opened once in the DOM, hidden rather than
        destroyed: a table keeps its page and selection, a form its draft and focus. A divider
        stands only between two panels both shown, so the one open page is parted from the
        pinned panels and from nothing else; an ordinary container renders its open children only.
      -->
      @for (child of kept(); track child.path) {
        <ui-panel-node [view]="child" [data]="data()" [hidden]="child.isCollapsed" />

        @let after = dividerAfter(child);
        @if (after >= 0) {
          <ui-panel-divider
            [direction]="v.direction"
            [interactive]="isDraggable(after)"
            [flush]="isFlush"
            [label]="texts.of('ResizePanel', reportsOn(after).path)"
            [controls]="elementIdOf(reportsOn(after).path)"
            [resizes]="reportsOn(after).isAfter ? 'after' : 'before'"
            (dragStart)="onDragStart(after)"
            (dragMove)="onDragMove($event)"
            (dragEnd)="onDragEnd()"
            (resetSize)="onResetSize(after)"
          />
        }
      }
    } @else {
      <!--
        A panel is a card and nothing else. It draws no nav of its own: a title strip on
        every panel eats the space the work needs, and whatever belongs above the content —
        a nav, a search box, the controls for the panels around it — belongs to the
        component that fills the panel, which knows what it is doing.

        It is also a container query root, so what is inside can lay itself out by the width
        it actually has rather than the width of the window. See cardClasses.

        A panel holding a layout of its own drops even the card, so a nested screen reads
        as part of the one tree.
      -->
      <!--
          A landmark only when the screen gave the panel a name. A region announced as "main"
          because that is its key tells a screen reader nothing, and an unnamed region is not
          a landmark anyway — better to leave it as the plain box it is. The page of a paged
          container is the panel of the tab that opens it, named by its label when it has one:
          a key is never read out.

          A kind nobody registered leaves the card empty and says so in the console: the
          message is for the developer who forgot a registration, as the nav's is.
        -->
      <section [class]="cardClasses()" [attr.role]="role()" [attr.aria-label]="v.node.label">
        <div class="flex flex-col flex-1 w-full h-full min-w-0 min-h-0 overflow-auto has-[ui-panels]:overflow-hidden">
          @if (isRegistered()) {
            <ng-container [uiDynamicOutlet]="v.node.model?.kind" [uiDynamicOutletInputs]="panelInputs()" />
          }
        </div>
      </section>
    }
  `,
  host: {
    '[style.flex]': 'view().flex',
    '[style.min-width.px]': 'bounds().minWidth',
    '[style.max-width.px]': 'bounds().maxWidth',
    '[style.min-height.px]': 'bounds().minHeight',
    '[style.max-height.px]': 'bounds().maxHeight',
    '[class]': 'classes()',
    '[attr.data-panel]': 'view().path || null',
    // A layout's root stands beside no divider: only its panels need an id
    '[attr.id]': 'view().path ? elementIdOf(view().path) : null',
    // Only where transparency means something: a flush panel has no card to drop, and a
    // nav inside one should keep its inset as it would inside a card.
    '[attr.data-transparent]': 'view().node.isTransparent && !isFlush ? "" : null',
  },
})
export class PanelNodeComponent implements PanelNode {
  private readonly host = inject(ElementRef<HTMLElement>);
  protected readonly texts = inject(TextsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly state = inject(PanelsState);
  private readonly registry = inject(DynamicComponentRegistry);

  /** The pages of an exclusive container that have been opened once, by path: every page shown joins them. Not persisted. */
  private readonly opened = linkedSignal<{ readonly key: string; readonly shown: readonly string[] }, ReadonlySet<string>>({
    source: () => ({
      key: this.state.key(),
      shown: this.view()
        .children.filter(child => child.isPage && !child.isCollapsed)
        .map(child => child.path),
    }),
    computation: ({ key, shown }, previous) => {
      if (!previous || previous.source.key !== key) {
        return new Set(shown);
      }
      const opened = previous.value;
      return shown.some(path => !opened.has(path)) ? new Set([...opened, ...shown]) : opened;
    },
  });

  private readonly childElements = viewChildren(PanelNodeComponent, {
    read: ElementRef,
  });

  /**
   * Everything a drag needs, read once when it begins. Re-deriving it from indices on every
   * move would be reading a list that may have changed underneath — a panel closing itself
   * mid-drag would silently move the drag onto a different panel.
   */
  private drag: DragPlan | null = null;

  /** How the layout around this node is drawn. Given by whatever mounted the layout. */
  protected readonly isFlush = (inject(PANEL_DENSITY, { optional: true }) ?? 'cards') === 'flush';

  protected readonly focusRing = FOCUS_RING;

  /** This node, as it should look right now. */
  readonly view = input.required<PanelView>();

  /** The screen's data. Handed to any panel component that declares a `data` input. */
  readonly data = input<unknown>();

  /** The node's path: `side/log`, or `` for a layout's root. */
  readonly path = computed(() => this.view().path);

  /** The layout host this node belongs to. */
  readonly panels = inject(PANELS);

  /** The node above — the outer layout's leaf, for a nested layout's root. */
  readonly parent = inject(PANEL_NODE, { optional: true, skipSelf: true });

  /** Children that are open. In an ordinary container closed ones are not rendered at all. */
  protected readonly visible = computed(() => this.view().children.filter(child => !child.isCollapsed));

  /**
   * The children that are rendered: the ones open now and, in an exclusive container, the
   * pages opened once, in declared order. A page never opened is not mounted.
   */
  protected readonly kept = computed(() => {
    const opened = this.opened();
    return this.view().children.filter(child => !child.isCollapsed || opened.has(child.path));
  });

  protected readonly classes = computed(() => {
    const dir = this.view().direction === 'column' ? 'flex-col' : 'flex-row';
    const isAuto = parseSize(this.view().size).unit === 'auto';

    if (isAuto) {
      const cross = this.view().axis === 'column' ? 'w-full' : 'h-full';
      return `flex ${cross} min-w-0 min-h-0 ${dir}`;
    }

    return `flex w-full h-full min-w-0 min-h-0 ${dir}`;
  });

  /**
   * The panel's bounds along its axis, held by the browser and not only by the drag — so a
   * panel sized to its content with a maximum is as large as its content up to that, and a
   * stored size outside the bounds is held to them. The root has no axis of its own.
   */
  protected readonly bounds = computed(() => {
    const view = this.view();
    const none = { minWidth: null, maxWidth: null, minHeight: null, maxHeight: null };

    if (!view.path) {
      return none;
    }

    const min = minSizeOf(view);
    const max = maxSizeOf(view);
    const isRow = view.axis === 'row';

    return {
      minWidth: isRow && min > 0 ? min : null,
      maxWidth: isRow && Number.isFinite(max) ? max : null,
      minHeight: !isRow && min > 0 ? min : null,
      maxHeight: !isRow && Number.isFinite(max) ? max : null,
    };
  });

  /**
   * The card, and the container its content measures itself against.
   *
   * `@container/panel` lets a component inside write `@lg/panel:flex` and mean *this panel
   * is wide enough*, not *the window is*. A nav in a squeezed panel then drops its
   * labels while the window stays as wide as it was — which viewport breakpoints can never
   * get right, because they do not know a panel is there.
   *
   * Not on a panel sized to its content: inline-size containment makes an element's width
   * independent of what is inside it, which is the one thing such a panel needs.
   */
  protected readonly cardClasses = computed(() =>
    panelCardClasses(this.view().node.isTransparent === true, this.view().size, this.isFlush ? 'flush' : 'cards'),
  );

  protected readonly isRegistered = computed(() => Boolean(this.registry.resolve(this.view().node.model?.kind)));

  /** A page of a container that is paged: one page is always open there, and its tab is the one naming it. */
  protected readonly isTabPanel = computed(() => this.view().isPage && this.parent?.view().isPaged === true);

  /** A tab's panel, a named region, or the plain box. */
  protected readonly role = computed(() => {
    if (this.isTabPanel()) {
      return 'tabpanel';
    }
    return this.view().node.label ? 'region' : null;
  });

  private readonly reportUnknown = effect(() => {
    const { isContainer, node, path } = this.view();

    if (!isContainer && !this.isRegistered()) {
      console.warn(`[ui-panels] No component registered for the kind "${node.model?.kind}"; panel "${path}" is empty.`);
    }
  });

  protected readonly panelInputs = computed<Record<string, unknown>>(() => ({
    model: this.view().node.model,
    data: this.data(),
    path: this.view().path,
    label: this.view().name,
  }));

  constructor() {
    afterNextRender(() => this.measureSelf());
  }

  /**
   * The id of the element of the panel at that path: the path made id-safe, after the layout's instance id, so a
   * layout mounted twice or nested inside another names each of its panels apart.
   */
  protected elementIdOf(path: string): string {
    return `${this.state.id}-${idSafe(path)}`;
  }

  /** A divider is draggable when either panel beside it was marked resizable and neither is rigid: a rigid panel is never written. */
  protected isDraggable(index: number): boolean {
    const open = this.visible();
    const before = open[index]?.node;
    const after = open[index + 1]?.node;
    return !!(before?.isResizable || after?.isResizable) && !before?.isRigid && !after?.isRigid;
  }

  protected onDragStart(index: number): void {
    const open = this.visible();

    this.drag = planDrag(open[index], open[index + 1], this.measure(open[index]), this.measure(open[index + 1]));
  }

  /**
   * The divider hands pixels between its two neighbours and touches nothing else: a
   * neighbour that fills absorbs, every other one is written. So the divider that is
   * dragged is the one that moves — never the one on the far side of a filler.
   */
  protected onDragMove(delta: number): void {
    if (!this.drag) {
      return;
    }

    for (const { path, size } of moveDrag(this.drag, delta)) {
      this.state.setSize(path, size);
    }
  }

  protected onDragEnd(): void {
    this.drag = null;
  }

  protected onResetSize(index: number): void {
    const open = this.visible();
    const written = writtenSides(open[index], open[index + 1]);

    if (written.before) {
      this.state.reset(open[index].path);
    }

    if (written.after) {
      this.state.reset(open[index + 1].path);
    }
  }

  /** The neighbour a divider speaks for: the first of the ones it writes. */
  protected reportsOn(index: number): { path: string; isAfter: boolean } {
    const open = this.visible();
    const isAfter = !writtenSides(open[index], open[index + 1]).before;

    return { path: open[isAfter ? index + 1 : index].path, isAfter };
  }

  /**
   * The index, among the visible children, of a divider after `child`: the child is shown
   * and another shown child follows it. `-1` when no divider belongs there.
   */
  protected dividerAfter(child: PanelView): number {
    if (child.isCollapsed) {
      return -1;
    }

    const index = this.visible().indexOf(child);
    return index >= 0 && index < this.visible().length - 1 ? index : -1;
  }

  /** Tells the view how much room this container has, and how wide it is, so it knows what still fits. */
  private measureSelf(): void {
    const { path, isContainer } = this.view();

    if (!isContainer || typeof ResizeObserver === 'undefined') {
      return;
    }

    const observer = new ResizeObserver(([entry]) => {
      const box = entry.contentRect;
      this.state.measure(path, this.isColumn() ? box.height : box.width, box.width);
    });

    observer.observe(this.host.nativeElement as HTMLElement);

    this.destroyRef.onDestroy(() => {
      observer.disconnect();
      this.state.forget(path);
    });
  }

  private isColumn(): boolean {
    return this.view().direction === 'column';
  }

  /** A rendered child's size along this container, found by its path: the rendered children include kept pages the open ones do not. */
  private measure(child: PanelView): number {
    const element = this.childElements()
      .map(ref => ref.nativeElement as HTMLElement)
      .find(rendered => rendered.getAttribute('data-panel') === child.path);

    if (!element) {
      return 0;
    }

    return this.isColumn() ? element.offsetHeight : element.offsetWidth;
  }
}

/**
 * A path as the tail of an id: letters, digits and hyphens kept, every other character, the path's `/` and `_` among
 * them, written as `_` and its code in hex and `_`, so two paths never give one id.
 */
function idSafe(path: string): string {
  return path.replace(/[^A-Za-z0-9-]/g, char => `_${char.charCodeAt(0).toString(16)}_`);
}
