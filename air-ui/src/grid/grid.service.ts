import { computed, DestroyRef, effect, inject, linkedSignal, Service, type Signal, signal, untracked } from '@angular/core';
import { AuthService } from '../app/auth/auth.service';
import { Api } from '../core/api';
import type { GridColumnDto, GridModel, GridPageDto, GridRequest, OverlayDto, SortDirection } from '../core/generated/contracts';
import { type IsCurrent, Lifetime } from '../core/lifetime';
import type { OverlayRef } from '../overlay/overlay-ref';
import { parentParams, SCREEN_ADDRESS } from '../screen/screen-address';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { ScreenMutations, type ScreenOperation } from '../screen/screen-mutations';
import type { SelectOption } from '../select/select-option';
import type { SelectField } from '../select/select-source';
import { SelectService } from '../select/select.service';
import { GridColumnState } from './column/grid-column-state';
import { SELECTION_COLUMN_DTO } from './column/grid-selection-column';
import type { GridFilter } from './filter/grid-filter-set';
import { GridActiveRow } from './grid-active-row';
import { GridCellEdits } from './grid-cell-edits';
import { GridChangedCells } from './grid-changed-cells';
import { GridCount } from './grid-count';
import { GridDeletion } from './grid-deletion';
import { GridForms } from './grid-forms';
import { GridLiveRegistry, GridMutationSync } from './grid-mutation-sync';
import { attachGridSelect, GridPageLoader, isPage, NO_ROWS, type PageRequest, projectGridRows } from './grid-page-loader';
import { GridQueryState } from './grid-query-state';
import { buildGridCountRequest, buildServiceGridRequest } from './grid-request';
import { type GridRow, type GridRowKey, keyOf } from './grid-row';
import { GridSelection } from './grid-selection';

export { isPage };

/** What the table that draws a grid names for a search box that drives it. */
export interface GridTableIds {
  readonly table: string;
  readonly activeCell: Signal<string | null>;
  readonly tableWidth?: Signal<number>;
}

/**
 * Everything one grid holds on the client: the model the server described, the query state (`GridQueryState`), the
 * selection and the active row, the row counter (`GridCount`), inline cell edits (`GridCellEdits`), and the page of rows
 * (`GridPageLoader`). Provided by the grid component so nav elements, cells, and overlays find it by injection.
 */
@Service({ autoProvided: false })
export class GridService {
  private static readonly registry = new GridLiveRegistry();

  /** Registers a listener invoked whenever `reloadServices` runs (e.g. live reports). */
  static onReloadServices(listener: () => void): () => void {
    return GridService.registry.onReloadServices(listener);
  }

  /** Reloads every active grid on screen, or those matching the given service names. */
  static reloadServices(services?: readonly string[], options?: { readonly notifyListeners?: boolean }): void {
    GridService.registry.reloadServices(services, options);
  }

  private readonly address = inject(SCREEN_ADDRESS, { optional: true });
  private readonly api = inject(Api);
  private readonly auth = inject(AuthService);
  private readonly mutations = inject(ScreenMutations, { optional: true });
  private readonly select = inject(SelectService, { optional: true });
  private readonly lifetime = (inject(SCREEN_LIFETIME, { optional: true }) ?? new Lifetime()).child();

  private readonly bound = signal<{ readonly model: Signal<GridModel | null | undefined>; readonly data: Signal<unknown> } | null>(null);
  readonly model = computed<GridModel | null>(() => this.bound()?.model() ?? null);
  private readonly invalidations = signal(0);
  private readonly generation = computed(() => ({ model: this.model(), invalidations: this.invalidations() }));
  private readonly opening = linkedSignal<unknown, GridPageDto<GridRow> | null>({
    source: () => this.bound()?.data(),
    computation: data => (isPage(data) ? data : null),
  });
  private readonly held = computed(() => ({ generation: this.generation(), opening: this.opening() }));

  readonly selection = new GridSelection(this.held);
  readonly deletion = new GridDeletion(this.generation);
  private readonly changes = new GridChangedCells(this.generation);
  readonly columnState = new GridColumnState(() => this.model()?.columns ?? [], this.generation);
  readonly allColumns = this.columnState.columns;
  readonly service = computed(() => this.model()?.service ?? null);
  readonly isSelectable = computed(() => this.model()?.isSelectable !== false && !this.select);
  readonly isPicking = computed(() => this.select !== null);
  readonly isPickerList = computed(() => this.select?.model().isPicker === true);
  readonly pickerField = computed<SelectField | null>(() => (this.isPickerList() ? (this.select?.model().field ?? null) : null));
  readonly tableIds = signal<GridTableIds | null>(null);
  readonly shortOf = computed<number | null>(() => (this.isPickerList() ? (this.select?.shortOf() ?? null) : null));
  readonly tableColumns = computed<readonly GridColumnDto[]>(() => {
    const visible = this.allColumns().filter(column => !column.isHidden);
    return this.isSelectable() ? [SELECTION_COLUMN_DTO, ...visible] : visible;
  });

  private readonly forms = new GridForms({
    model: this.model,
    service: this.service,
    capture: () => this.capture(),
    reload: () => this.reload(),
  });

  private readonly query = new GridQueryState({
    model: this.model,
    generation: this.generation,
    held: this.held,
    user: () => this.auth.currentUser(),
    select: this.select,
  });

  readonly page = this.query.page;
  readonly sort = this.query.sort;
  readonly search = this.query.search;
  readonly filterPicks = this.query.filterPicks;
  readonly filterTree = this.query.filterTree;
  readonly filters = this.query.filters;
  readonly columnFilters = this.query.columnFilters;
  readonly namedFilters = this.query.namedFilters;

  private readonly loader = new GridPageLoader({
    api: this.api,
    model: this.model,
    generation: this.generation,
    held: this.held,
    onAdopt: (answer, askedPage) => this.adopt(answer, askedPage),
    onVerified: result => this.deletion.verified(result),
    onFailed: fallbackPage => this.query.setPage(fallbackPage),
  });

  readonly data = this.loader.data;
  readonly pageSize = this.loader.pageSize;
  readonly isLoading = this.loader.isLoading;
  readonly error = this.loader.error;

  private readonly mutationSync = new GridMutationSync({
    auth: this.auth,
    mutations: this.mutations,
    model: this.model,
    followsForm: service => this.forms.follows(service),
    isTrackingForm: () => this.forms.isTracking(),
    expectChange: recordId => this.changes.expect(recordId, untracked(this.data)?.rows ?? []),
    reload: () => this.reload(),
    onIdentityChanged: () => {
      this.opening.set(null);
      this.invalidations.update(n => n + 1);
    },
    cancelPage: () => this.loader.cancelPage(),
  });

  readonly request = computed<GridRequest>(() =>
    buildServiceGridRequest(
      this.address?.(),
      this.service(),
      this.select?.model(),
      this.query.snapshot(this.pageSize(), this.loader.hasPage()),
    ),
  );

  private readonly count = new GridCount({
    capture: () => this.capture(),
    service: () => this.service(),
    body: service =>
      buildGridCountRequest(this.address?.(), service, this.filters(), this.columnFilters(), this.search(), this.namedFilters()),
    isCounted: () => !this.isPicking(),
    resetBy: this.held,
    totalAtStart: () => this.opening()?.total ?? null,
  });

  readonly rows = computed<readonly GridRow[]>(() =>
    projectGridRows(this.shortOf(), this.data()?.rows ?? [], this.select, this.page(), this.search(), this.allColumns()),
  );
  readonly topRows = computed<readonly GridRow[]>(() => (this.shortOf() !== null ? NO_ROWS : (this.data()?.topRows ?? [])));
  readonly bottomRows = computed<readonly GridRow[]>(() => (this.shortOf() !== null ? NO_ROWS : (this.data()?.bottomRows ?? [])));
  readonly hasMore = computed(() => this.data()?.hasMore ?? false);
  readonly total = computed(() => this.count.countedTotal() ?? this.data()?.total ?? 0);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / Math.max(1, this.pageSize()))));
  readonly isCountLoading = this.count.isCountLoading;
  readonly isCountPending = this.count.isCountPending;
  readonly isCountFailed = this.count.isCountFailed;

  private readonly rereadApart = effect(() => {
    if (this.held().opening && this.query.startsApart()) {
      untracked(() => this.changeQuery(() => undefined));
    }
  });

  private readonly active = new GridActiveRow(
    () => this.rows(),
    this.generation,
    () => this.select?.model().isPicker === true,
  );
  readonly activeRowIndex = this.active.index;
  readonly activeRow = computed<GridRow | null>(() => this.rows()[this.activeRowIndex()] ?? null);

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
    GridService.registry.add(this);
    inject(DestroyRef).onDestroy(() => {
      GridService.registry.delete(this);
      this.release();
      this.lifetime.end();
    });
    attachGridSelect(this.select, this);
  }

  bind(model: Signal<GridModel | null | undefined>, data: Signal<unknown>): void {
    this.bound.set({ model, data });
  }

  reload(): void {
    if (!this.lifetime.isCurrent()) {
      return;
    }
    this.active.keepKey();
    this.requestPage();
    if (this.hasMore() || this.page() > 1) {
      this.count.requestCount();
    }
  }

  retry(): void {
    this.loader.retry();
  }

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
    this.query.setPage(next);
    this.requestPage();
  }

  setSort(field: string, direction: SortDirection | null, multi = false): void {
    this.changeQuery(() => this.query.applySort(field, direction, multi), false);
  }

  sortOf(field: string | null | undefined): SortDirection | null {
    return this.query.sortOf(field);
  }

  sortPositionOf(field: string | null | undefined): number | null {
    return this.query.sortPositionOf(field);
  }

  setSearch(text: string): void {
    const next = text.trim();
    if (next === this.search()) {
      return;
    }
    this.changeQuery(() => this.query.setSearchValue(next));
  }

  setFilters(filters: readonly GridFilter[]): void {
    this.changeQuery(() => this.query.applyFilters(filters));
  }

  setColumnFilter(filter: GridFilter): void {
    this.changeQuery(() => this.query.applyColumnFilter(filter));
  }

  clearColumnFilter(field: string): void {
    this.changeQuery(() => this.query.clearColumnFilter(field));
  }

  columnFilterOf(field: string | null | undefined): GridFilter | undefined {
    return this.query.columnFilterOf(field);
  }

  setReferenceFilter(field: string, options: readonly SelectOption[]): void {
    this.changeQuery(() => this.query.applyReferenceFilter(field, options));
  }

  referencePicksOf(field: string | null | undefined): readonly SelectOption[] {
    return this.query.referencePicksOf(field);
  }

  removeFilter(field: string): void {
    this.changeQuery(() => this.query.removeFilter(field));
  }

  removeFilterItem(index: number): void {
    if (!this.filterTree()) {
      return;
    }
    this.changeQuery(() => this.query.removeFilterItem(index));
  }

  clearFilters(): void {
    if (this.filters().length === 0) {
      return;
    }
    this.changeQuery(() => this.query.clearGeneralFilters());
  }

  isNamedFilterOn(key: string): boolean {
    return this.query.isNamedFilterOn(key);
  }

  namedFilterCountOf(key: string): number | null {
    const count = this.data()?.namedFilterCounts?.[key];
    return typeof count === 'number' ? count : null;
  }

  pressNamedFilter(key: string): void {
    if (this.query.pressNamedFilter(key)) {
      this.changeQuery(() => undefined);
    }
  }

  isChanged(row: GridRow | null | undefined, path: string | null | undefined): boolean {
    return this.changes.isChanged(row, path);
  }

  setActive(index: number): void {
    this.active.set(index);
  }

  moveActive(delta: number): void {
    this.active.move(delta);
  }

  pickActive(): boolean {
    return this.select?.pickActive() ?? false;
  }

  openForm(id: GridRowKey | null, anchor?: HTMLElement | null, formService?: string | null, customOverlay?: OverlayDto | null): boolean {
    if (this.select) {
      const row = this.rows().find(r => keyOf(r) === id) ?? null;
      if (row) this.select.pickRow(row);
      return row !== null;
    }
    return this.forms.openForRow(id, this.rows(), this.data()?.forms, anchor, formService, customOverlay);
  }

  withParentParams(model: OverlayDto): OverlayDto {
    return this.forms.withParentParams(model);
  }

  trackForm(ref: OverlayRef): void {
    this.forms.track(ref);
  }

  capture(): IsCurrent {
    const generation = untracked(this.generation);
    const isCurrent = this.lifetime.capture();
    const isScopeCurrent = this.mutations?.capture();
    return () => isCurrent() && this.generation() === generation && (isScopeCurrent?.() ?? true);
  }

  beginMutation(): ScreenOperation | undefined {
    return this.mutationSync.begin();
  }

  finishMutation(operation: ScreenOperation | undefined): void {
    this.mutationSync.finish(operation);
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
      this.loader.trackVerification(request, result);
    }
  }

  private changeQuery(change: () => void, recount = true): void {
    change();
    this.selection.clear();
    this.changes.clear();
    this.active.leave();
    this.query.setPage(1);
    if (recount) {
      this.count.clear();
    }
    this.requestPage();
  }

  private requestPage(): PageRequest | undefined {
    return this.loader.requestPage(this.service(), untracked(this.request), this.capture());
  }

  private adopt(answer: GridPageDto<GridRow>, askedPage: number | null | undefined): void {
    if (answer.page !== askedPage) {
      this.selection.clear();
    } else {
      this.selection.retain(answer.rows);
    }
    this.loader.setPageData(answer);
    this.changes.settle(answer.rows, untracked(this.tableColumns));
    this.query.setPage(answer.page);
    if (typeof answer.total === 'number') {
      this.count.adoptTotal(answer.total);
    } else if (this.count.countedTotal() === null && !this.count.isCountLoading()) {
      this.count.requestCount();
    }
  }

  private adoptRow(id: GridRowKey, row: GridRow | null): void {
    const previousRows = this.loader.replaceRow(id, row);
    this.changes.expect(id, previousRows);
  }

  private release(): void {
    this.loader.cancelPage();
    this.mutationSync.release();
    this.forms.stop();
  }
}
