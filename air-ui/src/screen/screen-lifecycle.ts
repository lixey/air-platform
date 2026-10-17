import { DestroyRef, effect, ErrorHandler, inject, Service, signal } from '@angular/core';
import { type Event as NavigationEvent, NavigationCancel, NavigationEnd, NavigationError, NavigationStart, Router } from '@angular/router';
import { AuthService, identityOf } from '../app/auth/auth.service';
import type { CloseParticipant } from '../core/close-decision';
import { CloseDecision } from '../core/close-decision';
import { Lifetime } from '../core/lifetime';
import type { OverlayRef } from '../overlay/overlay-ref';
import { followedLifetime } from './followed-lifetime';
import { ScreenMutations } from './screen-mutations';
import { unloadWarning } from './unload-warning';

interface ScreenOwner {
  closeParticipant?: CloseParticipant;
  needsProtection: () => boolean;
  reset: () => void;
}

interface ScreenLeave {
  id: number;
  attempts: CloseDecision[];
  overlays: Map<OverlayRef, CloseDecision>;
  ready: Promise<boolean>;
}

/** One routed generation owns drafts, overlays and the navigation that may replace them. */
@Service({ autoProvided: false })
export class ScreenLifecycle {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService, { optional: true });
  private readonly errorHandler = inject(ErrorHandler);
  private readonly rosterLifetime = new Lifetime();
  private readonly owners = new Set<ScreenOwner>();
  private readonly overlays = new Map<OverlayRef, boolean>();
  private readonly followed = followedLifetime(
    () => this.readIdentity(),
    () => this.reset(),
    () => this.isActive() && !this.isDestroyed,
  );
  /**
   * The routed generation's lifetime, the root of the screen's `SCREEN_LIFETIME`: current while the screen is active, not
   * destroyed, and signed in as the identity it last synced; every reset starts a new epoch.
   */
  readonly lifetime = this.followed.lifetime;
  private pendingLeave: ScreenLeave | undefined;
  private isDestroyed = false;
  private readonly _isActive = signal(true);
  readonly isActive = this._isActive.asReadonly();
  private readonly _isLocked = signal(false);
  readonly isLocked = this._isLocked.asReadonly();
  /** The mutation scope of the routed generation: this lifecycle owns it and starts it over on every reset. */
  readonly mutations = new ScreenMutations(this.lifetime);
  /** Follows the navigations while the screen lives, and after its destruction until its pending leave settles. */
  private readonly navigation = this.router.events.subscribe(event => this.follow(event));
  private readonly setUnloadWarning = unloadWarning();

  constructor() {
    effect(() => {
      this.rosterLifetime.epoch();
      this.setUnloadWarning(this.isActive() && [...this.owners].some(owner => owner.needsProtection()));
    });
    inject(DestroyRef).onDestroy(() => {
      this.isDestroyed = true;
      this.lifetime.end();
      this.setUnloadWarning(false);
      if (!this.pendingLeave) {
        this.reset();
      }
      this.stopWhenSettled();
    });
  }

  registerOwner(owner: ScreenOwner): () => void {
    this.owners.add(owner);
    this.rosterLifetime.invalidate();
    return () => {
      this.owners.delete(owner);
      this.rosterLifetime.invalidate();
    };
  }

  ownOverlay(ref: OverlayRef, isDirtyPrompt = false): void {
    this.overlays.set(ref, isDirtyPrompt);
    ref.closed.subscribe(() => this.overlays.delete(ref));
    if (!this.isActive() || this.isLocked()) {
      ref.forceClose();
    }
  }

  requestLeave(navigationId: number): Promise<boolean> {
    this.followed.sync();
    if (!this.isActive()) {
      return Promise.resolve(true);
    }
    if (this.pendingLeave?.id === navigationId) {
      return this.pendingLeave.ready;
    }
    this.cancel();
    const leave: ScreenLeave = { id: navigationId, attempts: [], overlays: new Map(), ready: Promise.resolve(false) };
    this.pendingLeave = leave;
    leave.ready = this.prepare(leave);
    return leave.ready;
  }

  private async prepare(leave: ScreenLeave): Promise<boolean> {
    const isCurrent = this.lifetime.capture();
    const isRosterCurrent = this.rosterLifetime.capture();
    const refs = this.leavingOverlays().reverse();
    /** Whether nothing changed under this leave: the same generation, owners and overlays, and every approval held. */
    const isUnchanged = () =>
      this.pendingLeave === leave &&
      isCurrent() &&
      isRosterCurrent() &&
      this.leavingOverlays().every(ref => refs.includes(ref)) &&
      leave.attempts.every(attempt => attempt.isCurrent());
    for (const ref of refs) {
      const attempt = ref.prepareClose();
      leave.attempts.push(attempt);
      leave.overlays.set(ref, attempt);
      if (!(await attempt.ready) || this.pendingLeave !== leave) {
        this.cancel(leave);
        return false;
      }
    }
    const routed = new CloseDecision({
      participants: [...this.owners].flatMap(owner => (owner.closeParticipant ? [owner.closeParticipant] : [])),
      isOwnerCurrent: isCurrent,
      errorHandler: this.errorHandler,
    });
    leave.attempts.push(routed);
    if (!(await routed.ready) || !isUnchanged()) {
      this.cancel(leave);
      return false;
    }
    this._isLocked.set(true);
    return true;
  }

  /** The overlays a leave closes: every one the screen owns but its own dirty prompts. */
  private leavingOverlays(): OverlayRef[] {
    return [...this.overlays].filter(([, isDirtyPrompt]) => !isDirtyPrompt).map(([ref]) => ref);
  }

  private cancel(leave = this.pendingLeave): void {
    if (!leave) {
      return;
    }
    for (const attempt of leave.attempts) {
      attempt.cancel();
    }
    if (this.pendingLeave === leave) {
      this.pendingLeave = undefined;
      this._isLocked.set(false);
      this.stopWhenSettled();
    }
  }

  private reset(): void {
    const leave = this.pendingLeave;
    this._isActive.set(false);
    this.lifetime.invalidate();
    this.mutations.reset();
    this.cancel();
    for (const owner of this.owners) {
      owner.reset();
    }
    for (const ref of this.overlays.keys()) {
      ref.forceClose(leave?.overlays.get(ref)?.request);
    }
    this.setUnloadWarning(false);
  }

  private readIdentity(): string {
    return identityOf(this.auth?.currentUser());
  }

  /** Settles the pending leave by the navigation it waits for, and makes the screen active again once one ends. */
  private follow(event: NavigationEvent): void {
    const leave = this.pendingLeave;
    if (event instanceof NavigationStart && leave && event.id !== leave.id) {
      this.cancel();
    } else if (event instanceof NavigationEnd) {
      if (event.id === leave?.id) {
        this.reset();
      }
      if (!this.isDestroyed) {
        this._isActive.set(true);
      }
    } else if ((event instanceof NavigationCancel || event instanceof NavigationError) && event.id === leave?.id) {
      this.cancel();
    }
  }

  /** A destroyed screen stops following the navigations once no leave waits for one. */
  private stopWhenSettled(): void {
    if (this.isDestroyed && !this.pendingLeave) {
      this.navigation.unsubscribe();
    }
  }
}
