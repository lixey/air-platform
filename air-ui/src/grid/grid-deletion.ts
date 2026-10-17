import { computed, linkedSignal } from '@angular/core';
import type { MessageDto } from '../core/generated/contracts';
import type { MutationFailure } from '../core/mutation-failure';
import type { GridRowKey } from './grid-row';

interface DeleteResult extends MutationFailure {
  readonly ids: readonly GridRowKey[];
}

interface DeletionState {
  readonly pending: symbol | undefined;
  readonly isBusy: boolean;
  readonly result: DeleteResult | null;
  readonly needsVerification: boolean;
}

const IDLE: DeletionState = { pending: undefined, isBusy: false, result: null, needsVerification: false };

/**
 * One grid's attempted deletion; row and nav changes do not discard its diagnostics, a change of `resetBy` does, and
 * forgets the attempt in flight.
 */
export class GridDeletion {
  private readonly state = linkedSignal<unknown, DeletionState>({ source: () => this.resetBy(), computation: () => IDLE });

  readonly isBusy = computed(() => this.state().isBusy);
  readonly result = computed(() => this.state().result);
  readonly needsVerification = computed(() => this.state().needsVerification);
  readonly isBlocked = computed(() => this.isBusy() || this.needsVerification());
  readonly groups = computed(() => {
    const groups = new Map<number | null, { recordId: number | null; label?: string | null; messages: MessageDto[] }>();
    for (const message of this.result()?.messages ?? []) {
      const id = message.recordId ?? null;
      let group = groups.get(id);
      if (!group) {
        group = { recordId: id, label: message.recordLabel, messages: [] };
        groups.set(id, group);
      }
      group.label ??= message.recordLabel;
      group.messages.push(message);
    }
    const general = groups.get(null);
    groups.delete(null);
    return [...(general ? [general] : []), ...groups.values()];
  });
  readonly errorIds = computed(() => this.groups().flatMap(group => (group.recordId === null ? [] : [group.recordId])));

  constructor(private readonly resetBy: () => unknown = () => null) {}

  begin(): symbol | undefined {
    if (this.isBlocked()) {
      return undefined;
    }
    const pending = Symbol();
    this.state.update(state => ({ ...state, pending, isBusy: true }));
    return pending;
  }

  finish(token: symbol | undefined): void {
    if (token && token === this.state().pending) {
      this.state.update(state => ({ ...state, pending: undefined, isBusy: false }));
    }
  }

  commit(token: symbol | undefined): void {
    if (!token || token !== this.state().pending) {
      return;
    }
    this.state.update(state => ({ ...state, result: null, needsVerification: false }));
  }

  fail(token: symbol | undefined, ids: readonly GridRowKey[], failure: MutationFailure): void {
    if (!token || token !== this.state().pending) {
      return;
    }
    const result: DeleteResult = {
      ...failure,
      ids: [...ids],
      messages: failure.messages.map(message => ({ ...message })),
    };
    this.state.update(state => ({ ...state, result, needsVerification: failure.outcome === 'conflict' || failure.outcome === 'unknown' }));
  }

  verified(result: DeleteResult | null): void {
    if (result && result === this.result()) {
      this.state.update(state => ({ ...state, needsVerification: false }));
    }
  }
}
