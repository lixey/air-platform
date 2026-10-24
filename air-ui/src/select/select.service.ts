import { computed, inject, Injector, Service, type Signal, signal, untracked } from '@angular/core';
import { type Observable, Subject } from 'rxjs';
import type { OverlayDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import type { OverlayRef } from '../overlay/overlay-ref';
import { OverlayService } from '../overlay/overlay.service';
import { serviceMenu } from '../panels/service-menu';
import {
  BAR_HEIGHT,
  gridListWidth,
  LIST_PADDING,
  MOST_OVERLAY_WIDTH,
  OPTION_GAP,
  OPTION_MIN_HEIGHT,
  SELECT_LIST_HEIGHT,
} from './select-list';
import type { SelectOption } from './select-option';
import { isPicked, optionOfRow } from './select-option';
import type { SelectSource } from './select-source';

/** What the select needs of the grid inside its overlay: to search it, to move along it, to read its active row. */
export interface SelectGridLink {
  setSearch(text: string): void;
  moveActive(delta: number): void;
  setActive(index: number): void;
  activeRow(): Readonly<Record<string, unknown>> | null;
  rowCount(): number;
  /** The id of the grid's table, the list a search box controls; null before it is drawn. */
  tableId(): string | null;
  /** The id of the active row's active cell; null with none. */
  activeCellId(): string | null;
  /** The fitted content width of the grid's table, in pixels; 0 before it is measured. */
  tableWidth?(): number;
}

/**
 * A static list's width and the panel of the picked beside the list; the width of a grid's list and the most the
 * overlay takes are `select-list.ts`'s.
 */
const OPTIONS_WIDTH = 320;
const PICKED_PANEL_WIDTH = 260;

/**
 * One select's state, provided by whoever opens it — a reference header, a `ui-select`, a
 * filter overlay — and injected by the overlay body it opens: what to pick from, whether one
 * or several, what is picked, what is being picked while the overlay is open, and the grid
 * inside the overlay when there is one. A single pick closes the overlay and emits at once; a
 * multi pick is a draft until `apply`, and closing discards it.
 */
@Service({ autoProvided: false })
export class SelectService {
  private static next = 0;

  private readonly overlay = inject(OverlayService);
  private readonly texts = inject(TextsService);
  /** The overlay's card, one at a time. */
  private readonly menu = this.overlay.menu();
  private readonly gridLink = signal<SelectGridLink | null>(null);

  private readonly _model = signal<SelectSource>({ label: '' });
  readonly model = this._model.asReadonly();
  private readonly _isMultiple = signal(false);
  readonly isMultiple = this._isMultiple.asReadonly();

  /** What is picked, as the host holds it: one option or none in single mode, any number in multiple. */
  private readonly _picked = signal<readonly SelectOption[]>([]);
  readonly picked = this._picked.asReadonly();

  /** The draft of a multi pick while the overlay is open; the picked, in single mode. */
  private readonly _draft = signal<readonly SelectOption[]>([]);
  readonly draft = this._draft.asReadonly();

  /** The text of the overlay's search box. */
  private readonly _searchText = signal('');
  readonly searchText = this._searchText.asReadonly();

  /** The least length the text of a picker's open list does not reach; null while it reaches it and in a select. */
  private readonly _shortOf = signal<number | null>(null);
  readonly shortOf = this._shortOf.asReadonly();

  /** The active static option, by index into `options`; -1 for none. A grid keeps its own active row. */
  private readonly _activeIndex = signal(-1);
  readonly activeIndex = this._activeIndex.asReadonly();

  /** Emits what was picked when a pick is committed: at once in single mode, on `apply` in multiple. */
  private readonly _committed = new Subject<readonly SelectOption[]>();
  readonly committed: Observable<readonly SelectOption[]> = this._committed.asObservable();

  /** Whether the overlay is open, and the id of its card while it is: what the field's combobox says it controls. */
  readonly isOpen = this.menu.isOpen;
  readonly popupId = this.menu.id;

  /** The ids of the static list and of its active option, which the search box names. */
  readonly listId = `ui-select-list-${++SelectService.next}`;
  readonly activeOptionId = computed(() => (this.activeIndex() >= 0 ? `${this.listId}-${this.activeIndex()}` : null));

  /** The list the search box controls: a static list's id, or the grid's table. */
  readonly controlsId: Signal<string | null> = computed(() => {
    const link = this.gridLink();
    if (link) {
      return link.tableId();
    }
    return this.model().options ? this.listId : null;
  });

  /** The search box's active descendant: the active static option, or the grid's active cell. */
  readonly activeDescendantId: Signal<string | null> = computed(() => {
    const link = this.gridLink();
    if (link) {
      return link.activeCellId();
    }
    return this.model().options ? this.activeOptionId() : null;
  });

  /** The static options minus the picked in multiple mode, narrowed by the search box. */
  readonly options = computed<readonly SelectOption[]>(() => {
    const options = this.model().options ?? [];
    const term = this.searchText().trim().toLowerCase();
    const picked = this.draft();
    return options.filter(
      option => (!this.isMultiple() || !isPicked(option, picked)) && (!term || option.label.toLowerCase().includes(term)),
    );
  });

  /**
   * Opens the overlay as a menu from the anchor, telling the host the size it wants — wide enough
   * for the label columns and the panel of the picked beside them, tall enough for its bar (`BAR_HEIGHT`) and the
   * list — so the host opens it where that fits; the body it mounts injects this very service. Above its field, or
   * slid over it, the card keeps that height from the opening on, so the bar keeps its place while the list loads and
   * is narrowed; below its field it is as tall as its bar and its rows. What
   * to pick from and what the host holds are taken even when the overlay is open already.
   */
  open(
    { model, picked, multiple = false }: { model: SelectSource; picked: readonly SelectOption[]; multiple?: boolean },
    anchor: HTMLElement | null,
    injector: Injector,
  ): OverlayRef {
    this._model.set(model);
    this._picked.set([...picked]);
    if (this.menu.ref) {
      return this.menu.ref;
    }
    this._isMultiple.set(multiple);
    this._draft.set([...this.picked()]);
    this._searchText.set('');
    this._activeIndex.set(-1);

    const menu = serviceMenu('select.grid', { kind: 'SelectOverlay' }, { token: SelectService, instance: this, parent: injector });
    const list = this.model().options ? OPTIONS_WIDTH : gridListWidth(this.model().labelPaths?.length ?? 1);
    const width = Math.min(MOST_OVERLAY_WIDTH, list + (multiple ? PICKED_PANEL_WIDTH : 0));
    const overlayModel: OverlayDto = { ...menu.model, width: `${width}px`, height: `${BAR_HEIGHT + this.listHeight()}px` };
    const ref = this.menu.open(overlayModel, anchor, menu.injector, model.label || this.texts.of('SearchOptions'));
    ref.ask(
      computed(() => {
        const measured = this.gridLink()?.tableWidth?.() ?? 0;
        if (measured <= 0) {
          return null;
        }
        const fittedList = Math.max(list, measured + 2);
        const fittedWidth = Math.min(MOST_OVERLAY_WIDTH, fittedList + (this.isMultiple() ? PICKED_PANEL_WIDTH : 0));
        return { width: `${fittedWidth}px` };
      }),
    );
    ref.closed.subscribe(() => {
      this.gridLink.set(null);
      this._searchText.set('');
    });
    return ref;
  }

  /**
   * What is picked from, said by an opener that draws the list itself: a single pick, with the grid of a list drawn
   * before let go of and its short text forgotten. No overlay of this service opens.
   */
  setSource(model: SelectSource): void {
    this._model.set(model);
    this._isMultiple.set(false);
    this._shortOf.set(null);
    this.gridLink.set(null);
  }

  close(): void {
    this.menu.close();
  }

  /** The grid inside the overlay says it is there; the search box and the keys reach it through this. */
  attachGrid(link: SelectGridLink): void {
    this.gridLink.set(link);
  }

  search(text: string): void {
    this._shortOf.set(null);
    this._searchText.set(text);
    this._activeIndex.set(-1);
    untracked(this.gridLink)?.setSearch(text);
  }

  /** A picker's open list is told the least length its text does not reach, or that it reaches it again. */
  setShortOf(least: number | null): void {
    this._shortOf.set(least);
  }

  /** Moves the active row of the grid, or the active static option, by `delta`. */
  moveActive(delta: number): void {
    const link = untracked(this.gridLink);
    if (link) {
      link.moveActive(delta);
      return;
    }
    // From no active option either direction lands on the first, and a move never goes below it: a negative index is
    // `setActive`'s word for the last
    this.setActive(Math.max(0, this.activeIndex() + delta));
  }

  /** Makes the row or option at `index` active; a negative index means the last one. */
  setActive(index: number): void {
    const link = untracked(this.gridLink);
    const count = link?.rowCount() ?? this.options().length;
    const wanted = index < 0 ? count - 1 : index;
    const bounded = count === 0 ? -1 : Math.min(Math.max(0, wanted), count - 1);
    if (link) {
      link.setActive(bounded);
      return;
    }
    this._activeIndex.set(bounded);
  }

  /** Picks the grid's active row, or the active static option, when there is one. */
  pickActive(): boolean {
    const link = untracked(this.gridLink);
    if (link) {
      const row = link.activeRow();
      if (!row) {
        return false;
      }
      this.pickRow(row);
      return true;
    }
    const option = this.options()[this.activeIndex()];
    if (!option) {
      return false;
    }
    this.pick(option);
    return true;
  }

  pickRow(row: Readonly<Record<string, unknown>>): void {
    this.pick(optionOfRow(row, this.model().labelPaths));
  }

  /** A pick: in single mode it is the result and the overlay closes; in multiple it joins or leaves the draft. */
  pick(option: SelectOption): void {
    if (!this.isMultiple()) {
      this.commit([option]);
      return;
    }
    this._draft.update(current => (isPicked(option, current) ? current.filter(o => o.id !== option.id) : [...current, option]));
  }

  removeFromDraft(option: SelectOption): void {
    this._draft.update(current => current.filter(o => o.id !== option.id));
  }

  clearDraft(): void {
    this._draft.set([]);
  }

  /** The draft becomes the pick; the overlay closes. */
  apply(): void {
    this.commit(this.draft());
  }

  /** What the host holds, written by the host itself: the overlay opens from it next time. */
  setPicked(options: readonly SelectOption[]): void {
    this._picked.set([...options]);
  }

  private commit(options: readonly SelectOption[]): void {
    this._picked.set([...options]);
    this._draft.set([...options]);
    this._committed.next(this.picked());
    this.menu.close();
  }

  /** The height the list wants: a fixed list's rows, up to the select's height; a sourced list the select's height. */
  private listHeight(): number {
    const options = this.model().options;
    return options
      ? Math.min(SELECT_LIST_HEIGHT, 2 * LIST_PADDING + options.length * (OPTION_MIN_HEIGHT + OPTION_GAP))
      : SELECT_LIST_HEIGHT;
  }
}
