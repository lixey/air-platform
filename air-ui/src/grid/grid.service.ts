import { computed, DestroyRef, effect, inject, linkedSignal, Service, type Signal, signal, untracked } from '@angular/core';
import type { Subscription } from 'rxjs';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { AuthService } from '../app/auth/auth.service';
import { Api } from '../core/api';
import type {
  GridColumnDto,
  GridModel,
  GridPageDto,
  GridRequest,
  GridServiceEndpoints,
  OverlayModelDto,
  SortDirection,
  SortRule,
} from '../core/generated/contracts';
import { type IsCurrent, Lifetime } from '../core/lifetime';
import type { OverlayRef } from '../overlay/overlay-ref';
import { parentParams, SCREEN_ADDRESS } from '../screen/screen-address';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { ScreenMutations, type ScreenOperation } from '../screen/screen-mutations';
import type { SelectOption } from '../select/select-option';
import { optionOfPickedValue } from '../select/select-option';
import type { SelectField } from '../select/select-source';
import { SelectService } from '../select/select.service';
import { GridColumnState } from './column/grid-column-state';
import { SELECTION_COLUMN_DTO } from './column/grid-selection-column';
import {
  conjunctsOf,
  fieldOf,
  type GridFilter,
  GridFilterSet,
  initialFiltersOf,
  isEmptyFilter,
  isPick,
  normalizedFilter,
  referenceFilterOf,
  rulesOf,
  toFilterNode,
} from './filter/grid-filter-set';
import { GridActiveRow } from './grid-active-row';
import { GridCellEdits } from './grid-cell-edits';
import { GridChangedCells } from './grid-changed-cells';
import { GridCount } from './grid-count';
import { GridDeletion } from './grid-deletion';
import { GridForms } from './grid-forms';
import { GridNamedFilters } from './grid-named-filters';
import { buildGridRequest } from './grid-request';
import { type GridRow, type GridRowKey, keyOf } from './grid-row';
import { GridSelection } from './grid-selection';

/** What the table that draws a grid names for a search box that drives it. */
export interface GridTableIds {
  readonly table: string;
  readonly activeCell: Signal<string | null>;
  readonly tableWidth?: Signal<number>;
}

/** The rows of a picker's list while its text is shorter than the least length: one list, so nothing is drawn anew. */
const NO_ROWS: readonly GridRow[] = [];

/**
 * Everything one grid holds on the client: the model the server described, the query state — page, sort, filters,
 * the columns' filters, the search, the named filters — the selection and the active row, and the page of rows
 * the state asks for. The page that came with the screen is the first answer; the server is
 * asked, for the data alone, only when something changes. A sort, a filter, a search or a press of a named filter
 * returns the grid to the first page and clears the selection; a page change clears it too.
 * A new model, or an invalidation of the screen's mutation scope, starts the whole state over: the search from the
 * model's initial search, the general filters from its initial filter and the named filters from the elements the
 * model marks selected and the choice the browser keeps (`GridNamedFilters`). A state that starts apart from what the
 * opening page was read under, a kept choice that differs, asks page 1 and the count again at once.
 *
 * A failed page leaves the rows and the page number of the last answer, and `retry` asks the failed
 * request again; a failed count leaves the total open until `recount`.
 *
 * The active row is deliberate: none when the grid opens; after a change of sort, filter,
 * search or page the first row, if a row was active; after a reload of the same page the row
 * with the same key, or the first when it is gone.
 *
 * A save confirmed by a form this grid opened itself — from the nav's Add or from a row — while it is open marks,
 * on the page its refresh loads, the saved record's cells whose value or hue changed (`GridChangedCells`); a record
 * the form added has its non-empty cells marked. The marks add up over saves and survive a reload of the same page;
 * a change of sort, filter, search or page clears them, and a new model or an invalidated scope starts them over.
 *
 * A cell of a column the model marks editable is edited in place and saved by the grid's form (`GridCellEdits`): its
 * answer replaces the row, tells the screen, marks the cells it changed and loads the page again; a change of the
 * request or of the model cancels the edit.
 *
 * Provided by the grid component, so the nav's elements, the cells and the overlays
 * opened from the grid find it by injection. What the user changes about the columns — a
 * width, a fit to content, a hiding — lasts the session: remembering it across visits is
 * parked, and nothing is written to the panels storage.
 */
@Service({ autoProvided: false })
export class GridService {
  private readonly address = inject(SCREEN_ADDRESS, { optional: true });
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly mutations = inject(ScreenMutations, { optional: true });
  /** The select this grid stands in, when it is the list of one: rows are picked, not selected or opened. */
  private readonly select = inject(SelectService, { optional: true });
  /** A child of the screen's lifetime; a capture of it also holds the generation it was taken in. */
  private readonly lifetime = (inject(SCREEN_LIFETIME, { optional: true }) ?? new Lifetime()).child();

  /** The model and data inputs of the component that draws the grid. */
  private readonly bound = signal<{ readonly model: Signal<GridModel | null | undefined>; readonly data: Signal<unknown> } | null>(null);
  readonly model = computed<GridModel | null>(() => this.bound()?.model() ?? null);
  /** Advanced by an invalidation of the screen's mutation scope. */
  private readonly invalidations = signal(0);
  /** What the whole state belongs to: a new model or an invalidated scope starts it over. */
  private readonly generation = computed(() => ({ model: this.model(), invalidations: this.invalidations() }));
  /**
   * The page that came with the screen: the answer to page one, so opening asks nothing more. An invalidated scope
   * forgets it, so the rows of the former identity are not drawn again; the next data the screen hands is the next one.
   */
  private readonly opening = linkedSignal<unknown, GridPageDto<GridRow> | null>({
    source: () => this.bound()?.data(),
    computation: data => (isPage(data) ? data : null),
  });
  /** What the page state belongs to: the generation and the page that came with the screen. */
  private readonly held = computed(() => ({ generation: this.generation(), opening: this.opening() }));

  readonly selection = new GridSelection(this.held);
  readonly deletion = new GridDeletion(this.generation);
  /** The cells the saves of the forms this grid opened changed, settled whenever a page of rows arrives. */
  private readonly changes = new GridChangedCells(this.generation);
  readonly columnState = new GridColumnState(() => this.model()?.columns ?? [], this.generation);
  /** The general filters' picks, started from the model's initial filter at every generation. */
  private readonly filterSet = new GridFilterSet(this.generation, generation => initialFiltersOf(generation.model).filter(isPick));
  private readonly columnFilterSet = new GridFilterSet(this.generation);
  /** Every declared column with the opening fit and the user's widths and hiding applied, in the server's order. */
  readonly allColumns = this.columnState.columns;
  readonly service = computed(() => this.model()?.service ?? null);
  readonly isSelectable = computed(() => this.model()?.isSelectable !== false && !this.select);
  /** Whether the grid is the list of a select. */
  readonly isPicking = computed(() => this.select !== null);
  /** Whether the grid is the list of a picker: it stands under its own nav, which a select's list does not draw. */
  readonly isPickerList = computed(() => this.select?.model().isPicker === true);
  /** The field the picker's list was opened from, whose remote the list's search box is; null in any other grid. */
  readonly pickerField = computed<SelectField | null>(() => (this.isPickerList() ? (this.select?.model().field ?? null) : null));
  /** The ids the table that draws the grid names, its own and its active cell's; null while no table draws it. */
  readonly tableIds = signal<GridTableIds | null>(null);
  /** The least length the text of a picker's list does not reach; null in any other grid. */
  readonly shortOf = computed<number | null>(() => (this.isPickerList() ? (this.select?.shortOf() ?? null) : null));
  /** The columns the table draws: the selection column first when rows can be selected, the hidden ones out. */
  readonly tableColumns = computed<readonly GridColumnDto[]>(() => {
    const visible = this.allColumns().filter(column => !column.isHidden);
    return this.isSelectable() ? [SELECTION_COLUMN_DTO, ...visible] : visible;
  });
  private unsubscribeMutations: (() => void) | undefined;
  /** The form half: opening the declared form and watching the forms opened, so their saves refresh the grid. */
  private readonly forms = new GridForms({
    model: this.model,
    service: this.service,
    capture: () => this.capture(),
    reload: () => this.reload(),
  });
  private readonly operations = new Set<ScreenOperation>();

  /** The page asked for, and the page of the rows held once they arrived. */
  private readonly _page = linkedSignal({ source: this.held, computation: held => held.opening?.page ?? 1 });
  readonly page = this._page.asReadonly();
  private readonly _sort = linkedSignal<unknown, readonly SortRule[]>({ source: this.generation, computation: () => [] });
  readonly sort = this._sort.asReadonly();
  /** The search; the opening page was searched by the model's initial search, so the box shows it and asks nothing. */
  private readonly _search = linkedSignal({ source: this.generation, computation: generation => generation.model?.initialSearch ?? '' });
  readonly search = this._search.asReadonly();
  /**
   * The general filters' picks: one `in` rule per reference column, by its key (`referenceFilterOf`). The opening page
   * was read under the model's initial filter, so the general filters start from it and ask nothing.
   */
  readonly filterPicks = this.filterSet.state;
  /** The general filters' tree over the other columns, in normal form; none while it holds nothing. */
  private readonly _filterTree = linkedSignal<{ readonly model: GridModel | null }, GridFilter | undefined>({
    source: this.generation,
    computation: generation => toFilterNode(initialFiltersOf(generation.model).filter(filter => !isPick(filter))),
  });
  readonly filterTree = this._filterTree.asReadonly();
  /** The general filters as the request joins them by AND: the picks, then the tree. */
  readonly filters = computed<readonly GridFilter[]>(() => {
    const tree = this.filterTree();
    return tree ? [...this.filterPicks(), tree] : this.filterPicks();
  });
  /** The filters the columns hold, one per column. */
  readonly columnFilters = this.columnFilterSet.state;

  /** The named filters: the toggles that are on and the item each group stands on. */
  private readonly named = new GridNamedFilters({
    model: this.model,
    resetBy: this.generation,
    user: () => this.auth.currentUser(),
  });
  /** The keys of the pressed named filters, in the nav's order, as every request of the grid carries them. */
  readonly namedFilters = this.named.chosen;

  /** The page on screen: the last answer, or the one that came with the screen until the first answer. */
  private readonly _data = linkedSignal({ source: this.held, computation: held => held.opening });
  readonly data = this._data.asReadonly();
  /** Whether a page is held: the one that came with the screen, or one the server answered. */
  private readonly hasPage = computed(() => this.data() !== null);
  readonly pageSize = computed(() => this.data()?.pageSize ?? this.model()?.pageSize ?? 200);

  /** The request the state makes; data only once an answer is held, so the layout is never sent twice. */
  readonly request = computed<GridRequest>(() => ({
    ...parentParams(this.address?.(), this.service()),
    ...(this.select?.model().parent ?? {}),
    ...buildGridRequest({
      page: this.page(),
      pageSize: this.pageSize(),
      sort: this.sort(),
      filters: this.filters(),
      columnFilters: this.columnFilters(),
      search: this.search(),
      namedFilters: this.namedFilters(),
      isDataOnly: this.hasPage(),
      referenceLabelPaths: this.select?.model().labelPaths ?? null,
      isReferencePicker: this.select?.model().isPicker,
    }),
    ...(this.select?.model().patchSourceColumns?.length ? { patchSourceColumns: [...(this.select.model().patchSourceColumns ?? [])] } : {}),
    ...(this.select?.model().formDraft ? { formDraft: this.select.model().formDraft } : {}),
  }));

  /** The last page asked of the server, which a retry asks again. */
  private pageRequest: PageRequest | undefined;
  private inFlight: Subscription | undefined;
  private readonly _isLoading = linkedSignal({ source: this.generation, computation: () => false });
  readonly isLoading = this._isLoading.asReadonly();
  /** The failure of the last page asked; the rows and the page on screen are the last answer's. */
  private readonly _error = linkedSignal<unknown, unknown>({ source: this.generation, computation: () => undefined });
  readonly error = this._error.asReadonly();

  /** The row counter: a count is asked at once when the opening page leaves the total open. */
  private readonly count = new GridCount({
    capture: () => this.capture(),
    service: () => this.service(),
    body: service => ({
      ...parentParams(this.address?.(), service),
      filter: toFilterNode(this.filters()),
      columnFilter: toFilterNode(this.columnFilters()),
      search: this.search() ? this.search() : undefined,
      ...(this.namedFilters().length > 0 ? { namedFilters: [...this.namedFilters()] } : {}),
    }),
    isCounted: () => !this.isPicking(),
    resetBy: this.held,
    totalAtStart: () => this.opening()?.total ?? null,
  });

  /**
   * The page's rows; in a multi-pick select, minus the rows picked already. A picker's list whose text is shorter than
   * the least length shows none, pinned or not, whatever page it holds or receives: the page is kept, not shown.
   */
  readonly rows = computed<readonly GridRow[]>(() => {
    if (this.shortOf() !== null) {
      return NO_ROWS;
    }
    const rows = this.data()?.rows ?? [];
    const select = this.select;
    if (!select || !select.isMultiple()) {
      return rows;
    }
    const picked = new Set(select.draft().map(o => o.id));
    return rows.filter(row => {
      const key = keyOf(row);
      return key === null || !picked.has(key);
    });
  });
  readonly topRows = computed<readonly GridRow[]>(() => (this.shortOf() !== null ? NO_ROWS : (this.data()?.topRows ?? [])));
  readonly bottomRows = computed<readonly GridRow[]>(() => (this.shortOf() !== null ? NO_ROWS : (this.data()?.bottomRows ?? [])));
  readonly hasMore = computed(() => this.data()?.hasMore ?? false);
  readonly total = computed(() => this.count.countedTotal() ?? this.data()?.total ?? 0);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / Math.max(1, this.pageSize()))));
  readonly isCountLoading = this.count.isCountLoading;
  readonly isCountPending = this.count.isCountPending;
  /** Whether the total is unknown because its count failed. */
  readonly isCountFailed = this.count.isCountFailed;
  /** The deletion the answer to one page request verifies, kept through a retry of that request. */
  private verification: { request: PageRequest; result: NonNullable<ReturnType<GridDeletion['result']>> } | undefined;

  /** A new model or an invalidated scope cancels the page request of the former one: its answer would be dropped. */
  private readonly cancelObsoletePage = effect(() => {
    this.generation();
    untracked(() => {
      if (this.pageRequest && !this.pageRequest.isCurrent()) {
        this.inFlight?.unsubscribe();
        this.pageRequest = undefined;
        this.verification = undefined;
      }
    });
  });

  /**
   * A state of the named filters that starts apart from what the opening page was read under asks page 1 for the data
   * alone and the count under it, when the opening page is held: the screen read that page under no kept choice.
   */
  private readonly rereadApart = effect(() => {
    if (this.held().opening && this.named.startsApart()) {
      untracked(() => this.changeQuery(() => undefined));
    }
  });

  /** The active row, settled on the rows above whenever a page of them arrives. */
  private readonly active = new GridActiveRow(
    () => this.rows(),
    this.generation,
    () => this.select?.model().isPicker === true,
  );
  readonly activeRowIndex = this.active.index;
  readonly activeRow = computed<GridRow | null>(() => this.rows()[this.activeRowIndex()] ?? null);

  /** The cells edited in place: one edit, held by its row and column, cancelled by a change of the request or the model. */
  readonly edits = new GridCellEdits({
    resetBy: computed(() => [this.generation(), this.request()]),
    model: this.model,
    service: this.service,
    params: () => ({ ...parentParams(this.address?.(), this.service()), ...(this.select?.model().parent ?? {}) }),
    rows: this.rows,
    columns: this.tableColumns,
    isPicking: this.isPicking,
    capture: () => this.capture(),
    beginMutation: () => this.beginMutation(),
    finishMutation: operation => this.finishMutation(operation),
    adoptRow: (id, row) => this.adoptRow(id, row),
    moveActive: delta => this.moveActive(delta),
    reload: () => this.reload(),
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.release();
      this.lifetime.end();
    });
    this.subscribeMutations();
    this.select?.attachGrid({
      setSearch: text => this.setSearch(text),
      moveActive: delta => this.moveActive(delta),
      setActive: index => this.setActive(index),
      activeRow: () => this.activeRow(),
      rowCount: () => this.rows().length,
      tableId: () => this.tableIds()?.table ?? null,
      activeCellId: () => this.tableIds()?.activeCell() ?? null,
      tableWidth: () => this.tableIds()?.tableWidth?.() ?? 0,
    });
  }

  /**
   * Binds the component's inputs: the model the grid draws — another model starts from nothing — and the screen's
   * data, whose page is the first answer.
   */
  bind(model: Signal<GridModel | null | undefined>, data: Signal<unknown>): void {
    this.bound.set({ model, data });
  }

  /** The same page again: the active row is found by its key, or the first row takes its place. */
  reload(): void {
    if (!this.lifetime.isCurrent()) {
      return;
    }
    this.active.keepKey();
    this.requestPage();
    this.count.requestCount();
  }

  /** The page request that failed, asked again. */
  retry(): void {
    const request = this.pageRequest;
    if (request && this.error() !== undefined && request.isCurrent()) {
      this.send(request);
    }
  }

  /** Asks the count again, after it failed. */
  recount(): void {
    if (this.lifetime.isCurrent()) {
      this.count.requestCount();
    }
  }

  goToPage(page: number): void {
    const target = Math.max(1, Math.trunc(page));
    const maxPage = this.hasMore() ? Math.max(this.pageCount(), this.page() + 1) : this.pageCount();
    const next = Math.min(target, maxPage);
    if (next === this.page()) {
      return;
    }
    this.selection.clear();
    this.changes.clear();
    this.active.leave();
    this._page.set(next);
    this.requestPage();
  }

  /**
   * A direction sorts by the field, `null` returns to the default order. With `multi` the field
   * joins the columns already sorted, after them, or changes its direction there; without, it is
   * the only sort.
   */
  setSort(field: string, direction: SortDirection | null, multi = false): void {
    this.changeQuery(() => {
      if (!multi) {
        this._sort.set(direction ? [{ field, direction }] : []);
      } else {
        const current = [...this.sort()];
        const index = current.findIndex(s => s.field === field);
        if (direction === null) {
          if (index >= 0) {
            current.splice(index, 1);
          }
        } else if (index >= 0) {
          current[index] = { ...current[index], direction };
        } else {
          current.push({ field, direction });
        }
        this._sort.set(current);
      }
    }, false);
  }

  sortOf(field: string | null | undefined): SortDirection | null {
    const found = this.sort().find(s => s.field === field);
    return found?.direction ?? null;
  }

  /** The field's place in the sort, from 1, while more than one column is sorted; `null` otherwise. */
  sortPositionOf(field: string | null | undefined): number | null {
    const sort = this.sort();
    const index = sort.findIndex(s => s.field === field);
    return sort.length > 1 && index >= 0 ? index + 1 : null;
  }

  setSearch(text: string): void {
    const next = text.trim();
    if (next === this.search()) {
      return;
    }
    this.changeQuery(() => this._search.set(next));
  }

  /**
   * Replaces the general filters, as the nav's overlay applies them: a rule that carries what a select picked is
   * its field's pick, the others are joined by AND into the tree, two on one field both kept.
   */
  setFilters(filters: readonly GridFilter[]): void {
    this.changeQuery(() => {
      this.filterSet.set(filters.filter(isPick));
      this._filterTree.set(toFilterNode(filters.filter(filter => !isPick(filter))));
    });
  }

  /**
   * Sets one column's filter. A filter without a value is tolerated as a removal, for an application's header that
   * hands over what its control holds; the library's own headers and menus remove by `clearColumnFilter`.
   */
  setColumnFilter(filter: GridFilter): void {
    this.changeQuery(() => {
      if (isEmptyFilter(filter)) {
        this.columnFilterSet.removeByField(fieldOf(filter));
      } else {
        this.columnFilterSet.add(filter);
      }
    });
  }

  /** Removes one column's filter, as its header's or its menu's clear asks. */
  clearColumnFilter(field: string): void {
    this.changeQuery(() => this.columnFilterSet.removeByField(field));
  }

  columnFilterOf(field: string | null | undefined): GridFilter | undefined {
    return field ? this.columnFilterSet.get(field) : undefined;
  }

  /**
   * Sets the one general filter of a reference column (`referenceFilterOf`),
   * replacing the field's filter, or removes it when nothing is picked.
   */
  setReferenceFilter(field: string, options: readonly SelectOption[]): void {
    this.changeQuery(() => {
      const filter = referenceFilterOf(field, options);
      if (filter) {
        this.filterSet.add(filter);
      } else {
        this.filterSet.removeByField(field);
      }
    });
  }

  /**
   * What a reference column's filter picked, as options: the header's count and the
   * select's initial state. Reads the filters signal, so a `computed` follows it.
   */
  referencePicksOf(field: string | null | undefined): readonly SelectOption[] {
    if (!field) {
      return [];
    }
    const filter = this.filterPicks().find(f => fieldOf(f) === field);
    return (filter ? (rulesOf(filter)[0]?.picked ?? []) : []).map(optionOfPickedValue);
  }

  /** Removes the pick of a reference column's key. */
  removeFilter(field: string): void {
    this.changeQuery(() => this.filterSet.removeByField(field));
  }

  /**
   * Removes one filter the tree's AND joins (`conjunctsOf`) — a condition, or a group of them — as its chip asks; a
   * tree joined by OR is one such filter, so its chip clears the tree. The tree is in normal form again.
   */
  removeFilterItem(index: number): void {
    const tree = this.filterTree();
    if (!tree) {
      return;
    }
    this.changeQuery(() => {
      const items = conjunctsOf(tree).filter((_, i) => i !== index);
      this._filterTree.set(normalizedFilter({ kind: 'FilterGroup', logic: 'and', items }));
    });
  }

  /**
   * Removes the general filters, the ones the grid opened with among them: the model's initial filter returns only
   * with a new generation. The columns keep theirs, cleared from their headers, and the named filters stay pressed.
   */
  clearFilters(): void {
    if (this.filters().length === 0) {
      return;
    }
    this.changeQuery(() => {
      this.filterSet.clear();
      this._filterTree.set(undefined);
    });
  }

  /** Whether the toggle or the group's item of this key is pressed; reads a signal. */
  isNamedFilterOn(key: string): boolean {
    return this.named.isOn(key);
  }

  /**
   * Presses a toggle, which flips it, or an item, which takes its group's place; a press of the chosen item changes
   * nothing and asks nothing. The general filters stay as they are.
   */
  pressNamedFilter(key: string): void {
    if (this.named.press(key)) {
      this.changeQuery(() => undefined);
    }
  }

  /** The badge the last count answered for the element of this key; null before one did, when the model's stands. */
  namedFilterBadgeOf(key: string): number | null {
    return this.count.badges()?.[key] ?? null;
  }

  /** Whether a save of a form this grid opened changed the row's cell at the column's path; reads a signal. */
  isChanged(row: GridRow | null | undefined, path: string | null | undefined): boolean {
    return this.changes.isChanged(row, path);
  }

  setActive(index: number): void {
    this.active.set(index);
  }

  moveActive(delta: number): void {
    this.active.move(delta);
  }

  /** In a select, the active row is picked; elsewhere nothing happens and the caller opens the row instead. */
  pickActive(): boolean {
    return this.select?.pickActive() ?? false;
  }

  /**
   * Opens the form for the row with this key, by address: the form `formService` names, else the one the row
   * resolves, else the grid's own; returns `false` when the grid names no form. It opens in the overlay the page
   * carries under `forms` for that form when the row with this key names it, else in the overlay of the grid's own
   * form addressed to it, else in a dialog. In a select, picks the row instead.
   */
  openForm(id: GridRowKey | null, anchor?: HTMLElement | null, formService?: string | null, customOverlay?: OverlayModelDto | null): boolean {
    // In a select the row's link picks the row, as a click on it does
    if (this.select) {
      const row = this.rows().find(r => keyOf(r) === id) ?? null;
      if (row) this.select.pickRow(row);
      return row !== null;
    }
    const row = id !== null ? (this.rows().find(r => keyOf(r) === id) as Record<string, unknown> | undefined) : undefined;
    const rowForm = typeof row?.['form'] === 'string' ? row['form'] : null;
    const targetService = formService ?? rowForm;
    // The page's overlay is the row's alone: a form opened by a name the row does not carry takes none
    const rowOverlay = customOverlay ?? (targetService !== null && targetService === rowForm ? (this.data()?.forms?.[targetService] ?? null) : null);
    return this.forms.open(id, anchor, targetService, rowOverlay);
  }

  /** Nav Add and row edit share the same verified parent address. */
  withParentParams(model: OverlayModelDto): OverlayModelDto {
    return this.forms.withParentParams(model);
  }

  /** Only the explicitly associated form is a freshness trigger. */
  trackForm(ref: OverlayRef): void {
    this.forms.track(ref);
  }

  /**
   * Whether what is captured now is still current: the screen's lifetime, this grid's, the same model and the screen's
   * mutation scope, whose currency is asked on the fly, so an answer arriving after a change of session or address and
   * before the scope's owner resets it is dropped.
   */
  capture(): IsCurrent {
    const generation = untracked(this.generation);
    const isCurrent = this.lifetime.capture();
    const isScopeCurrent = this.mutations?.capture();
    return () => isCurrent() && this.generation() === generation && (isScopeCurrent?.() ?? true);
  }

  beginMutation(): ScreenOperation | undefined {
    const operation = this.mutations?.begin();
    if (operation) this.operations.add(operation);
    return operation;
  }

  finishMutation(operation: ScreenOperation | undefined): void {
    if (operation) {
      this.operations.delete(operation);
      this.mutations?.finish(operation);
    }
  }

  deselectFailedRows(): void {
    if (this.lifetime.isCurrent() && !this.deletion.isBusy()) {
      this.selection.remove(this.deletion.errorIds());
    }
  }

  verifyDeletion(): void {
    const result = this.deletion.result();
    if (!result || !this.deletion.needsVerification() || this.deletion.isBusy() || this.isLoading() || !this.lifetime.isCurrent()) {
      return;
    }
    this.active.keepKey();
    const request = this.requestPage();
    if (request) {
      this.verification = { request, result };
    }
  }

  /** A change of the query and, unless only the order changed, a new count, then back to the first page and a new page. */
  private changeQuery(change: () => void, recount = true): void {
    change();
    this.selection.clear();
    this.changes.clear();
    this.active.leave();
    this._page.set(1);
    this.requestPage();
    if (recount) {
      this.count.requestCount();
    }
  }

  /** Asks the server for the page the state names, for the data alone once a page is held. */
  private requestPage(): PageRequest | undefined {
    this.verification = undefined;
    const service = this.service();
    if (!service) {
      return undefined;
    }
    const request: PageRequest = { service, body: untracked(this.request), isCurrent: this.capture() };
    this.pageRequest = request;
    this.send(request);
    return request;
  }

  /** Posts a page request; its answer is adopted only while it is the last one asked and its grid is current. */
  private send(request: PageRequest): void {
    this.inFlight?.unsubscribe();
    this._isLoading.set(true);
    this._error.set(undefined);
    const isLast = () => request === this.pageRequest && request.isCurrent();
    this.inFlight = this.api
      .postTo<GridServiceEndpoints, 'get-data'>(request.service, 'get-data', request.body, shownByCaller())
      .subscribe({
        next: screen => {
          if (!isLast()) {
            return;
          }
          this._isLoading.set(false);
          if (isPage(screen?.data)) {
            this.adopt(screen.data, request.body.page);
          }
          if (this.verification?.request === request) {
            this.deletion.verified(this.verification.result);
            this.verification = undefined;
          }
        },
        error: (error: unknown) => {
          if (!isLast()) {
            return;
          }
          // The rows and the page on screen stay the last answer's; the failed request waits for a retry
          this._isLoading.set(false);
          this._error.set(error ?? new Error('The page could not be loaded'));
          this._page.set(this.data()?.page ?? 1);
        },
      });
  }

  /** An answer: the rows on screen, its page and page size, and its total when it carries one. */
  private adopt(answer: GridPageDto<GridRow>, askedPage: number | null | undefined): void {
    if (answer.page !== askedPage) {
      this.selection.clear();
    } else {
      this.selection.retain(answer.rows);
    }
    this._data.set(answer);
    this.changes.settle(answer.rows, untracked(this.tableColumns));
    this._page.set(answer.page);
    if (typeof answer.total === 'number') {
      this.count.adoptTotal(answer.total);
    }
  }

  /**
   * A cell's committed answer: the record's cells are to be marked against the rows on screen on the page the reload
   * loads, and its row is replaced by the answered one, when there is one.
   */
  private adoptRow(id: GridRowKey, row: GridRow | null): void {
    const page = untracked(this.data);
    this.changes.expect(id, page?.rows ?? []);
    if (page && row) {
      this._data.set({ ...page, rows: page.rows.map(held => (keyOf(held) === id ? row : held)) });
    }
  }

  /** Listens to the mutations of the screen for the current model; an invalidation of the scope starts the state over. */
  private subscribeMutations(): void {
    this.unsubscribeMutations = this.mutations?.subscribe({
      accepts: event => {
        const model = untracked(this.model);
        return (
          !!model && (event.operation === 'save' ? untracked(() => this.forms.follows(event.service)) : event.service === model.service)
        );
      },
      refresh: event => {
        // A save of a form this grid opened, which is open while it saves, is to be marked on the page the reload loads
        if (event?.operation === 'save' && event.recordId != null && this.forms.isTracking()) {
          this.changes.expect(event.recordId, untracked(this.data)?.rows ?? []);
        }
        this.reload();
      },
      invalidate: () => {
        this.opening.set(null);
        this.invalidations.update(n => n + 1);
        this.subscribeMutations();
      },
    });
  }

  private release(): void {
    this.inFlight?.unsubscribe();
    this.verification = undefined;
    this.unsubscribeMutations?.();
    this.unsubscribeMutations = undefined;
    this.forms.stop();
    for (const operation of this.operations) this.mutations?.finish(operation);
    this.operations.clear();
  }
}

/** One page asked of the server: the service, the request, and whether its grid is still current. */
interface PageRequest {
  readonly service: string;
  readonly body: GridRequest;
  readonly isCurrent: IsCurrent;
}

/** A page by the contract: an object with its rows. */
export function isPage(data: unknown): data is GridPageDto<GridRow> {
  return !!data && typeof data === 'object' && Array.isArray((data as GridPageDto<GridRow>).rows);
}
