import { computed, inject, linkedSignal, Service } from '@angular/core';
import { AppService } from '../app/app.service';
import { AuthService } from '../app/auth/auth.service';
import { browserStorage, userStorageKey } from '../app/auth/user-storage';
import type { MenuItemDto } from '../core/generated/contracts';

/**
 * What the signed-in user has pinned.
 *
 * A pin is the path of a menu entry and nothing more. What a tab draws — the name, the icon, the badge — is the entry
 * the current menu holds for that path, in the language of the metadata the user has now; a path the menu does not
 * offer is not drawn and is kept, because the menu is empty at startup, after a failed metadata request and during a
 * reload of the rights, and dropping unknown paths then would quietly delete the user's pins.
 *
 * The paths are the user's own, under `ui:u{id}:layout:pins` (`userStorageKey`); without a signed-in user nothing is
 * read or written. Pinning what is already pinned changes nothing, and closing a tab removes by the same path.
 */
@Service()
export class LayoutPinsService {
  private readonly auth = inject(AuthService);
  private readonly app = inject(AppService);

  /** The pinned paths of the signed-in user, in the order pinned. */
  private readonly paths = linkedSignal<number | null, readonly string[]>({
    source: () => this.auth.currentUser()?.id ?? null,
    computation: id => (id === null ? [] : readPins(id)),
  });

  /** What the strip draws: each pinned path by the entry the current menu holds for it. */
  readonly pins = computed<readonly MenuItemDto[]>(() =>
    this.paths().flatMap(path => {
      const item = this.app.findMenuItem(path);
      return item ? [{ ...item, path, items: [] }] : [];
    }),
  );

  isPinned(path: string | undefined | null): boolean {
    return !!path && this.paths().includes(path);
  }

  /** Adds the entry's path, unless it is already there. */
  pin(item: MenuItemDto): void {
    if (!item?.path || this.isPinned(item.path)) {
      return;
    }

    this.commit([...this.paths(), item.path]);
  }

  unpin(path: string | undefined | null): void {
    if (!path) {
      return;
    }

    this.commit(this.paths().filter(pinned => pinned !== path));
  }

  toggle(item: MenuItemDto): void {
    if (this.isPinned(item?.path)) {
      this.unpin(item.path);
    } else {
      this.pin(item);
    }
  }

  private commit(paths: readonly string[]): void {
    const id = this.auth.currentUser()?.id;
    if (id === undefined) {
      return;
    }
    this.paths.set(paths);
    try {
      browserStorage()?.setItem(userStorageKey({ id }, 'layout:pins'), JSON.stringify(paths));
    } catch {
      // A browser that refuses to store simply does not remember; nothing else changes.
    }
  }
}

/** The paths the user stored, minus anything that is not one: browser storage is the user's to edit. */
export function readPins(id: number, storage: Pick<Storage, 'getItem'> | undefined = browserStorage()): readonly string[] {
  try {
    const parsed: unknown = JSON.parse(storage?.getItem(userStorageKey({ id }, 'layout:pins')) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter((path): path is string => typeof path === 'string' && path.length > 0) : [];
  } catch {
    // A storage the browser refuses, or a text that is no JSON, reads as no pins: nothing else depends on them.
    return [];
  }
}
