import type { DestroyRef } from '@angular/core';
import type { IsCurrent } from '../../core/lifetime';
import type { ScreenOperation } from '../../screen/screen-mutations';
import type { GridService } from '../grid.service';

/** A mutation posted over a grid from its nav, from the moment it begins until its owner finishes it. */
export interface GridNavMutation {
  /** The screen's operation the mutation holds, none outside a screen. */
  readonly operation: ScreenOperation | undefined;
  /**
   * Whether an answer still applies: the owner lives, the grid shows what it showed then and the screen's operation is
   * still current.
   */
  readonly isCurrent: IsCurrent;
  /** Releases the operation, as the owner's destruction does, whichever comes first. */
  readonly finish: () => void;
}

/**
 * Whether an answer to a call over `grid` still applies, captured at the call: the owner of `destroyRef` lives, the
 * grid's actions or a nav element that calls for itself, and the grid shows what it showed then.
 */
export function isOwnerCurrent(grid: GridService, destroyRef: DestroyRef): IsCurrent {
  const isGridCurrent = grid.capture();
  return () => !destroyRef.destroyed && isGridCurrent();
}

/** Begins a mutation over `grid` for the owner of `destroyRef`. */
export function gridNavMutation(grid: GridService, destroyRef: DestroyRef): GridNavMutation {
  const isOwnerStillCurrent = isOwnerCurrent(grid, destroyRef);
  const operation = grid.beginMutation();
  const unregisterDestroy = destroyRef.onDestroy(() => grid.finishMutation(operation));
  return {
    operation,
    isCurrent: () => isOwnerStillCurrent() && (operation?.isCurrent() ?? true),
    finish: () => {
      unregisterDestroy();
      grid.finishMutation(operation);
    },
  };
}
