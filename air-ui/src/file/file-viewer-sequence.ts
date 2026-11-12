import { computed, linkedSignal, signal, type Signal } from '@angular/core';
import type { GridRowKey } from '../grid/grid-row';
import { FOCUS_RING } from '../theme/css-classes';
import type { FileView } from './file-view';

/** A thumbnail of the strip: a small square button, the shown file's framed in the checked role. */
export const THUMB_CLASSES = `flex items-center justify-center size-14 rounded-md border-2 bg-surface-2 overflow-hidden cursor-pointer ${FOCUS_RING}`;

/** The keys that walk the files, with where each goes from the shown one among `count`. */
const MOVES: Readonly<Record<string, (index: number, count: number) => number>> = {
  ArrowLeft: index => index - 1,
  ArrowRight: index => index + 1,
  Home: () => 0,
  End: (_, count) => count - 1,
};

/** Whether a key is one of the sequence navigation keys (`ArrowLeft`, `ArrowRight`, `Home`, `End`). */
export function isViewerMoveKey(key: string): boolean {
  return key in MOVES;
}

/** Whether a key came from inside Info, whose fields keep their keys. */
export function isInInfo(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[data-file-info-panel]') !== null;
}

/** Whether the focus is on the strip of thumbnails. */
export function isInStrip(active: Element | null): boolean {
  return active !== null && active.closest('[data-file-strip]') !== null;
}

/**
 * Tracks the ordered sequence of files shown by `FileViewerOverlayComponent`: filters out files deleted in-place,
 * maintains the active key and fallback index across external refreshes, and computes keyboard navigation targets.
 */
export class FileViewerSequence {
  private readonly removed = signal<ReadonlySet<GridRowKey>>(new Set());
  private readonly shownKey: ReturnType<typeof linkedSignal<GridRowKey | null>>;
  private readonly position = signal(0);

  /** The files the viewer walks: the opener's that have a key, less those deleted here. */
  readonly files: Signal<readonly FileView[]>;

  /** The shown file's place: where its key stands, else the last place held, within the sequence. */
  readonly index: Signal<number>;

  /** The active file in the sequence, or null when empty. */
  readonly current: Signal<FileView | null>;

  constructor(sourceFiles: () => readonly FileView[], initialKey: () => GridRowKey | null) {
    this.shownKey = linkedSignal<GridRowKey | null>(initialKey);
    this.files = computed(() => {
      const removed = this.removed();
      return sourceFiles().filter(file => file.key !== null && !removed.has(file.key));
    });
    this.index = computed(() => {
      const files = this.files();
      const found = files.findIndex(file => file.key === this.shownKey());
      return found >= 0 ? found : Math.min(this.position(), files.length - 1);
    });
    this.current = computed<FileView | null>(() => this.files()[this.index()] ?? null);
  }

  /** Computes the clamped target index for a viewer move key, or null when no move applies. */
  targetIndexFor(key: string): number | null {
    const count = this.files().length;
    const to = MOVES[key]?.(this.index(), count);
    return to !== undefined && count > 0 ? Math.max(0, Math.min(to, count - 1)) : null;
  }

  /** Selects the file at `index`, returning `true` when the active file changed. */
  select(index: number): boolean {
    const file = this.files()[index];
    if (!file || index === this.index()) {
      return false;
    }
    this.shownKey.set(file.key);
    this.position.set(index);
    return true;
  }

  /** Marks `key` deleted at `at` and advances to the neighbour; returns `false` when no files remain. */
  recordDeleted(key: GridRowKey, at: number): boolean {
    this.removed.update(removed => new Set(removed).add(key));
    const rest = this.files();
    if (!rest.length) {
      return false;
    }
    const next = Math.min(at, rest.length - 1);
    this.shownKey.set(rest[next].key);
    this.position.set(next);
    return true;
  }
}
