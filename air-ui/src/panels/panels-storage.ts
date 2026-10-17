import { inject, InjectionToken } from '@angular/core';
import { AuthService } from '../app/auth/auth.service';
import { browserStorage, userStorageKey } from '../app/auth/user-storage';
import type { UserProfileDto } from '../core/generated/contracts';

/** What one panel remembers between visits. */
export interface PanelPref {
  /** Size the user dragged it to. */
  size?: string;

  /** Whether the user closed or opened it. Absent means "never touched". */
  isCollapsed?: boolean;

  /**
   * What the container around it measured when that choice was made. A choice holds while
   * the container stays on the same side of the panel's threshold, and is forgotten when
   * the container crosses it. A choice without this is ignored for a panel that has a
   * threshold.
   */
  at?: number;
}

/** Everything one layout remembers, keyed by panel path (`side/log`). */
export type PanelPrefs = Record<string, PanelPref>;

/**
 * Where the user's arrangement is kept. The browser is the default; swap in your own
 * to store it on the server and have layouts follow the user between devices.
 */
export interface PanelsStorage {
  read(layoutKey: string): PanelPrefs;

  write(layoutKey: string, prefs: PanelPrefs): void;
}

/**
 * Keeps each signed-in user's arrangement in this browser, under `ui:u{id}:panels:{layoutKey}` (`userStorageKey`);
 * without a signed-in user nothing is read or written.
 */
export class LocalPanelsStorage implements PanelsStorage {
  constructor(private readonly currentUser: () => UserProfileDto | null) {}

  read(layoutKey: string): PanelPrefs {
    try {
      const key = this.storageKey(layoutKey);
      const raw = key ? browserStorage()?.getItem(key) : null;
      const parsed = raw ? JSON.parse(raw) : null;
      return parsed && typeof parsed === 'object' ? (parsed as PanelPrefs) : {};
    } catch {
      return {};
    }
  }

  write(layoutKey: string, prefs: PanelPrefs): void {
    try {
      const key = this.storageKey(layoutKey);
      if (key) {
        browserStorage()?.setItem(key, JSON.stringify(prefs));
      }
    } catch {
      // A full or blocked storage must never break the layout.
    }
  }

  /**
   * Nothing here names the library or the platform it ends up in. A stored key outlives
   * every rename — the day this engine is called something else, what people arranged is
   * still theirs.
   */
  private storageKey(layoutKey: string): string | null {
    const user = this.currentUser();
    return user ? userStorageKey(user, `panels:${layoutKey}`) : null;
  }
}

/** Nothing is remembered. Useful in tests and on screens that should always start fresh. */
export class NoPanelsStorage implements PanelsStorage {
  read(): PanelPrefs {
    return {};
  }

  write(): void {
    // Intentionally empty.
  }
}

export const PANELS_STORAGE = new InjectionToken<PanelsStorage>('PANELS_STORAGE', {
  providedIn: 'root',
  factory: () => {
    if (typeof localStorage === 'undefined') {
      return new NoPanelsStorage();
    }
    const auth = inject(AuthService);
    return new LocalPanelsStorage(() => auth.currentUser());
  },
});
