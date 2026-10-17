import { effect, untracked } from '@angular/core';
import { type IsCurrent, Lifetime } from '../core/lifetime';

/**
 * A lifetime current while `read` answers the key it answered at the last sync and `isOuterCurrent` holds. A change
 * of the key, seen by the effect this installs over every signal `read` reads or by `sync` for what no signal carries,
 * stores the new key and runs `reset` untracked. Call it in an injection context; the effect ends with it.
 */
export function followedLifetime(
  read: () => string,
  reset: () => void,
  isOuterCurrent: IsCurrent = () => true,
): { readonly lifetime: Lifetime; readonly sync: () => void } {
  let current = read();
  const lifetime = new Lifetime(() => isOuterCurrent() && read() === current);
  const sync = () => {
    const next = read();
    if (next !== current) {
      current = next;
      untracked(reset);
    }
  };
  effect(() => sync());
  return { lifetime, sync };
}
