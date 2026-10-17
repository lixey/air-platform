import { NgComponentOutlet } from '@angular/common';
import {
  afterNextRender,
  afterRenderEffect,
  ChangeDetectorRef,
  Component,
  computed,
  DestroyRef,
  type ElementRef,
  inject,
  Injector,
  signal,
  type Type,
  viewChildren,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DEFAULT_GAP } from '../panels/panels-view';
import { PanelsComponent } from '../panels/panels.component';
import { WORK_AREA } from '../theme/css-classes';
import { type OverlayBox, type OverlayRect, type OverlaySize, type OverlayUnits, resolveOverlay } from './overlay-box';
import { focusInto, hasMovedOn, restoreFocus, trapTab } from './overlay-focus';
import type { OverlayRef } from './overlay-ref';
import { OverlayScreenComponent } from './overlay-screen.component';
import { OverlayService } from './overlay.service';

/** How the card sits in its layer, a column: `justify-*` places it on y, `items-*` on x. */
const JUSTIFY = { start: 'justify-start', center: 'justify-center', end: 'justify-end' };
const ITEMS = { start: 'items-start', center: 'items-center', end: 'items-end' };

/**
 * Every overlay's box: the panels inside lay themselves out in it. The template adds `ui-work`, the work area's slate
 * roles, unless the overlay was opened from the frame (`OverlayRef.scope`).
 *
 * The card is the one item of a column layer the size of its frame, `flex: 0 1 auto`: as tall as its content, shrinking to
 * the frame — and, the layer's height being definite, a height its panels lay out against as they do in a routed screen,
 * so that what is inside scrolls where it would scroll on a page. `relative` carries the offset that puts a menu's corner
 * on its trigger's.
 */
const BOX = 'relative flex flex-col flex-initial min-w-0 min-h-0 overflow-hidden pointer-events-auto';

/**
 * Every overlay is one card: the surface ground, the theme's frame as its border, its shade
 * as its shadow and the outer radius, with the panels flush inside it — the ref's injector
 * hands the layout the flush density, so the panels draw no card, no gap and no padding of
 * their own and the card's edge is theirs. The frame and the shade are the brand's, which is
 * what sets a window off the page. On a narrow window the card keeps only its ground and is
 * edge to edge.
 */
const CARD = 'bg-surface border border-frame shadow-lg shadow-shade rounded-xl @xl:rounded-2xl';

/** The margin every overlay keeps from its frame's edges and the menu's corner offset, as spacing steps: `2` and `3`. */
const OVERLAY_MARGIN_REM = 0.5;
const MENU_OFFSET_REM = 0.75;

/**
 * Draws every open overlay.
 *
 * The frame renders it beside its own panels — never inside one, because a panel is a
 * container and a fixed box inside a container is positioned against it, not the window.
 * Each overlay is a scrim, a layer over the frame it stands in — the work area a margin in
 * for a drawer, the window a margin in for the rest — and one card that holds a panel layout
 * flush inside it: its own, or the one a screen answers when the model names one by address.
 * The layer is a column the frame's size and the card its one item, so the card's height is
 * definite whatever its content and the panels inside lay out against it. A drawer stands in
 * the frame's corner; a dialog is centred; a menu hangs from its trigger, placed by the size
 * its card is measured at and placed again as that changes.
 *
 * The panels are mounted through the ref's injector, which is how anything inside can
 * inject `OverlayRef` and close the overlay it is in.
 *
 * A card reads the work area's slate scale, as the routed screen does, unless its opener put
 * it in the frame's scale: then it reads the brand, as the navigation does.
 *
 * Each card says what it is: a dialog, modal for a `dialog` and a `drawer` and not for a `menu`
 * or a `popover`, or the alert dialog a confirmation names it; its name is the first the ref
 * was given. The cards under the topmost modal one are inert, as the frame's panels are while
 * a modal overlay is open. `Escape` closes the topmost overlay only when nothing inside
 * handled it.
 */
@Component({
  selector: 'ui-overlay-host, [ui-overlay-host], [uiOverlayHost]',
  imports: [NgComponentOutlet],
  template: `
    @for (entry of entries(); track entry.ref) {
      <div
        class="fixed inset-0 z-50"
        [class.bg-scrim]="entry.box.presentation !== 'menu'"
        (pointerdown)="overlay.closeOutside(entry.ref)"
      ></div>

      <div
        class="fixed z-50 flex flex-col pointer-events-none"
        [class]="alignment(entry.box)"
        [style]="layerStyle(entry.box)"
        [attr.inert]="entry.isCovered ? '' : null"
      >
        <div
          #card
          [class]="windowClasses(entry.box)"
          [class.${WORK_AREA}]="entry.ref.scope === 'work'"
          [style]="boxStyle(entry.box)"
          [style.--ui-panel-gap.px]="gap"
          [attr.id]="entry.ref.id"
          [attr.role]="entry.ref.naming().role ?? 'dialog'"
          [attr.aria-modal]="entry.ref.isModal ? 'true' : null"
          [attr.aria-label]="entry.ref.naming().label ?? null"
          [attr.aria-labelledby]="entry.ref.naming().labelledBy ?? null"
          [attr.aria-describedby]="entry.ref.naming().describedBy ?? null"
          tabindex="-1"
          (keydown.tab)="trapTab($event, card)"
          (keydown.shift.tab)="trapTab($event, card)"
        >
          <ng-container
            [ngComponentOutlet]="entry.component"
            [ngComponentOutletInputs]="entry.inputs"
            [ngComponentOutletInjector]="entry.ref.injector"
          />
        </div>
      </div>
    }
  `,
  host: {
    class: 'contents',
    '(document:keydown.escape)': 'closeTop($event)',
    '(window:resize)': 'measureViewport()',
  },
})
export class OverlayHostComponent {
  protected readonly overlay = inject(OverlayService);
  protected readonly gap = DEFAULT_GAP;
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  private readonly viewport = signal<OverlayRect>(readViewport());
  private readonly units = signal<OverlayUnits>(readUnits(this.gap));

  /** The border-box size each open card was last measured at: what places it until the next measurement. */
  private readonly sizes = signal<ReadonlyMap<OverlayRef, OverlaySize>>(new Map());
  private readonly observers = new Map<OverlayRef, ResizeObserver>();

  private readonly cards = viewChildren<ElementRef<HTMLElement>>('card');

  /** What had focus when each overlay opened, to hand it back when that overlay closes. */
  private readonly openers = new Map<OverlayRef, Element | null>();

  /**
   * A newly opened overlay takes focus once its card is rendered, remembers where focus
   * came from, and has its card measured from then on. The card is gone by the time the ref
   * reports closed, so the return of focus and the end of measuring are wired here, at the
   * moment of opening, for as long as the host lives.
   */
  private readonly watchNewOverlays = afterRenderEffect(() => {
    const cards = this.cards();

    for (const ref of this.overlay.stack()) {
      if (this.openers.has(ref)) {
        continue;
      }

      this.openers.set(ref, document.activeElement);
      ref.closed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
        const opener = this.openers.get(ref) ?? null;
        this.openers.delete(ref);
        this.unobserve(ref);
        // The host's destroy hooks run in the view it stands in, beside an opener's that closes its menu: a close from
        // there reaches this subscription while the view is already destroyed, which takes no render hook
        if (this.destroyRef.destroyed) {
          return;
        }
        // Once the layer it stood under is no longer inert
        afterNextRender(
          () => {
            // A menu that left focus with its opener gives it back unless the user moved it on meanwhile, as `Tab` does
            if (!ref.keepsOpenerFocus || !hasMovedOn(opener)) {
              restoreFocus(opener);
            }
          },
          { injector: this.injector },
        );
      });

      const card = cards.find(c => c.nativeElement.id === ref.id)?.nativeElement;
      if (card) {
        if (!ref.keepsOpenerFocus) {
          focusInto(card);
        }
        this.observe(ref, card);
      }
    }
  });

  protected readonly trapTab = trapTab;

  protected readonly entries = computed(() => {
    const viewport = this.viewport();
    const work = this.overlay.bounds() ?? viewport;
    const units = this.units();
    const sizes = this.sizes();

    // Every card under the topmost modal one is inert, as the frame's panels are
    const stack = this.overlay.stack();
    const topModal = stack.reduce((top, ref, index) => (ref.isModal ? index : top), -1);

    // An overlay carries a layout, or names a screen the host loads by address: same card, other content.
    return stack.map((ref, index) => {
      const model = ref.model;
      const content: { component: Type<unknown>; inputs: Record<string, unknown> } = model.screen
        ? { component: OverlayScreenComponent, inputs: { service: model.screen.service, params: model.screen.params } }
        : { component: PanelsComponent, inputs: { layout: model.layout } };

      const box = resolveOverlay(model, {
        work,
        anchor: rectOf(ref.anchor),
        viewport,
        units,
        size: sizes.get(ref) ?? null,
        keepsOwnWidth: ref.keepsOwnWidth,
        hangsDownOnly: ref.hangsDownOnly,
        asked: ref.asked(),
      });
      return { ref, box, isCovered: index < topModal, ...content };
    });
  });

  constructor() {
    // The overlays live in a root service and may outlive the host: it stops measuring their cards and returns no focus.
    this.destroyRef.onDestroy(() => {
      for (const observer of this.observers.values()) {
        observer.disconnect();
      }
      this.observers.clear();
      this.openers.clear();
    });
  }

  /** `Escape` closes the topmost overlay, unless something inside it handled the key first. */
  protected closeTop(event: Event): void {
    if (!event.defaultPrevented) {
      this.overlay.closeTop();
    }
  }

  protected alignment(box: OverlayBox): string {
    return `${JUSTIFY[box.align.y]} ${ITEMS[box.align.x]}`;
  }

  protected windowClasses(box: OverlayBox): string {
    return box.isNarrow ? `${BOX} bg-surface` : `${BOX} ${CARD}`;
  }

  protected layerStyle(box: OverlayBox): Record<string, string> {
    return {
      left: `${box.layer.left}px`,
      top: `${box.layer.top}px`,
      width: `${box.layer.width}px`,
      height: `${box.layer.height}px`,
    };
  }

  protected boxStyle(box: OverlayBox): Record<string, string> {
    return {
      left: `${box.offset.x}px`,
      top: `${box.offset.y}px`,
      width: box.width === null ? 'max-content' : `${box.width}px`,
      height: box.height === null ? 'auto' : `${box.height}px`,
      'min-width': `${box.minWidth}px`,
      'max-width': `${box.maxWidth}px`,
      'max-height': `${box.maxHeight}px`,
    };
  }

  protected measureViewport(): void {
    this.viewport.set(readViewport());
    this.units.set(readUnits(this.gap));
  }

  /**
   * Measures the card from its first layout on and places it again by every new size before
   * that size is drawn. `ResizeObserver` reports after layout and before paint — the first
   * time before the card's first paint, which is why no size is guessed — and a change
   * detection run inside its callback lays the card out again in the same rendering step,
   * where the scheduler's later run would have shown one frame of the old placement.
   */
  private observe(ref: OverlayRef, card: HTMLElement): void {
    if (typeof ResizeObserver === 'undefined') {
      return;
    }

    const observer = new ResizeObserver(entries => {
      if (this.observers.get(ref) !== observer) {
        return;
      }

      const size = borderBoxOf(entries[entries.length - 1], card);
      this.sizes.update(sizes => new Map(sizes).set(ref, size));
      this.cdr.detectChanges();
    });

    observer.observe(card, { box: 'border-box' });
    this.observers.set(ref, observer);
  }

  private unobserve(ref: OverlayRef): void {
    this.observers.get(ref)?.disconnect();
    this.observers.delete(ref);
    this.sizes.update(sizes => {
      if (!sizes.has(ref)) {
        return sizes;
      }

      const next = new Map(sizes);
      next.delete(ref);
      return next;
    });
  }
}

/** The spacing steps in pixels, from the root font size, which this product changes with the window. */
function readUnits(gap: number): OverlayUnits {
  const rem = typeof document === 'undefined' ? 16 : parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;

  return { gap, margin: OVERLAY_MARGIN_REM * rem, offset: MENU_OFFSET_REM * rem };
}

function readViewport(): OverlayRect {
  return typeof window === 'undefined'
    ? { left: 0, top: 0, width: 0, height: 0 }
    : { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
}

function rectOf(anchor: HTMLElement | null): OverlayRect | null {
  if (!anchor) {
    return null;
  }

  const { left, top, width, height } = anchor.getBoundingClientRect();
  return { left, top, width, height };
}

/** A card's border box as the observer reports it, or as the card measures when the report carries none. */
function borderBoxOf(entry: ResizeObserverEntry | undefined, card: HTMLElement): OverlaySize {
  const box = entry?.borderBoxSize?.[0];
  if (box) {
    return { width: box.inlineSize, height: box.blockSize };
  }

  const { width, height } = card.getBoundingClientRect();
  return { width, height };
}
