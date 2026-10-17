import { afterNextRender, Component, computed, ElementRef, inject, input, output, signal } from '@angular/core';
import type { PanelDirection } from '../core/generated/contracts';
import { FOCUS_RING_OUTSIDE } from '../theme/css-classes';

/** Widens what the pointer can hit without widening what the layout reserves. */
const ROW_HIT = 'after:absolute after:content-[""] after:inset-y-0 after:-left-1 after:-right-1';
const COLUMN_HIT = 'after:absolute after:content-[""] after:inset-x-0 after:-top-1 after:-bottom-1';

/**
 * The nearest sibling on one side that is shown: a page its container keeps rendered while
 * it is closed stands between the divider and its neighbour, and is not that neighbour.
 */
function shownSibling(element: Element, side: 'previousElementSibling' | 'nextElementSibling'): HTMLElement | null {
  let sibling = element[side];

  while (sibling instanceof HTMLElement && sibling.hidden) {
    sibling = sibling[side];
  }

  return sibling instanceof HTMLElement ? sibling : null;
}

/**
 * The space between two panels — and, when the panels either side can be resized,
 * the handle you drag to resize them.
 *
 * Between cards it is invisible, always: what tells you it is there is the cursor, and what tells you a
 * drag is happening is the panels moving. Nothing lights up under the pointer, so crossing
 * a row of panels sets off no flicker at all. Keyboard focus is the one exception — a
 * control you can tab to has to say where it is.
 *
 * It reports how far it has been dragged from where the drag began; the container owns
 * the sizes and decides what that means. Works with a mouse, a finger, and the keyboard.
 */
@Component({
  selector: 'ui-panel-divider',
  template: '',
  host: {
    '[class]': 'classes()',
    '[attr.role]': 'interactive() ? "separator" : null',
    '[attr.tabindex]': 'interactive() ? 0 : null',
    '[attr.aria-orientation]': 'interactive() ? ariaOrientation() : null',
    '[attr.aria-label]': 'interactive() ? label() : null',
    '[attr.aria-controls]': 'interactive() ? controls() : null',
    '[attr.aria-valuenow]': 'interactive() ? valueNow() : null',
    '[attr.aria-valuemin]': 'interactive() ? 0 : null',
    '[attr.aria-valuemax]': 'interactive() ? valueMax() : null',
    '(pointerdown)': 'onPointerDown($event)',
    '(keydown)': 'onKeyDown($event)',
    '(dblclick)': 'onDoubleClick()',
    '(focus)': 'measureNeighbours()',
  },
})
export class PanelDividerComponent {
  private static readonly Step = 10;
  private static readonly LargeStep = 50;

  private readonly host = inject(ElementRef<HTMLElement>);

  /** Direction of the container this divider sits in. */
  readonly direction = input.required<PanelDirection>();

  /** Whether the panels either side of it can be resized. */
  readonly interactive = input(false);

  /** Drawn as the hairline between flush panels rather than as the gap between cards. */
  readonly flush = input(false);

  /** Name of the panel being resized, for screen readers. */
  readonly label = input.required<string>();

  /** The element id of the panel being resized, which the divider controls. */
  readonly controls = input.required<string>();

  /** Which of the two neighbours this divider actually resizes — the one it reports on. */
  readonly resizes = input<'before' | 'after'>('before');

  /** Fired once, as a drag begins. */
  readonly dragStart = output<void>();

  /** How far the divider has moved, in pixels, since that drag began. */
  readonly dragMove = output<number>();

  /** Fired once, as a drag ends. Also fires when a drag is cancelled. */
  readonly dragEnd = output<void>();

  /** The user asked for the original size back: double-click, or Home. */
  readonly resetSize = output<void>();

  /** Size of the panel this divider resizes, and how large it could get. Read when it matters. */
  protected readonly valueNow = signal(0);
  protected readonly valueMax = signal(0);

  protected readonly ariaOrientation = computed(() =>
    // A divider in a row is a vertical bar, and the other way round.
    this.direction() === 'row' ? 'vertical' : 'horizontal',
  );

  protected readonly classes = computed(() => {
    const base = this.flush()
      ? 'relative self-stretch flex-none basis-px bg-line'
      : 'relative self-stretch flex-none basis-(--ui-panel-gap)';

    if (!this.interactive()) {
      return base;
    }

    const isRow = this.direction() === 'row';

    return [
      base,
      `touch-none rounded-full ${FOCUS_RING_OUTSIDE}`,
      isRow ? `cursor-col-resize ${ROW_HIT}` : `cursor-row-resize ${COLUMN_HIT}`,
    ].join(' ');
  });

  constructor() {
    afterNextRender(() => this.measureNeighbours());
  }

  /**
   * Measures the two panels either side, on the next frame so the numbers describe what
   * is on screen. Called when the divider is focused or a resize ends — never on every
   * change detection pass, which would read layout constantly.
   */
  protected measureNeighbours(): void {
    requestAnimationFrame(() => {
      const element = this.host.nativeElement as HTMLElement;
      const isRow = this.direction() === 'row';
      const sizeOf = (sibling: HTMLElement | null): number => {
        if (!sibling) {
          return 0;
        }
        return isRow ? sibling.offsetWidth : sibling.offsetHeight;
      };

      const before = sizeOf(shownSibling(element, 'previousElementSibling'));
      const after = sizeOf(shownSibling(element, 'nextElementSibling'));

      this.valueNow.set(Math.round(this.resizes() === 'after' ? after : before));
      this.valueMax.set(Math.round(before + after));
    });
  }

  protected onPointerDown(event: PointerEvent): void {
    if (!this.interactive() || event.button !== 0) {
      return;
    }

    event.preventDefault();

    const isRow = this.direction() === 'row';
    const origin = isRow ? event.clientX : event.clientY;
    const element = this.host.nativeElement as HTMLElement;
    const body = document.body;

    // Capture keeps the events coming to us; the document listeners make sure the drag
    // still ends properly if capture is refused or lost.
    try {
      element.setPointerCapture?.(event.pointerId);
    } catch {
      // Ignored if pointer capture is unsupported or inactive
    }

    const onMove = (moveEvent: PointerEvent): void => {
      this.dragMove.emit((isRow ? moveEvent.clientX : moveEvent.clientY) - origin);
    };

    const onEnd = (): void => {
      document.removeEventListener('pointermove', onMove);
      document.removeEventListener('pointerup', onEnd);
      document.removeEventListener('pointercancel', onEnd);
      document.removeEventListener('keydown', onEscape, true);
      body.classList.remove('ui-panels-dragging', isRow ? 'is-row' : 'is-column');
      // The browser drops the capture itself on pointerup and pointercancel, and releasing a dropped one throws
      if (element.hasPointerCapture?.(event.pointerId)) {
        element.releasePointerCapture(event.pointerId);
      }
      this.dragEnd.emit();
      this.measureNeighbours();
    };

    // Capturing, so it runs before the overlay host's listener, and handled, so no overlay closes with the drag
    const onEscape = (keyEvent: KeyboardEvent): void => {
      if (keyEvent.key === 'Escape') {
        keyEvent.preventDefault();
        this.dragMove.emit(0);
        onEnd();
      }
    };

    document.addEventListener('pointermove', onMove);
    document.addEventListener('pointerup', onEnd);
    document.addEventListener('pointercancel', onEnd);
    document.addEventListener('keydown', onEscape, true);
    // Stops the drag from selecting the text it passes over, and keeps the cursor steady
    // even when the pointer runs ahead of the divider. Defined in panels.css.
    body.classList.add('ui-panels-dragging', isRow ? 'is-row' : 'is-column');

    this.dragStart.emit();
  }

  protected onKeyDown(event: KeyboardEvent): void {
    if (!this.interactive()) {
      return;
    }

    if (event.key === 'Home') {
      event.preventDefault();
      this.resetSize.emit();
      return;
    }

    const step = this.stepFor(event);
    if (step === null) {
      return;
    }

    event.preventDefault();
    this.dragStart.emit();
    this.dragMove.emit(step * (event.shiftKey ? PanelDividerComponent.LargeStep : PanelDividerComponent.Step));
    this.dragEnd.emit();
    this.measureNeighbours();
  }

  protected onDoubleClick(): void {
    if (this.interactive()) {
      this.resetSize.emit();
    }
  }

  private stepFor(event: KeyboardEvent): number | null {
    if (this.direction() === 'row') {
      if (event.key === 'ArrowLeft') return -1;
      if (event.key === 'ArrowRight') return 1;
      return null;
    }

    if (event.key === 'ArrowUp') return -1;
    if (event.key === 'ArrowDown') return 1;
    return null;
  }
}
