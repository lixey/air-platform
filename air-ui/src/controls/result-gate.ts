import { linkedSignal, type Signal, type WritableSignal } from '@angular/core';

/**
 * Whether a result of the overlay a control opened is written: the control raises it when it opens the overlay,
 * and it falls each time `refusesInput` changes, so a result that comes after the control refused input is dropped.
 */
export function resultGate(refusesInput: Signal<boolean>): WritableSignal<boolean> {
  return linkedSignal({ source: refusesInput, computation: () => false });
}
