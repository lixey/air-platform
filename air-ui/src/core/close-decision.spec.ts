import type { ErrorHandler } from '@angular/core';
import { describe, expect, it, vi } from 'vitest';
import { type CloseApproval, CloseDecision } from './close-decision';

function makeErrorHandler(): { handler: ErrorHandler; errors: unknown[] } {
  const errors: unknown[] = [];
  return {
    errors,
    handler: { handleError: (error: unknown) => errors.push(error) },
  };
}

describe('CloseDecision', () => {
  it('collects participant approvals in order, reshapes result/cause, commits once, and releases receipts', async () => {
    const { handler, errors } = makeErrorHandler();
    const order: string[] = [];
    const cause = Symbol('saved');
    let committedRequest: unknown;

    const decision = new CloseDecision({
      request: { result: 'initial' },
      isOwnerCurrent: () => true,
      errorHandler: handler,
      onCommit: req => {
        order.push('commit');
        committedRequest = { result: req.result, cause: req.cause };
      },
      participants: [
        req => {
          order.push(`p1:${String(req.result)}`);
          return {
            result: 'reshaped',
            cause,
            release: () => order.push('release-1'),
          };
        },
        async req => {
          order.push(`p2:${String(req.result)}`);
          return {
            release: () => order.push('release-2'),
          };
        },
      ],
    });

    expect(await decision.ready).toBe(true);
    expect(decision.isCurrent()).toBe(true);
    expect(decision.request.result).toBe('reshaped');
    expect(decision.request.cause).toBe(cause);

    expect(decision.commit()).toBe(true);
    expect(await decision.done).toBe(true);
    expect(decision.isCurrent()).toBe(false);
    expect(decision.commit()).toBe(false);
    expect(committedRequest).toEqual({ result: 'reshaped', cause });
    expect(order).toEqual(['p1:initial', 'p2:reshaped', 'commit', 'release-1', 'release-2']);
    expect(errors).toEqual([]);
  });

  it('stops on the first refusing participant, aborts the signal, and releases earlier receipts', async () => {
    const { handler } = makeErrorHandler();
    const released: string[] = [];
    let thirdCalled = false;
    let observedSignal: AbortSignal | undefined;

    const decision = new CloseDecision({
      isOwnerCurrent: () => true,
      errorHandler: handler,
      participants: [
        req => {
          observedSignal = req.signal;
          return { release: () => released.push('first') };
        },
        () => false,
        () => {
          thirdCalled = true;
          return true;
        },
      ],
    });

    expect(await decision.ready).toBe(false);
    expect(await decision.done).toBe(false);
    expect(thirdCalled).toBe(false);
    expect(observedSignal?.aborted).toBe(true);
    expect(released).toEqual(['first']);
  });

  it('refuses commit when owner or an approval receipt becomes stale before commit', async () => {
    const { handler } = makeErrorHandler();
    let receiptCurrent = true;
    const release = vi.fn();
    const onCommit = vi.fn();

    const decision = new CloseDecision({
      isOwnerCurrent: () => true,
      errorHandler: handler,
      onCommit,
      participants: [(): CloseApproval => ({ isCurrent: () => receiptCurrent, release })],
    });

    expect(await decision.ready).toBe(true);
    receiptCurrent = false;
    expect(decision.isCurrent()).toBe(false);
    expect(decision.commit()).toBe(false);
    expect(await decision.done).toBe(false);
    expect(onCommit).not.toHaveBeenCalled();
    expect(release).toHaveBeenCalledTimes(1);
  });

  it('isolates participant, commit, and release errors via ErrorHandler without stranding remaining receipts', async () => {
    const { handler, errors } = makeErrorHandler();
    const secondRelease = vi.fn();

    const decision = new CloseDecision({
      isOwnerCurrent: () => true,
      errorHandler: handler,
      onCommit: () => {
        throw new Error('commit-boom');
      },
      participants: [
        () => ({
          release: () => {
            throw new Error('release-boom');
          },
        }),
        () => ({ release: secondRelease }),
      ],
    });

    expect(await decision.ready).toBe(true);
    expect(decision.commit()).toBe(false);
    expect(await decision.done).toBe(false);
    expect(secondRelease).toHaveBeenCalledTimes(1);
    expect(errors.map(err => (err as Error).message)).toEqual(['release-boom', 'commit-boom']);
  });
});
