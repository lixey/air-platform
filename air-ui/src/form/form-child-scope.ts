import type { CloseApproval, CloseAttempt, CloseDecision } from '../core/close-decision';
import { Lifetime } from '../core/lifetime';
import type { OverlayRef } from '../overlay/overlay-ref';
import { ScreenMutations } from '../screen/screen-mutations';

/**
 * One child screen owns its requests, notifications and descendant overlays. Its lifetime, the child page's
 * `SCREEN_LIFETIME`, is a child of its parent's; whenever the parent record changes it starts a new epoch and its
 * mutation scope starts over. It tells whoever asked of every change in it: the form of the layout the page stands in.
 */
export class FormChildScope {
  readonly lifetime: Lifetime;
  private readonly overlays = new Map<OverlayRef, boolean>();
  private readonly heldAttempts = new Map<OverlayRef, CloseDecision>();
  readonly mutations: ScreenMutations;
  private readonly changes = new Set<() => void>();

  constructor(parent: Lifetime) {
    this.lifetime = parent.child();
    this.mutations = new ScreenMutations(this.lifetime);
    this.listen();
  }

  /**
   * Tells `changed` of every committed mutation published in the child's scope and of every tracked close in it; the
   * registration outlives the scope's resets. Answers what cancels it.
   */
  onChange(changed: () => void): () => void {
    this.changes.add(changed);
    return () => this.changes.delete(changed);
  }

  capture(): () => boolean {
    return this.lifetime.capture();
  }

  ownOverlay(ref: OverlayRef, isDirtyPrompt: boolean): void {
    if (!this.lifetime.isCurrent()) {
      ref.forceClose();
      return;
    }
    this.overlays.set(ref, isDirtyPrompt);
    ref.closed.subscribe(() => {
      this.overlays.delete(ref);
      this.heldAttempts.delete(ref);
    });
  }

  hasOverlays(): boolean {
    return this.overlays.size > 0;
  }

  async prepareClose(request: CloseAttempt): Promise<CloseApproval | false> {
    const isCurrent = this.capture();
    const refs = [...this.overlays]
      .filter(([, isDirtyPrompt]) => !isDirtyPrompt)
      .map(([ref]) => ref)
      .reverse();
    const attempts: CloseDecision[] = [];
    const cancel = () => attempts.forEach(attempt => attempt.cancel());
    request.signal?.addEventListener('abort', cancel, { once: true });
    const release = () => {
      request.signal?.removeEventListener('abort', cancel);
      cancel();
    };
    for (const ref of refs) {
      if (!isCurrent() || request.signal?.aborted) {
        release();
        return false;
      }
      const attempt = ref.prepareClose();
      this.heldAttempts.set(ref, attempt);
      attempts.push(attempt);
      if (!(await attempt.ready)) {
        release();
        return false;
      }
    }
    return {
      isCurrent: () =>
        isCurrent() &&
        !request.signal?.aborted &&
        [...this.overlays].every(([ref, isDirtyPrompt]) => isDirtyPrompt || refs.includes(ref)) &&
        attempts.every(attempt => attempt.isCurrent()),
      release,
    };
  }

  reset(): void {
    // Invalidate subscribers before closed events can ask for another old-parent page.
    this.lifetime.invalidate();
    this.mutations.reset();
    for (const ref of [...this.overlays.keys()].reverse()) {
      ref.forceClose(this.heldAttempts.get(ref)?.request);
    }
    this.overlays.clear();
    this.heldAttempts.clear();
  }

  destroy(): void {
    this.lifetime.end();
    this.reset();
    this.changes.clear();
  }

  /** Hears every mutation of the scope, one call a mutation and the close it causes; a reset of the scope subscribes again. */
  private listen(): void {
    this.mutations.subscribe({
      accepts: () => true,
      refresh: () => {
        for (const changed of [...this.changes]) {
          changed();
        }
      },
      invalidate: () => this.listen(),
    });
  }
}
