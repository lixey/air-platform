import { afterNextRender, computed, Directive, ElementRef, inject, Injector, input, output, signal } from '@angular/core';
import { TextsService } from '../core/texts.service';
import type { GridRowKey } from '../grid/grid-row';

/** A file the reorder carries: its key, and its name, which the live region announces. */
export interface FileReorderItem {
  readonly key: GridRowKey;
  readonly name: string;
}

/** A file the user moved: its key, the key of the file it now stands before, none when it stands last, and the order it left. */
export interface FileReorderMove {
  readonly key: GridRowKey;
  readonly beforeKey: GridRowKey | null;
  readonly keys: readonly GridRowKey[];
}

/** The file being carried: where it stood when taken and where it stands now, and what took it. */
interface Taken {
  readonly key: GridRowKey;
  readonly from: number;
  readonly at: number;
  readonly by: 'pointer' | 'keyboard';
}

/** A press on a handle that has not moved far enough yet to take its file. */
interface Press {
  readonly key: GridRowKey;
  readonly pointerId: number;
  readonly x: number;
  readonly y: number;
}

/** How far a press travels before it takes its file: a tap on the handle moves nothing. */
const DRAG_THRESHOLD_PX = 4;

/** The keys that carry a taken file, with the step each makes: earlier or later. */
const STEPS: Readonly<Record<string, -1 | 1>> = { ArrowLeft: -1, ArrowUp: -1, ArrowRight: 1, ArrowDown: 1 };

/**
 * Lets the user move a file of a list among its files by the file's handle, a descendant carrying
 * `data-reorder-handle="{key}"`, inside the element of the file, which carries `data-reorder-item="{key}"`.
 *
 * A mouse, a pen or a finger drags the handle: the press takes the file once it has travelled a few pixels, the pointer
 * captured by the host, and the file takes the place of each file the pointer passes over, the others making room, as
 * `order` says; lifting it drops it, and a cancelled pointer puts it back. Only the handle refuses the browser's
 * gestures (`touch-action: none`, the handle's own class), so the page still scrolls under a finger anywhere else.
 * From the keyboard, Space on the handle takes the file, Left and Up carry it earlier, Right and Down later, Space
 * drops it, and Escape or Tab puts it back; the focus stays on its handle as the file moves. Each step is announced
 * as "{name}, position {n} of {count}" in `announcement`, which the host draws in a polite live region. A drop that
 * changed the place emits `uiFileReorderMoved`; what a move means is the host's, which gives the directive the new
 * order as its files.
 */
@Directive({
  selector: '[uiFileReorder]',
  exportAs: 'uiFileReorder',
  host: {
    '(pointerdown)': 'press($event)',
    '(pointermove)': 'drag($event)',
    '(pointerup)': 'lift($event)',
    '(pointercancel)': 'cancelDrag($event)',
    '(lostpointercapture)': 'cancelDrag($event)',
    '(keydown)': 'step($event)',
  },
})
export class FileReorderDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly texts = inject(TextsService);
  private readonly injector = inject(Injector);

  /** The files in the order shown, each with a handle and an element of its own under the host. */
  readonly uiFileReorder = input.required<readonly FileReorderItem[]>();
  /** Whether no file may be taken now: a taken one is put back. */
  readonly uiFileReorderDisabled = input(false);

  readonly uiFileReorderMoved = output<FileReorderMove>();

  private readonly taken = signal<Taken | null>(null);
  private pressed: Press | null = null;

  /** The key of the file being carried; null while none is. */
  readonly movingKey = computed(() => (this.uiFileReorderDisabled() ? null : (this.taken()?.key ?? null)));

  /** The files in the order to draw: the carried one where it stands now, the others making room. */
  readonly order = computed<readonly FileReorderItem[]>(() => {
    const items = this.uiFileReorder();
    const taken = this.taken();
    const from = taken ? items.findIndex(item => item.key === taken.key) : -1;
    if (!taken || from < 0 || this.uiFileReorderDisabled()) {
      return items;
    }
    const rest = items.filter((_, index) => index !== from);
    return [...rest.slice(0, taken.at), items[from], ...rest.slice(taken.at)];
  });

  /** What the live region says: the carried file's place after each step, or that it was put back. */
  readonly announcement = signal('');

  /** A press on a handle: the file is taken once the pointer has travelled, and the host captures the pointer. */
  protected press(event: PointerEvent): void {
    const key = this.handleKeyOf(event.target);
    if (key === null || this.uiFileReorderDisabled() || this.taken() || (event.pointerType === 'mouse' && event.button !== 0)) {
      return;
    }
    // No text is selected and no picture is dragged by the browser
    event.preventDefault();
    this.pressed = { key, pointerId: event.pointerId, x: event.clientX, y: event.clientY };
    this.host.setPointerCapture?.(event.pointerId);
  }

  /** The pointer travels: past the threshold the file is taken, and it takes the place of the file under the pointer. */
  protected drag(event: PointerEvent): void {
    const pressed = this.pressed;
    if (!pressed || pressed.pointerId !== event.pointerId) {
      return;
    }
    let taken = this.taken();
    if (!taken) {
      if (Math.hypot(event.clientX - pressed.x, event.clientY - pressed.y) < DRAG_THRESHOLD_PX) {
        return;
      }
      taken = this.take(pressed.key, 'pointer');
      if (!taken) {
        this.pressed = null;
        return;
      }
    }
    const over = this.itemKeyAt(event.clientX, event.clientY);
    if (over === null || over === taken.key) {
      return;
    }
    const at = this.order().findIndex(item => item.key === over);
    if (at >= 0) {
      this.carry(at);
    }
  }

  /** The pointer is lifted: a carried file is dropped where it stands. */
  protected lift(event: PointerEvent): void {
    if (!this.pressed || this.pressed.pointerId !== event.pointerId) {
      return;
    }
    this.release(event.pointerId);
    if (this.taken()?.by === 'pointer') {
      this.drop();
    }
  }

  /** The pointer is cancelled or its capture lost: a carried file is put back. */
  protected cancelDrag(event: PointerEvent): void {
    if (!this.pressed || this.pressed.pointerId !== event.pointerId) {
      return;
    }
    this.release(event.pointerId);
    if (this.taken()?.by === 'pointer') {
      this.putBack();
    }
  }

  /** Space takes and drops the file of the focused handle, the arrows carry it, Escape and Tab put it back. */
  protected step(event: KeyboardEvent): void {
    const key = this.handleKeyOf(event.target);
    if (key === null || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    const taken = this.taken();
    if (event.key === ' ') {
      event.preventDefault();
      if (!taken) {
        this.take(key, 'keyboard');
      } else if (taken.key === key && taken.by === 'keyboard') {
        this.drop();
      }
      return;
    }
    if (!taken || taken.key !== key || taken.by !== 'keyboard') {
      return;
    }
    if (event.key in STEPS) {
      event.preventDefault();
      this.carry(taken.at + STEPS[event.key]);
      this.keepFocus(key);
    } else if (event.key === 'Escape') {
      // Escape puts the file back and closes nothing around it
      event.preventDefault();
      event.stopPropagation();
      this.putBack();
      this.keepFocus(key);
    } else if (event.key === 'Tab') {
      this.putBack();
    }
  }

  /** Takes the file of `key` where it stands and announces its place; none where the file is not among the files. */
  private take(key: GridRowKey, by: Taken['by']): Taken | null {
    const from = this.uiFileReorder().findIndex(item => item.key === key);
    if (from < 0 || this.uiFileReorderDisabled()) {
      return null;
    }
    const taken: Taken = { key, from, at: from, by };
    this.taken.set(taken);
    this.announce(taken);
    return taken;
  }

  /** Carries the taken file to `at`, within the files, and announces its new place. */
  private carry(at: number): void {
    const taken = this.taken();
    if (!taken) {
      return;
    }
    const to = Math.max(0, Math.min(at, this.uiFileReorder().length - 1));
    if (to === taken.at) {
      return;
    }
    const moved = { ...taken, at: to };
    this.taken.set(moved);
    this.announce(moved);
  }

  /** Drops the taken file where it stands: a new place emits the move with the file it now stands before. */
  private drop(): void {
    const taken = this.taken();
    if (!taken) {
      return;
    }
    const keys = this.order().map(item => item.key);
    this.announce(taken);
    this.taken.set(null);
    if (taken.at !== taken.from && !this.uiFileReorderDisabled()) {
      this.uiFileReorderMoved.emit({ key: taken.key, beforeKey: keys[taken.at + 1] ?? null, keys });
    }
  }

  /** Puts the taken file back where it was taken, and says so. */
  private putBack(): void {
    const taken = this.taken();
    if (!taken) {
      return;
    }
    this.taken.set(null);
    const name = this.nameOf(taken.key);
    this.announcement.set(this.texts.of('FileMoveCancelled', name));
  }

  private release(pointerId: number): void {
    this.pressed = null;
    if (this.host.hasPointerCapture?.(pointerId)) {
      this.host.releasePointerCapture(pointerId);
    }
  }

  private announce(taken: Taken): void {
    const count = this.uiFileReorder().length;
    this.announcement.set(this.texts.of('FilePosition', this.nameOf(taken.key), taken.at + 1, count));
  }

  private nameOf(key: GridRowKey): string {
    return this.uiFileReorder().find(item => item.key === key)?.name ?? '';
  }

  /** The handle of the file keeps the focus once the files are drawn in their new order. */
  private keepFocus(key: GridRowKey): void {
    afterNextRender(
      () => {
        const handle = this.host.querySelector<HTMLElement>(`[data-reorder-handle="${key}"]`);
        if (handle && handle.ownerDocument.activeElement !== handle) {
          handle.focus();
        }
      },
      { injector: this.injector },
    );
  }

  /** The key of the handle an event came from, within the host; null for any other target. */
  private handleKeyOf(target: EventTarget | null): GridRowKey | null {
    const handle = target instanceof Element ? target.closest<HTMLElement>('[data-reorder-handle]') : null;
    if (!handle || !this.host.contains(handle)) {
      return null;
    }
    return keyOfAttribute(handle.dataset['reorderHandle']);
  }

  /** The key of the file whose element holds the point; null where none does. */
  private itemKeyAt(x: number, y: number): GridRowKey | null {
    for (const element of this.host.querySelectorAll<HTMLElement>('[data-reorder-item]')) {
      const box = element.getBoundingClientRect();
      if (x >= box.left && x <= box.right && y >= box.top && y <= box.bottom) {
        return keyOfAttribute(element.dataset['reorderItem']);
      }
    }
    return null;
  }
}

function keyOfAttribute(value: string | undefined): GridRowKey | null {
  const key = Number(value);
  return value !== undefined && value !== '' && Number.isInteger(key) ? key : null;
}
