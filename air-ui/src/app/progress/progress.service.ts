import { computed, Service, signal } from '@angular/core';

/**
 * The requests in flight, which the bar of `ui-progress` shows: loading while one runs, completing when the last one
 * ends. The bar's creep and completion are the stylesheet's (`.ui-progress-bar`), so a request that starts while the
 * bar completes shows loading again at once.
 */
@Service()
export class ProgressService {
  private readonly activeRequests = signal(0);

  /** Whether a request is in flight. */
  readonly isLoading = computed(() => this.activeRequests() > 0);

  /** Counts a request that starts; every call is matched by one `endRequest`. */
  startRequest(): void {
    this.activeRequests.update(count => count + 1);
  }

  /** Counts a request that ended, successfully or not; the count never falls below zero. */
  endRequest(): void {
    this.activeRequests.update(count => Math.max(0, count - 1));
  }
}
