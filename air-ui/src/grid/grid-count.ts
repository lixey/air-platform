import { httpResource } from '@angular/common/http';
import { computed, effect, linkedSignal, untracked } from '@angular/core';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { type AnswerOf, serviceUrl } from '../core/api';
import type { GridRequest, GridServiceEndpoints } from '../core/generated/contracts';
import type { IsCurrent } from '../core/lifetime';

/** One request of the counter: whether the grid it was asked for is still current. */
interface CountRequest {
  readonly isCurrent: IsCurrent;
}

/** What the row counter is given of its grid. */
export interface GridCountOptions {
  /** Captures the grid's lifetime for a request, so an answer for a grid that moved on is dropped. */
  readonly capture: () => IsCurrent;
  /** The grid's service, or null while there is none to ask. */
  readonly service: () => string | null;
  /** The body of a count request to the service. */
  readonly body: (service: string) => GridRequest;
  /** Whether the grid counts at all: a select picking does not. */
  readonly isCounted: () => boolean;
  /** What starts the counter over when it changes. */
  readonly resetBy: () => unknown;
  /** The total the opening page carries, or null when it leaves the total open and a count is asked at once. */
  readonly totalAtStart: () => number | null;
}

/**
 * The row counter of one grid: how many rows match its filters, named filters and search, asked apart from the page of
 * rows, and beside the total the badges of the named filters that declare one. A requested count forgets the total it
 * held and keeps the badges; its answer is adopted only while the request that asked it is the last
 * one and its capture is current. A page that carries an exact total is adopted without a request. When `resetBy`
 * changes the counter starts over from `totalAtStart`, asking at once when the opening page leaves the total open.
 *
 * Created in a field of the grid service, so its request and its effect live in the service's injection context.
 * It knows nothing of the service: it is given its options.
 */
export class GridCount {
  /** The last count asked, or none. */
  private readonly request = linkedSignal<unknown, CountRequest | null>({
    source: () => this.options.resetBy(),
    computation: () => (untracked(this.options.totalAtStart) === null ? { isCurrent: untracked(this.options.capture) } : null),
  });

  private readonly countLoader = httpResource<AnswerOf<GridServiceEndpoints, 'get-count'>>(() => {
    const request = this.request();
    const service = this.options.service();
    if (!request || !service || !this.options.isCounted() || !request.isCurrent()) {
      return undefined;
    }
    return {
      url: serviceUrl<GridServiceEndpoints>(service, 'get-count'),
      method: 'POST',
      body: this.options.body(service),
      ...shownByCaller(),
    };
  });

  /** The total counted: the answer to the last request while it is current, or the total a page carried. */
  private readonly _countedTotal = linkedSignal<
    { readonly reset: unknown; readonly request: CountRequest | null; readonly total: number | undefined },
    number | null
  >({
    source: () => ({
      reset: this.options.resetBy(),
      request: this.request(),
      total: this.countLoader.hasValue() ? this.countLoader.value()?.total : undefined,
    }),
    computation: (source, previous) => {
      if (typeof source.total === 'number' && source.request?.isCurrent()) {
        return source.total;
      }
      if (previous && previous.source.reset === source.reset) {
        return previous.source.request === source.request ? previous.value : null;
      }
      // A start, or a start over: the opening total, unless a count is asked already, read or not before it was
      return source.request ? null : untracked(this.options.totalAtStart);
    },
  });
  readonly countedTotal = this._countedTotal.asReadonly();

  private readonly _badges = linkedSignal<
    { readonly reset: unknown; readonly answer: Readonly<Record<string, number>> | null },
    Readonly<Record<string, number>> | null
  >({
    source: () => ({
      reset: this.options.resetBy(),
      answer: (this.countLoader.hasValue() ? this.countLoader.value()?.namedFilterBadges : null) ?? null,
    }),
    // The answer of the count held now; while one is asked or failed, what the last one answered since the start
    computation: (source, previous) => source.answer ?? (previous && previous.source.reset === source.reset ? previous.value : null),
  });
  /** The badges the last count answered, kept while a count is asked; null before the first and for a grid with none. */
  readonly badges = this._badges.asReadonly();

  readonly isCountLoading = this.countLoader.isLoading;
  readonly isCountPending = computed(() => this.countLoader.isLoading() && this.countedTotal() === null);
  /** Whether the last count failed: the total is unknown until it is asked again. */
  readonly isCountFailed = computed(() => this.countLoader.status() === 'error' && this.countedTotal() === null);

  private hadCountRequest = false;
  private readonly cancelObsoleteCount = effect(() => {
    const request = this.request();
    const target = !!request && !!this.options.service() && this.options.isCounted() && request.isCurrent();
    if (!target && this.hadCountRequest) {
      this.countLoader.set(undefined);
    }
    this.hadCountRequest = target;
  });

  constructor(private readonly options: GridCountOptions) {}

  /** Asks the count again: the total held is forgotten until the answer to this request arrives. */
  requestCount(): void {
    this.request.set({ isCurrent: this.options.capture() });
  }

  /** A total that came with a page, adopted as the count. */
  adoptTotal(total: number): void {
    this._countedTotal.set(total);
  }
}
