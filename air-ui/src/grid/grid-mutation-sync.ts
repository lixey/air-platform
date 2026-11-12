import { untracked } from '@angular/core';
import { type AuthService, identityOf } from '../app/auth/auth.service';
import type { GridModel } from '../core/generated/contracts';
import type { ScreenMutations, ScreenOperation } from '../screen/screen-mutations';
import type { GridRowKey } from './grid-row';

export interface LiveGridInstance {
  readonly service: () => string | null;
  readonly reload: () => void;
}

/** Process-wide registry of mounted `GridService` instances and reload listeners (e.g. live reports). */
export class GridLiveRegistry {
  private readonly grids = new Set<LiveGridInstance>();
  private readonly reloadListeners = new Set<() => void>();

  add(grid: LiveGridInstance): void {
    this.grids.add(grid);
  }

  delete(grid: LiveGridInstance): void {
    this.grids.delete(grid);
  }

  onReloadServices(listener: () => void): () => void {
    this.reloadListeners.add(listener);
    return () => this.reloadListeners.delete(listener);
  }

  reloadServices(services?: readonly string[], options?: { readonly notifyListeners?: boolean }): void {
    const filter = services && services.length > 0 ? new Set(services.map(s => s.toLowerCase())) : null;
    for (const grid of this.grids) {
      const svc = grid.service()?.toLowerCase();
      if (!filter || (svc && filter.has(svc))) {
        grid.reload();
      }
    }
    if (options?.notifyListeners !== false) {
      for (const listener of this.reloadListeners) {
        listener();
      }
    }
  }
}

export interface GridMutationSyncOptions {
  readonly auth: AuthService;
  readonly mutations: ScreenMutations | null;
  readonly model: () => GridModel | null;
  readonly followsForm: (service: string | null | undefined) => boolean;
  readonly isTrackingForm: () => boolean;
  readonly expectChange: (recordId: GridRowKey) => void;
  readonly reload: () => void;
  readonly onIdentityChanged: () => void;
  readonly cancelPage: () => void;
}

/**
 * Coordinates `ScreenMutations` subscriptions, identity invalidation checks, and in-flight `ScreenOperation` tracking
 * for one `GridService`.
 */
export class GridMutationSync {
  private readonly operations = new Set<ScreenOperation>();
  private unsubscribeMutations: (() => void) | undefined;
  private lastIdentity: string;

  constructor(private readonly options: GridMutationSyncOptions) {
    this.lastIdentity = identityOf(options.auth.currentUser());
    this.subscribe();
  }

  begin(): ScreenOperation | undefined {
    const operation = this.options.mutations?.begin();
    if (operation) {
      this.operations.add(operation);
    }
    return operation;
  }

  finish(operation: ScreenOperation | undefined): void {
    if (operation) {
      this.operations.delete(operation);
      this.options.mutations?.finish(operation);
    }
  }

  release(): void {
    this.unsubscribeMutations?.();
    this.unsubscribeMutations = undefined;
    for (const operation of this.operations) {
      this.options.mutations?.finish(operation);
    }
    this.operations.clear();
  }

  private subscribe(): void {
    this.unsubscribeMutations = this.options.mutations?.subscribe({
      accepts: event => {
        const model = untracked(this.options.model);
        return (
          !!model &&
          ((event.operation === 'save' ? untracked(() => this.options.followsForm(event.service)) : event.service === model.service) ||
            !!event.affected?.some(a => a.service === model.service))
        );
      },
      refresh: event => {
        if (event?.operation === 'save' && event.recordId != null && this.options.isTrackingForm()) {
          this.options.expectChange(event.recordId);
        }
        this.options.reload();
      },
      invalidate: () => {
        const nextIdentity = identityOf(this.options.auth.currentUser());
        if (nextIdentity !== this.lastIdentity) {
          this.lastIdentity = nextIdentity;
          this.options.onIdentityChanged();
        } else {
          this.options.cancelPage();
        }
        this.subscribe();
      },
    });
  }
}
