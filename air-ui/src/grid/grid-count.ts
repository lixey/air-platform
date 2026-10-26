import { httpResource } from '@angular/common/http';
import { computed, effect, linkedSignal, type Signal, untracked } from '@angular/core';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { type AnswerOf, serviceUrl } from '../core/api';
import type { GridRequest, GridServiceEndpoints } from '../core/generated/contracts';
import type { IsCurrent } from '../core/lifetime';

/** An initial count request started in parallel with `get-data` by `screenLoader`. */
export interface PrefetchedGridCount {
  readonly total: Signal<number | undefined>;
  readonly isLoading: Signal<boolean>;
  readonly isError: Signal<boolean>;
  readonly isCurrent: IsCurrent;
}

const prefetchedCounts = new Map<string, PrefetchedGridCount>();

/** Registers an in-flight or completed initial `get-count` started alongside `get-data`. */
export function registerPrefetchedGridCount(service: string, prefetched: PrefetchedGridCount): void {
  for (const [k, v] of prefetchedCounts) {
    if (!untracked(v.isCurrent)) {
      prefetchedCounts.delete(k);
    }
  }
  prefetchedCounts.set(service.toLowerCase(), prefetched);
}

/** Consumes the prefetched `get-count` for `service` if one is current. */
export function consumePrefetchedGridCount(service: string | null | undefined): PrefetchedGridCount | null {
  if (!service) {
    return null;
  }
  const key = service.toLowerCase();
  const entry = prefetchedCounts.get(key) ?? null;
  if (entry) {
    prefetchedCounts.delete(key);
  }
  return entry && untracked(entry.isCurrent) ? entry : null;
}

/** One request of the counter: whether the grid it was asked for is still current, and an optional prefetched count. */
interface CountRequest {
  readonly isCurrent: IsCurrent;
  readonly prefetched?: PrefetchedGridCount | null;
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
 * rows. A requested count forgets the total it held; its answer is adopted only while the request that asked it is the
 * last one and its capture is current. A page that carries an exact total is adopted without a request. When `resetBy`
 * changes the counter starts over from `totalAtStart`, adopting a prefetched parallel count when available or asking at
 * once when the opening page leaves the total open.
 *
 * Created in a field of the grid service, so its request and its effect live in the service's injection context.
 * It knows nothing of the service: it is given its options.
 */
export class GridCount {
  /** The last count asked, or none. */
  private readonly request = linkedSignal<unknown, CountRequest | null>({
    source: () => this.options.resetBy(),
    computation: () =>
      untracked(() => {
        if (this.options.totalAtStart() !== null) {
          return null;
        }
        const service = this.options.service();
        if (!service || !this.options.isCounted()) {
          return null;
        }
        return {
          isCurrent: this.options.capture(),
          prefetched: consumePrefetchedGridCount(service),
        };
      }),
  });

  private readonly countLoader = httpResource<AnswerOf<GridServiceEndpoints, 'get-count'>>(() => {
    const request = this.request();
    const service = this.options.service();
    if (!request || request.prefetched || !service || !this.options.isCounted() || !request.isCurrent()) {
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
    source: () => {
      const request = this.request();
      const total = request?.prefetched
        ? request.prefetched.total()
        : this.countLoader.hasValue()
          ? this.countLoader.value()?.total
          : undefined;
      return {
        reset: this.options.resetBy(),
        request,
        total,
      };
    },
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

  readonly isCountLoading = computed(() => this.request()?.prefetched?.isLoading() ?? this.countLoader.isLoading());
  readonly isCountPending = computed(() => this.isCountLoading() && this.countedTotal() === null);
  /** Whether the last count failed: the total is unknown until it is asked again. */
  readonly isCountFailed = computed(
    () =>
      (this.request()?.prefetched ? this.request()!.prefetched!.isError() : this.countLoader.status() === 'error') &&
      this.countedTotal() === null,
  );

  private hadCountRequest = false;
  private readonly cancelObsoleteCount = effect(() => {
    const request = this.request();
    const target = !!request && !request.prefetched && !!this.options.service() && this.options.isCounted() && request.isCurrent();
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
