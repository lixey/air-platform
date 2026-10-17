import { computed, linkedSignal, type Signal, untracked } from '@angular/core';
import { browserStorage, userStorageKey } from '../app/auth/user-storage';
import type {
  GridModel,
  GridNavNamedFilterGroupModel,
  GridNavNamedFilterModel,
  NavElementModel,
  UserProfileDto,
} from '../core/generated/contracts';

/** A named filter of a grid's nav: a toggle, or a group of one choice. */
type NamedFilter = GridNavNamedFilterModel | GridNavNamedFilterGroupModel;

/** What the state of a grid's named filters is given of its grid. */
export interface GridNamedFiltersOptions {
  readonly model: () => GridModel | null;
  /** What starts the state over when it changes. */
  readonly resetBy: () => unknown;
  /** The signed-in user, whose entry the browser keeps; null for none. */
  readonly user: () => Pick<UserProfileDto, 'id'> | null;
}

/**
 * The named filters of one grid: the keys chosen, started from the model and the kept choice at every generation. The
 * start is the elements the model marks selected, which is what the opening page was read under, and, for a toggle or
 * a group marked `isRemembered`, the choice the browser keeps in their place: a toggle is on when its key is kept, a
 * group stands on the kept key of one of its items, and on its marked item when none is kept. A kept key the model does
 * not offer, or one of a filter that is not remembered, is never chosen.
 *
 * The browser keeps, under `ui:u{id}:named-filters:{service}` (`userStorageKey`), a JSON array of the chosen keys of
 * the grid's remembered filters, written at a press of one of them. An unreadable entry is an empty one, a failed
 * write is silent, and without a signed-in user nothing is read or written.
 *
 * Created in a field of the grid service. It knows nothing of the service: it is given its options.
 */
export class GridNamedFilters {
  /** The toggles and the groups of the nav, in its order: the start part, the middle, the end, the More menu. */
  private readonly filters = computed<readonly NamedFilter[]>(() => {
    const nav = this.options.model()?.nav;
    return nav ? [...nav.start, ...nav.elements, ...nav.end, ...nav.more].filter(isNamedFilter) : [];
  });

  /** The state a generation starts from. */
  private readonly start = computed(() => {
    this.options.resetBy();
    return untracked(() => this.startOf(this.filters()));
  });

  private readonly _chosen = linkedSignal({ source: this.start, computation: start => start.chosen });
  /** The keys of the pressed elements, in the nav's order. */
  readonly chosen: Signal<readonly string[]> = this._chosen.asReadonly();
  /** Whether the state started apart from what the opening answer was read under. */
  readonly startsApart: Signal<boolean> = computed(() => this.start().startsApart);

  constructor(private readonly options: GridNamedFiltersOptions) {}

  /** Whether the toggle or the item of this key is pressed; reads a signal. */
  isOn(key: string): boolean {
    return this.chosen().includes(key);
  }

  /** Presses a toggle or an item, keeps the choice of a remembered filter, and says whether the keys changed. */
  press(key: string): boolean {
    const filters = this.filters();
    const chosen = untracked(this.chosen);
    const owner = filters.find(filter => keysOf(filter).includes(key));
    if (!owner) {
      return false;
    }

    let next: readonly string[];
    if (owner.kind === 'GridNavNamedFilter') {
      next = chosen.includes(key) ? chosen.filter(other => other !== key) : [...chosen, key];
    } else if (chosen.includes(key)) {
      return false;
    } else {
      const items = keysOf(owner);
      next = [...chosen.filter(other => !items.includes(other)), key];
    }
    const order = filters.flatMap(keysOf);
    this._chosen.set(order.filter(other => next.includes(other)));
    if (owner.isRemembered) {
      this.keep(filters);
    }
    return true;
  }

  private startOf(filters: readonly NamedFilter[]): { readonly chosen: readonly string[]; readonly startsApart: boolean } {
    const marked = filters.flatMap(filter => markedOf(filter));
    const kept = this.kept(filters);
    if (!kept) {
      return { chosen: marked, startsApart: false };
    }
    const chosen = filters.flatMap(filter => (filter.isRemembered ? rememberedOf(filter, kept) : markedOf(filter)));
    return { chosen, startsApart: chosen.length !== marked.length || chosen.some((key, index) => key !== marked[index]) };
  }

  /** The kept keys; null when the grid remembers nothing or nobody is signed in, and nothing is read. */
  private kept(filters: readonly NamedFilter[]): readonly string[] | null {
    const key = this.storageKey(filters);
    if (!key) {
      return null;
    }
    try {
      const raw = browserStorage()?.getItem(key);
      const parsed: unknown = raw ? JSON.parse(raw) : null;
      return Array.isArray(parsed) ? parsed.filter(item => typeof item === 'string') : [];
    } catch {
      return [];
    }
  }

  /** Writes the chosen keys of the remembered filters. */
  private keep(filters: readonly NamedFilter[]): void {
    const key = this.storageKey(filters);
    if (!key) {
      return;
    }
    const remembered = filters.filter(filter => filter.isRemembered).flatMap(keysOf);
    try {
      browserStorage()?.setItem(key, JSON.stringify(untracked(this.chosen).filter(chosen => remembered.includes(chosen))));
    } catch {
      // A full or blocked storage must never break the grid.
    }
  }

  /** The browser key of the grid's entry; null when no filter is remembered, the grid names no service or nobody is signed in. */
  private storageKey(filters: readonly NamedFilter[]): string | null {
    const service = untracked(this.options.model)?.service;
    const user = untracked(this.options.user);
    return service && user && filters.some(filter => filter.isRemembered) ? userStorageKey(user, `named-filters:${service}`) : null;
  }
}

function isNamedFilter(element: NavElementModel): element is NamedFilter {
  return element.kind === 'GridNavNamedFilter' || element.kind === 'GridNavNamedFilterGroup';
}

/** The keys a filter can add to the request: a toggle's own, a group's items'. */
function keysOf(filter: NamedFilter): readonly string[] {
  return filter.kind === 'GridNavNamedFilter' ? [filter.key] : filter.items.map(item => item.key);
}

/** The keys of a filter the model marks selected: the toggle that is on, the one item a group was read under. */
function markedOf(filter: NamedFilter): readonly string[] {
  if (filter.kind === 'GridNavNamedFilter') {
    return filter.isSelected ? [filter.key] : [];
  }
  const item = filter.items.find(candidate => candidate.isSelected);
  return item ? [item.key] : [];
}

/**
 * The keys a remembered filter starts on under the kept choice: a toggle on its key when the entry keeps it, a group
 * on the first of its items the entry keeps, and on its marked item when the entry keeps none.
 */
function rememberedOf(filter: NamedFilter, kept: readonly string[]): readonly string[] {
  if (filter.kind === 'GridNavNamedFilter') {
    return kept.includes(filter.key) ? [filter.key] : [];
  }
  const item = filter.items.find(candidate => kept.includes(candidate.key));
  return item ? [item.key] : markedOf(filter);
}
