import { DestroyRef, inject, type Provider } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService, identityOf } from '../app/auth/auth.service';
import { type IsCurrent, Lifetime } from '../core/lifetime';
import type { OverlayRef } from '../overlay/overlay-ref';
import { followedLifetime } from './followed-lifetime';

export interface ScreenMutationEvent {
  readonly service: string;
  readonly operation: 'save' | 'delete';
  readonly recordId?: number | null;
  readonly selectedIds?: readonly (number | string)[];
}

/**
 * What a subscriber asks to hear: which mutations concern it, what a mutation that does makes it do, and what a reset
 * does. `refresh` is handed the event a publish carries; a tracked close hands it none.
 */
export interface ScreenMutationListener {
  readonly accepts: (event: ScreenMutationEvent) => boolean;
  readonly refresh: (event?: ScreenMutationEvent) => void;
  readonly invalidate: () => void;
}

export interface ScreenOperation {
  readonly id: symbol;
  readonly isCurrent: () => boolean;
}

/**
 * Ephemeral notifications owned by one screen, shared by its panels and overlays. The owner that provides the scope
 * resets it; a consumer only begins, publishes, subscribes and tracks.
 */
export class ScreenMutations {
  private readonly lifetime: Lifetime;
  private readonly listeners = new Set<{ listener: ScreenMutationListener; receipts: Set<symbol> }>();
  private readonly operations = new Set<symbol>();
  private readonly closes = new Map<OverlayRef, () => void>();

  /** `owner` is the lifetime of the scope's owner: the notifications are current only while it is. */
  constructor(owner?: Lifetime) {
    this.lifetime = owner ? owner.child() : new Lifetime();
  }

  begin(): ScreenOperation {
    const isCurrent = this.lifetime.capture();
    const id = Symbol('screen mutation');
    if (!this.lifetime.isEnded) {
      this.operations.add(id);
    }
    return { id, isCurrent: () => isCurrent() && this.operations.has(id) };
  }

  /**
   * Whether what is captured now is still current in this scope: false once its owner resets or destroys it, and at
   * once when the owner's lifetime stops holding, before the owner's reset runs (a root scope's identity or address).
   */
  capture(): IsCurrent {
    return this.lifetime.capture();
  }

  finish(operation: ScreenOperation): void {
    this.operations.delete(operation.id);
    for (const entry of this.listeners) {
      entry.receipts.delete(operation.id);
    }
  }

  subscribe(listener: ScreenMutationListener): () => void {
    if (this.lifetime.isEnded) {
      return () => undefined;
    }
    const entry = { listener, receipts: new Set<symbol>() };
    this.listeners.add(entry);
    return () => this.listeners.delete(entry);
  }

  publish(operation: ScreenOperation, event: ScreenMutationEvent): void {
    if (!operation.isCurrent()) {
      return;
    }
    for (const entry of this.listeners) {
      if (entry.listener.accepts(event) && !entry.receipts.has(operation.id)) {
        entry.receipts.add(operation.id);
        entry.listener.refresh(event);
      }
    }
  }

  track(ref: OverlayRef, service: string, isOwnerCurrent: () => boolean): () => void {
    if (this.lifetime.isEnded || ref.isClosed) {
      return () => undefined;
    }
    const existing = this.closes.get(ref);
    if (existing) {
      return existing;
    }
    const isCurrent = this.lifetime.capture();
    const subscription = ref.closed.subscribe(() => {
      if (isCurrent() && isOwnerCurrent()) {
        const cause = ref.closeCause;
        for (const entry of this.listeners) {
          if (entry.listener.accepts({ service, operation: 'save' })) {
            if (cause && entry.receipts.delete(cause)) {
              continue;
            }
            entry.listener.refresh();
          }
        }
      }
      cleanup();
    });
    const cleanup = () => {
      subscription.unsubscribe();
      this.closes.delete(ref);
    };
    this.closes.set(ref, cleanup);
    return cleanup;
  }

  /**
   * Starts the scope over for its owner: the operations, receipts and tracked closes of the old scope are dropped, their
   * late answers find it stale, and every listener is told to invalidate and subscribe again.
   */
  reset(): void {
    this.lifetime.invalidate();
    this.operations.clear();
    for (const cleanup of this.closes.values()) {
      cleanup();
    }
    const entries = [...this.listeners];
    this.listeners.clear();
    for (const entry of entries) {
      entry.receipts.clear();
      entry.listener.invalidate();
    }
  }

  /** Ends the scope for good: nothing begun, published or tracked in it is current any more. */
  destroy(): void {
    this.lifetime.end();
    this.reset();
  }
}

/**
 * Provides the screen's mutation scope to a component and what it contains. A nested owner shares its parent's scope;
 * a root one owns a scope of its own, current only while the signed-in identity and the address it was reset for hold,
 * and resets it on a sign-in, a change of user or tenant and a new address.
 */
export function provideScreenMutations(): Provider {
  return {
    provide: ScreenMutations,
    useFactory: () => inject(ScreenMutations, { optional: true, skipSelf: true }) ?? rootScreenMutations(),
  };
}

/** The scope of a root owner, in its injection context: it follows the identity and the address and ends with it. */
function rootScreenMutations(): ScreenMutations {
  const router = inject(Router, { optional: true });
  // The session service needs the router: a host without one has no session to follow
  const auth = router ? inject(AuthService, { optional: true }) : null;
  const read = () => JSON.stringify([identityOf(auth?.currentUser()), router?.url]);
  // The follow first runs after this returns, so its reset always finds the scope
  const { lifetime, sync } = followedLifetime(read, () => scope.reset());
  const scope = new ScreenMutations(lifetime);
  const routes = router?.events.subscribe(() => sync());
  inject(DestroyRef).onDestroy(() => {
    routes?.unsubscribe();
    scope.destroy();
  });
  return scope;
}
