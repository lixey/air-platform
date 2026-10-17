import { afterRenderEffect, Directive, ElementRef, inject, input, output } from '@angular/core';

/** Whether the host's right edge may be dragged. */
export type ResizableSide = 'right' | 'none';

/**
 * Lets the user drag the right edge of the host to change its width, with what a column needs and no
 * more: a handle inside the edge, pointer capture for the drag, a width held between the bounds, and
 * the width emitted on every move for the host to apply itself; nothing is written to the host's style
 * here, so a width the host refuses never shows. Two presses on the handle within the double-click time
 * and a few pixels emit `uiResizableFit` instead, and what a fit means is the host's: the press prevents
 * the pointer's default, so no `dblclick` event comes.
 */
@Directive({
  selector: '[uiResizable]',
  host: {
    '[class.relative]': 'uiResizable() !== "none"',
  },
})
export class ResizableDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** Whether the right edge may be dragged; the directive's own attribute names it. */
  readonly uiResizable = input.required<ResizableSide>();
  readonly uiResizableMin = input.required<number>();
  readonly uiResizableMax = input.required<number>();

  /** The width a drag reached, in pixels, within the bounds; one per move that changed it. */
  readonly uiResizableResized = output<number>();
  /** A double click on the handle. */
  readonly uiResizableFit = output();

  private handle: HTMLElement | null = null;
  private startX = 0;
  private startWidth = 0;
  private width = 0;

  /** When the handle was last pressed, to tell a double click: the pointer's default is prevented, so no `dblclick` comes. */
  private lastPressAt = 0;
  private lastPressX = 0;
  private hasMoved = false;

  constructor() {
    afterRenderEffect(() => {
      this.uiResizable();
      this.ensureHandle();
    });
    this.host.addEventListener('pointerdown', this.onPointerDown);
    this.host.addEventListener('pointermove', this.onPointerMove);
    this.host.addEventListener('pointerup', this.onPointerUp);
    this.host.addEventListener('pointercancel', this.onPointerUp);
  }

  private isHandle(target: EventTarget | null): boolean {
    return target instanceof HTMLElement && target.dataset['resizeHandle'] !== undefined;
  }

  private ensureHandle(): void {
    if (this.uiResizable() === 'none') {
      this.handle?.remove();
      this.handle = null;
      return;
    }
    if (this.handle) {
      return;
    }

    const handle = document.createElement('span');
    handle.dataset['resizeHandle'] = '';
    handle.setAttribute('aria-hidden', 'true');
    // Inside the edge: a host that clips its overflow would halve a handle that straddles it
    handle.className = 'absolute top-0 bottom-0 right-0 w-2 cursor-col-resize select-none touch-none z-10';
    this.host.appendChild(handle);
    this.handle = handle;
  }

  private readonly onPointerDown = (event: PointerEvent): void => {
    if (!this.isHandle(event.target) || event.button !== 0) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();

    // Two presses within the double-click time and a few pixels are a fit, not a drag
    const now = event.timeStamp;
    const isDoubleClick = now - this.lastPressAt < DOUBLE_CLICK_MS && Math.abs(event.clientX - this.lastPressX) < DOUBLE_CLICK_SLOP_PX;
    if (isDoubleClick) {
      this.lastPressAt = 0;
      this.uiResizableFit.emit();
      return;
    }
    this.lastPressAt = now;
    this.lastPressX = event.clientX;

    this.startX = event.clientX;
    this.startWidth = this.host.getBoundingClientRect().width;
    this.width = this.startWidth;
    this.hasMoved = false;
    this.host.setPointerCapture(event.pointerId);
    this.host.dataset['resizing'] = '';
  };

  private readonly onPointerMove = (event: PointerEvent): void => {
    if (this.host.dataset['resizing'] === undefined) {
      return;
    }

    const delta = event.clientX - this.startX;
    // A hand that trembles over a double click is not a drag
    if (!this.hasMoved && Math.abs(delta) < DRAG_THRESHOLD_PX) {
      return;
    }
    const width = Math.round(Math.max(this.uiResizableMin(), Math.min(this.uiResizableMax(), this.startWidth + delta)));
    if (width !== this.width) {
      this.width = width;
      this.hasMoved = true;
      this.uiResizableResized.emit(width);
    }
  };

  private readonly onPointerUp = (event: PointerEvent): void => {
    if (this.host.dataset['resizing'] === undefined) {
      return;
    }

    delete this.host.dataset['resizing'];
    if (this.host.hasPointerCapture(event.pointerId)) {
      this.host.releasePointerCapture(event.pointerId);
    }
  };
}

const DOUBLE_CLICK_MS = 400;
/** How far the second press of a double click may land from the first. */
const DOUBLE_CLICK_SLOP_PX = 4;
const DRAG_THRESHOLD_PX = 3;
