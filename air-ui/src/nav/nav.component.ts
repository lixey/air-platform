import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, DestroyRef, ElementRef, inject, Injector, model } from '@angular/core';
import { BtnTablistDirective } from '../btn/btn-tablist.directive';
import { BtnComponent } from '../btn/btn.component';
import type { NavDirection, NavElementModel, NavModel, NavOverflow, OverlayModelDto } from '../core/generated/contracts';
import { resolveModel } from '../core/resolve-model';
import { TextsService } from '../core/texts.service';
import { OverlayService } from '../overlay/overlay.service';
import { singlePanelLayout } from '../panels/single-panel-layout';
import { NavElementOutletComponent } from './nav-element-outlet.component';

interface NavView {
  direction: NavDirection;
  overflow: NavOverflow;
}

const NAV_DEFAULTS: NavView = { direction: 'row', overflow: 'scroll' };

/** One part of the nav: the classes that keep it a row or a column of its own. */
const PART = 'flex items-center gap-(--ui-panel-gap) empty:hidden';

/** The key of the More menu's layout, and the stem of its overlay's key. */
const NAV_MORE_OVERLAY = 'nav.more';

/** A stretch of a part: one element, or the consecutive `NavTab` elements of one group, drawn as one tablist. */
interface NavRun {
  readonly key: string;
  readonly group: string | null;
  readonly elements: readonly NavElementModel[];
}

/** The runs of a part in order: a tab joins the run before it when that run is of its group. */
function runsOf(elements: readonly NavElementModel[] | undefined): readonly NavRun[] {
  const runs: { key: string; group: string | null; elements: NavElementModel[] }[] = [];

  for (const element of elements ?? []) {
    const group = element.kind === 'NavTab' ? element.group : null;
    const last = runs.at(-1);

    if (group !== null && last?.group === group) {
      last.elements.push(element);
    } else {
      runs.push({ key: element.key, group, elements: [element] });
    }
  }

  return runs;
}

/**
 * The nav: three parts of element models that arrive from the server, each a `kind`, a
 * `key` and what the element reads, and act for themselves, and a fourth, `more`, whose
 * elements it draws in a menu behind a `⋮` button at the end of the nav, named by the text
 * `More` and drawn only while the part has an element. The menu hangs from that button, is
 * mounted through the nav's injector — so its elements reach what they would reach in the
 * nav — and closes with the nav.
 *
 * It follows the library's rule for server-described components — a `model` carrying the
 * whole `NavModel`, `direction` and `overflow` inputs that win over its options — and
 * adds a content slot per part, so a template can put hand-written controls beside the
 * declared ones: `<ui-btn start …>`, `<ui-btn …>`, `<ui-btn end …>`.
 *
 * It spaces its elements by the gap the panels engine puts between panels, read from that
 * gap rather than from a text-relative step, so it agrees with the panels at every root
 * font size. It pads itself by that gap inside a card, and by nothing inside a transparent
 * panel: there the engine's gap around the panel is already its inset, and padding again
 * would set it one gap further in than the cards beside it. The panel that decides is the
 * nearest one — an overlay opened from a transparent panel is still inside it in the DOM,
 * and its own panels are cards. It draws nothing of its own.
 *
 * The consecutive `NavTab` elements of one group in a part are one tablist: one Tab stop, the
 * arrows between them. The nav itself stays a row of Tab stops, not a `toolbar` widget: it
 * holds unlike elements, a search field among them.
 */
@Component({
  selector: 'ui-nav, [ui-nav], [uiNav]',
  imports: [BtnComponent, BtnTablistDirective, NavElementOutletComponent, NgTemplateOutlet],
  template: `
    <ng-template #part let-runs>
      @for (run of runs; track run.key) {
        @if (run.group === null) {
          <ui-nav-element-outlet [element]="run.elements[0]" />
        } @else {
          <div uiTablist [orientation]="isColumn() ? 'vertical' : 'horizontal'" [class]="tablistClasses()">
            @for (element of run.elements; track element.key) {
              <ui-nav-element-outlet [element]="element" />
            }
          </div>
        }
      }
    </ng-template>

    <div class="${PART} shrink-0">
      <ng-container [ngTemplateOutlet]="part" [ngTemplateOutletContext]="{ $implicit: runs().start }" />
      <ng-content select="[start]" />
    </div>

    <div [class]="middleClasses()">
      <ng-container [ngTemplateOutlet]="part" [ngTemplateOutletContext]="{ $implicit: runs().elements }" />
      <ng-content />
    </div>

    <div class="${PART} shrink-0" [class.ml-auto]="!isColumn()" [class.mt-auto]="isColumn()">
      <ng-container [ngTemplateOutlet]="part" [ngTemplateOutletContext]="{ $implicit: runs().end }" />
      <ng-content select="[end]" />
      @if (more().length > 0) {
        <ui-btn
          icon="ellipsis-vertical"
          variant="ghost"
          [title]="texts.of('More')"
          aria-haspopup="dialog"
          [attr.aria-expanded]="moreMenu.isOpen()"
          [attr.aria-controls]="moreMenu.id()"
          (click)="openMore($event)"
          (keydown.arrowdown)="openMore($event)"
        />
      }
    </div>
  `,
  host: {
    '[class]': 'hostClasses()',
  },
})
export class NavComponent {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  protected readonly texts = inject(TextsService);
  /** The More menu, what the `⋮` button says of it; it closes with the nav. */
  protected readonly moreMenu = this.overlay.menu();

  /** The whole nav, as a server describes it. The two inputs below win over its options. */
  readonly model = model<NavModel | null>();

  readonly direction = model<NavDirection | null>();
  readonly overflow = model<NavOverflow | null>();

  readonly view = computed<NavView>(() =>
    resolveModel(NAV_DEFAULTS, this.model()?.options, {
      direction: this.direction(),
      overflow: this.overflow(),
    }),
  );

  readonly isColumn = computed(() => this.view().direction === 'column');

  /** The runs of each part: single elements, and the tabs of one group together. */
  protected readonly runs = computed(() => {
    const model = this.model();
    return { start: runsOf(model?.start), elements: runsOf(model?.elements), end: runsOf(model?.end) };
  });

  /** The elements of the `more` part: the `⋮` button stands while there is one. */
  protected readonly more = computed(() => this.model()?.more ?? []);

  /** A tablist lays its tabs out as the part does, with the part's gap, as a box of its own that keeps its role. */
  protected readonly tablistClasses = computed(() =>
    this.isColumn() ? 'flex flex-col gap-(--ui-panel-gap)' : 'flex flex-none items-center gap-(--ui-panel-gap)',
  );

  /** Whether the nearest panel around this nav draws no card. Read once: panels do not change their mind. */
  protected readonly isOnPage = computed(
    () => (this.host.nativeElement as HTMLElement).closest('[data-panel]')?.hasAttribute('data-transparent') === true,
  );

  protected readonly hostClasses = computed(() => {
    const inset = this.isOnPage() ? '' : ' p-(--ui-panel-gap)';

    return this.isColumn()
      ? `flex flex-col h-full min-h-0 w-full gap-(--ui-panel-gap)${inset}`
      : `flex flex-row items-center w-full min-w-0 gap-(--ui-panel-gap)${inset}`;
  });

  protected readonly middleClasses = computed(() => {
    const { direction, overflow } = this.view();
    const axis = direction === 'column' ? 'flex-col min-h-0' : 'min-w-0';
    const fit = fitOf(direction, overflow);

    return `${PART} flex-1 ${axis} ${fit}`;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.moreMenu.close());
  }

  /** Opens the More menu from the `⋮` button, over the part's elements as they are now; an open menu stays as it is. */
  protected openMore(event: Event): void {
    const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    event.preventDefault();
    if (!anchor || this.moreMenu.isOpen()) {
      return;
    }

    const model: OverlayModelDto = {
      key: `${NAV_MORE_OVERLAY}.${this.model()?.key ?? ''}`,
      presentation: 'menu',
      layout: singlePanelLayout(NAV_MORE_OVERLAY, { kind: 'NavMenuBody', elements: this.more() }),
    };
    this.moreMenu.open(model, anchor, this.injector, this.texts.of('More'));
  }
}

/** How the middle part fits what does not fit: wrapped, or scrolled along its axis. */
function fitOf(direction: NavDirection, overflow: NavOverflow): string {
  if (overflow === 'wrap') {
    return 'flex-wrap';
  }
  return direction === 'column' ? 'overflow-y-auto' : 'overflow-x-auto no-scrollbar';
}
