import '@angular/compiler';
import { describe, expect, it } from 'vitest';
import { Lifetime } from '../core/lifetime';
import { FormChildScope } from './form-child-scope';

describe('FormChildScope multi-tab synchronization', () => {
  it('notifies onChange when a child mutation commits and suppresses re-entrant onChange during external refresh', () => {
    const parentLifetime = new Lifetime();
    const tabLines = new FormChildScope(parentLifetime);
    const tabMovements = new FormChildScope(parentLifetime);
    const tabHistory = new FormChildScope(parentLifetime);

    let parentRefreshCount = 0;
    let movementsReloadCount = 0;
    let historyReloadCount = 0;

    tabMovements.mutations.subscribe({
      accepts: event => event.service === 'ecom/order-movements',
      refresh: () => {
        movementsReloadCount++;
      },
      invalidate: () => undefined,
    });
    tabHistory.mutations.subscribe({
      accepts: event => event.service === 'ecom/order-history',
      refresh: () => {
        historyReloadCount++;
      },
      invalidate: () => undefined,
    });

    // Simulate FormChildScreenComponent wiring: when tabLines mutates, parent form refreshes and notifies sibling tabs
    tabLines.onChange(() => {
      parentRefreshCount++;
      tabMovements.refresh('ecom/order-movements');
      tabHistory.refresh('ecom/order-history');
    });
    tabMovements.onChange(() => {
      parentRefreshCount++;
    });
    tabHistory.onChange(() => {
      parentRefreshCount++;
    });

    // Trigger a committed mutation in tabLines (e.g. adding or editing an order line)
    const op = tabLines.mutations.begin();
    tabLines.mutations.publish(op, { service: 'ecom/order-lines', operation: 'save', rowId: 42 });
    tabLines.mutations.finish(op);

    expect(parentRefreshCount).toBe(1);
    expect(movementsReloadCount).toBe(1);
    expect(historyReloadCount).toBe(1);

    parentLifetime.end();
  });
});
