import { computed, signal, type Signal } from '@angular/core';

/** The predicate a capture hands back: whether what was captured is still current. */
export type IsCurrent = () => boolean;

/**
 * Says whether asynchronous work still belongs to what started it: an epoch that `invalidate` advances, an end that
 * is for good, and an outer predicate. A `child` is current only while its parent is and ends with it; what happens
 * to a child touches neither its parent nor its siblings.
 */
export class Lifetime {
  private readonly _epoch = signal(0);
  /** The epoch as a signal, a parent's included: an effect that must follow every invalidation reads it. */
  readonly epoch: Signal<number>;
  private hasEnded = false;

  constructor(
    private readonly isOuterCurrent: IsCurrent = () => true,
    private readonly parent?: Lifetime,
  ) {
    this.epoch = parent ? computed(() => this._epoch() + parent.epoch()) : this._epoch.asReadonly();
  }

  get isEnded(): boolean {
    return this.hasEnded || (this.parent?.isEnded ?? false);
  }

  /**
   * A lifetime inside this one: current only while this one is and its own predicate holds, ended when this one ends,
   * and free to end or invalidate alone.
   */
  child(isOuterCurrent: IsCurrent = () => true): Lifetime {
    return new Lifetime(isOuterCurrent, this);
  }

  /** Whether what is captured now is still current: this epoch, not ended, the outer and the parent still current. */
  capture(): IsCurrent {
    const epoch = this._epoch();
    const isOuterCurrent = this.isOuterCurrent;
    const parent = this.parent?.capture();
    return () => !this.hasEnded && this._epoch() === epoch && isOuterCurrent() && (parent?.() ?? true);
  }

  /** Whether it is current now, without a snapshot: not ended, the outer and the parent still current. */
  isCurrent(): boolean {
    return !this.hasEnded && this.isOuterCurrent() && (this.parent?.isCurrent() ?? true);
  }

  /** Starts a new epoch: every capture taken before answers false. */
  invalidate(): void {
    if (this.isEnded) {
      return;
    }
    this._epoch.update(v => v + 1);
  }

  /** Ends it for good: every capture answers false and no new epoch starts. */
  end(): void {
    this.hasEnded = true;
  }
}
