import { DestroyRef, inject, Service, signal } from '@angular/core';
import { type PanelPref, type PanelPrefs, PANELS_STORAGE } from './panels-storage';
import type { PanelActive } from './panels-view';

/**
 * Everything the view needs besides the layout itself: what the user arranged, how much
 * room each container has, and which page each exclusive container shows.
 *
 * They stay separate fields because they are separate things — one is the user's, persisted
 * and theirs to reset; the second is the browser's, measured and thrown away; the third is
 * this visit's, gone with the layout. But they are read together on every projection, so
 * they live in one place.
 *
 * The layout that came from the server is never modified. The rendered view is computed
 * from all of them, so a redeploy can change the layout without wiping what people arranged.
 */
@Service({ autoProvided: false })
export class PanelsState {
  private static readonly SaveDelayMs = 200;
  private static next = 0;

  private readonly storage = inject(PANELS_STORAGE);

  /** This layout's instance id, unique on the page: what the element ids of its panels begin with. */
  readonly id = `ui-panels-${++PanelsState.next}`;

  private readonly _key = signal('');
  readonly key = this._key.asReadonly();
  private saveHandle: ReturnType<typeof setTimeout> | null = null;

  /** The user's arrangement, by panel path. */
  private readonly _prefs = signal<PanelPrefs>({});
  readonly prefs = this._prefs.asReadonly();

  /** How large each container is along its own direction, by panel path. */
  private readonly _sizes = signal<Readonly<Record<string, number>>>({});
  readonly sizes = this._sizes.asReadonly();

  /** How wide each container is whatever its direction, by panel path: what the paging threshold reads. */
  private readonly _widths = signal<Readonly<Record<string, number>>>({});
  readonly widths = this._widths.asReadonly();

  /**
   * The page open in each exclusive container, by container path: a page's key, or `null` when
   * the user closed the open page and left none. Absent means "not chosen this visit" and the
   * first page opens. Never stored: a form opens on its first page every time.
   */
  private readonly _active = signal<PanelActive>({});
  readonly active = this._active.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.flush());
  }

  /** Switches to a layout and loads what the user arranged last time. */
  open(layoutKey: string): void {
    if (this._key() === layoutKey) {
      return;
    }

    this.flush();
    this._key.set(layoutKey);
    this._prefs.set(this.storage.read(layoutKey));
    this._active.set({});
  }

  /** Remembers the size the user dragged a panel to. */
  setSize(path: string, size: string): void {
    this.patch(path, { size });
  }

  /** Remembers that the user closed or opened a panel, and how much room its container had. */
  setCollapsed(path: string, isCollapsed: boolean, at?: number): void {
    this.patch(path, { isCollapsed, at });
  }

  /** The page the user opened in an exclusive container — `null` for none. Kept for this visit only. */
  setActive(containerPath: string, active: string | null): void {
    this._active.update(pages => ({ ...pages, [containerPath]: active }));
  }

  /** Forgets the user's open or close of one panel — the size they dragged it to stays. */
  forgetChoice(path: string): void {
    const { isCollapsed: _choice, at: _at, ...rest } = this.prefs()[path] ?? {};
    this.commit({ ...this.prefs(), [path]: rest });
  }

  /** Forgets everything about one panel, so it goes back to how the screen defines it. */
  reset(path: string): void {
    const next = { ...this.prefs() };
    delete next[path];
    this.commit(next);
  }

  /** Forgets the whole arrangement. Measurements are not the user's, and stay. */
  resetAll(): void {
    this.commit({});
  }

  /**
   * A container reporting its size along its direction and its width. Repeats are ignored so
   * the view is not rebuilt for nothing.
   */
  measure(path: string, along: number, width: number = along): void {
    const size = Math.round(along);
    const wide = Math.round(width);

    if (this.sizes()[path] !== size) {
      this._sizes.update(sizes => ({ ...sizes, [path]: size }));
    }

    if (this.widths()[path] !== wide) {
      this._widths.update(widths => ({ ...widths, [path]: wide }));
    }
  }

  /** A container that has left the screen. */
  forget(path: string): void {
    this._sizes.update(sizes => {
      const { [path]: _gone, ...rest } = sizes;
      return rest;
    });
    this._widths.update(widths => {
      const { [path]: _gone, ...rest } = widths;
      return rest;
    });
  }

  private patch(path: string, change: PanelPref): void {
    const current = this.prefs();
    this.commit({ ...current, [path]: { ...current[path], ...change } });
  }

  private commit(prefs: PanelPrefs): void {
    this._prefs.set(prefs);

    // Dragging a splitter fires dozens of changes a second; write once the user pauses.
    if (this.saveHandle !== null) {
      clearTimeout(this.saveHandle);
    }

    this.saveHandle = setTimeout(() => this.flush(), PanelsState.SaveDelayMs);
  }

  private flush(): void {
    if (this.saveHandle !== null) {
      clearTimeout(this.saveHandle);
      this.saveHandle = null;
    }

    if (this._key()) {
      this.storage.write(this._key(), this.prefs());
    }
  }
}
