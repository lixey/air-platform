import { computed, DestroyRef, inject, Service, signal } from '@angular/core';

export type ProgressPhase = 'idle' | 'loading' | 'done';

/**
 * Tracks in-flight HTTP requests and drives the smart top progress bar (`ui-progress`).
 * Fast requests finishing within 100 ms stay in `'idle'` and never flash the bar; longer
 * requests enter `'loading'` (creeping via compositor CSS) and transition through `'done'`
 * when the last request ends.
 */
@Service()
export class ProgressService {
  private static readonly ShowDelayMs = 100;
  private static readonly DoneDurationMs = 350;

  private readonly activeRequests = signal(0);
  private readonly _phase = signal<ProgressPhase>('idle');

  private showTimer: ReturnType<typeof setTimeout> | null = null;
  private doneTimer: ReturnType<typeof setTimeout> | null = null;

  /** Whether a request is in flight. */
  readonly isLoading = computed(() => this.activeRequests() > 0);
  /** The visual phase of the progress bar (`'idle'`, `'loading'`, or `'done'`). */
  readonly phase = this._phase.asReadonly();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearTimers());
  }

  /** Counts a request that starts; every call is matched by one `endRequest`. */
  startRequest(): void {
    const next = this.activeRequests() + 1;
    this.activeRequests.set(next);
    if (next > 1) {
      return;
    }

    if (this.doneTimer !== null) {
      clearTimeout(this.doneTimer);
      this.doneTimer = null;
    }

    if (this._phase() === 'done') {
      this._phase.set('loading');
      return;
    }

    if (this.showTimer === null) {
      this.showTimer = setTimeout(() => {
        this.showTimer = null;
        if (this.activeRequests() > 0) {
          this._phase.set('loading');
        }
      }, ProgressService.ShowDelayMs);
    }
  }

  /** Counts a request that ended, successfully or not; the count never falls below zero. */
  endRequest(): void {
    const next = Math.max(0, this.activeRequests() - 1);
    this.activeRequests.set(next);
    if (next > 0) {
      return;
    }

    if (this.showTimer !== null) {
      clearTimeout(this.showTimer);
      this.showTimer = null;
      return;
    }

    if (this._phase() === 'loading') {
      this._phase.set('done');
      this.doneTimer = setTimeout(() => {
        this.doneTimer = null;
        if (this.activeRequests() === 0) {
          this._phase.set('idle');
        }
      }, ProgressService.DoneDurationMs);
    }
  }

  private clearTimers(): void {
    if (this.showTimer !== null) {
      clearTimeout(this.showTimer);
      this.showTimer = null;
    }
    if (this.doneTimer !== null) {
      clearTimeout(this.doneTimer);
      this.doneTimer = null;
    }
  }
}
