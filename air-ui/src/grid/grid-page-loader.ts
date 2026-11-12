import { computed, effect, linkedSignal, type Signal, untracked } from '@angular/core';
import type { Subscription } from 'rxjs';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import type { Api } from '../core/api';
import type { GridColumnDto, GridModel, GridPageDto, GridRequest, GridServiceEndpoints } from '../core/generated/contracts';
import type { IsCurrent } from '../core/lifetime';
import type { SelectService } from '../select/select.service';
import { emptySelectRowOf } from './column/grid-column-select-source';
import type { GridDeletion } from './grid-deletion';
import type { GridGeneration, GridHeld } from './grid-query-state';
import { type GridRow, type GridRowKey, keyOf } from './grid-row';

/** The rows of a picker's list while its text is shorter than the least length: one list, so nothing is drawn anew. */
export const NO_ROWS: readonly GridRow[] = [];

/** Projects visible grid rows, inserting an empty picker option or filtering already-picked multi-select options when needed. */
export function projectGridRows(
  shortOf: number | null,
  baseRows: readonly GridRow[],
  select: SelectService | null,
  page: number,
  search: string,
  allColumns: readonly GridColumnDto[],
): readonly GridRow[] {
  if (shortOf !== null) {
    return NO_ROWS;
  }
  const rows =
    select?.model().allowEmpty && page === 1 && !search && !baseRows.some(r => keyOf(r) === null)
      ? [emptySelectRowOf(select.model().labelPaths, allColumns), ...baseRows]
      : baseRows;
  if (!select || !select.isMultiple()) {
    return rows;
  }
  const picked = new Set(select.draft().map(o => o.id));
  return rows.filter(row => !picked.has(keyOf(row)));
}

export interface GridSelectHost {
  readonly setSearch: (text: string) => void;
  readonly moveActive: (delta: number) => void;
  readonly setActive: (index: number) => void;
  readonly activeRow: () => GridRow | null;
  readonly rows: () => readonly GridRow[];
  readonly isLoading: () => boolean;
  readonly tableIds: () => {
    readonly table: string;
    readonly activeCell: () => string | null;
    readonly tableWidth?: () => number;
  } | null;
}

/** Wires a `GridService` as the backing table link of an enclosing `SelectService`, if present. */
export function attachGridSelect(select: SelectService | null, host: GridSelectHost): void {
  select?.attachGrid({
    setSearch: text => host.setSearch(text),
    moveActive: delta => host.moveActive(delta),
    setActive: index => host.setActive(index),
    activeRow: () => host.activeRow(),
    rowCount: () => host.rows().length,
    isLoading: () => host.isLoading(),
    tableId: () => host.tableIds()?.table ?? null,
    activeCellId: () => host.tableIds()?.activeCell() ?? null,
    tableWidth: () => host.tableIds()?.tableWidth?.() ?? 0,
  });
}

/** One page asked of the server: the service, the request, and whether its grid is still current. */
export interface PageRequest {
  readonly service: string;
  readonly body: GridRequest;
  readonly isCurrent: IsCurrent;
}

/** A page by the contract: an object with its rows. */
export function isPage(data: unknown): data is GridPageDto<GridRow> {
  return !!data && typeof data === 'object' && Array.isArray((data as GridPageDto<GridRow>).rows);
}

type DeletionVerification = NonNullable<ReturnType<GridDeletion['result']>>;

export interface GridPageLoaderOptions {
  readonly api: Api;
  readonly model: Signal<GridModel | null>;
  readonly generation: Signal<GridGeneration>;
  readonly held: Signal<GridHeld>;
  readonly onAdopt: (answer: GridPageDto<GridRow>, askedPage: number | null | undefined) => void;
  readonly onVerified: (result: DeletionVerification) => void;
  readonly onFailed: (fallbackPage: number) => void;
}

/**
 * Owns the active page data, loading/error signals, in-flight HTTP subscription, retryable `PageRequest`, and
 * post-delete verification tracking for `GridService`. Constructed in the service's injection context.
 */
export class GridPageLoader {
  private readonly api: Api;
  private readonly onAdopt: GridPageLoaderOptions['onAdopt'];
  private readonly onVerified: GridPageLoaderOptions['onVerified'];
  private readonly onFailed: GridPageLoaderOptions['onFailed'];

  private readonly _data: ReturnType<typeof linkedSignal<GridHeld, GridPageDto<GridRow> | null>>;
  private readonly _isLoading: ReturnType<typeof linkedSignal<GridGeneration, boolean>>;
  private readonly _error: ReturnType<typeof linkedSignal<unknown, unknown>>;

  readonly data: Signal<GridPageDto<GridRow> | null>;
  readonly hasPage: Signal<boolean>;
  readonly pageSize: Signal<number>;
  readonly isLoading: Signal<boolean>;
  readonly error: Signal<unknown>;

  private pageRequest: PageRequest | undefined;
  private inFlight: Subscription | undefined;
  private verification: { request: PageRequest; result: DeletionVerification } | undefined;

  constructor(options: GridPageLoaderOptions) {
    const { api, model, generation, held, onAdopt, onVerified, onFailed } = options;
    this.api = api;
    this.onAdopt = onAdopt;
    this.onVerified = onVerified;
    this.onFailed = onFailed;

    this._data = linkedSignal({ source: held, computation: current => current.opening });
    this.data = this._data.asReadonly();
    this.hasPage = computed(() => this.data() !== null);
    this.pageSize = computed(() => this.data()?.pageSize ?? model()?.pageSize ?? 200);

    this._isLoading = linkedSignal({ source: generation, computation: () => false });
    this.isLoading = this._isLoading.asReadonly();

    this._error = linkedSignal<unknown, unknown>({ source: generation, computation: () => undefined });
    this.error = this._error.asReadonly();

    effect(() => {
      generation();
      untracked(() => {
        if (this.pageRequest && !this.pageRequest.isCurrent()) {
          this.cancelPage();
        }
      });
    });
  }

  requestPage(service: string | null, body: GridRequest, isCurrent: IsCurrent): PageRequest | undefined {
    this.verification = undefined;
    if (!service) {
      return undefined;
    }
    const request: PageRequest = { service, body, isCurrent };
    this.pageRequest = request;
    this.send(request);
    return request;
  }

  retry(): void {
    const request = this.pageRequest;
    if (request && this.error() !== undefined && request.isCurrent()) {
      this.send(request);
    }
  }

  trackVerification(request: PageRequest, result: DeletionVerification): void {
    this.verification = { request, result };
  }

  setPageData(answer: GridPageDto<GridRow>): void {
    const prevCounts = untracked(this.data)?.namedFilterCounts;
    this._data.set(
      answer.page > 1 && answer.namedFilterCounts === undefined && prevCounts !== undefined
        ? { ...answer, namedFilterCounts: prevCounts }
        : answer,
    );
  }

  replaceRow(id: GridRowKey, row: GridRow | null): readonly GridRow[] {
    const page = untracked(this.data);
    const previousRows = page?.rows ?? [];
    if (page && row) {
      this._data.set({ ...page, rows: page.rows.map(held => (keyOf(held) === id ? row : held)) });
    }
    return previousRows;
  }

  cancelPage(): void {
    this.inFlight?.unsubscribe();
    this.pageRequest = undefined;
    this.verification = undefined;
    this._isLoading.set(false);
  }

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
            this.onAdopt(screen.data, request.body.page);
          }
          if (this.verification?.request === request) {
            this.onVerified(this.verification.result);
            this.verification = undefined;
          }
        },
        error: (error: unknown) => {
          if (!isLast()) {
            return;
          }
          this._isLoading.set(false);
          this._error.set(error ?? new Error('The page could not be loaded'));
          this.onFailed(this.data()?.page ?? 1);
        },
      });
  }
}
